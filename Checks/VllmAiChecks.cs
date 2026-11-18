using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Checks;

public static class VllmAiChecks
{
    public static async Task RunAsync()
    {
        await CommitteeApprovalOrderChecks.RunAsync();
        await FinalReportChecks.RunAsync();
        await RankedReportChecks.RunAsync();
        await ReportContentChecks.RunAsync();

        // 1. Default local vLLM resolution + thinking disabled
        var defaultCfg = new ConfigurationBuilder().Build();
        var (localUri, isLoopback, _) = VllmSettings.ResolveEndpoint(defaultCfg);
        Require(isLoopback && localUri.ToString() == "http://localhost:8000/v1/chat/completions", "Default vLLM endpoint mismatch.");
        Require(VllmSettings.Model(defaultCfg) == "Qwen/Qwen3-8B-AWQ", "Default vLLM model mismatch.");
        Require(!VllmSettings.EnableThinking(defaultCfg), "Thinking must be disabled by default.");

        // 2. Remote endpoint security: block plain HTTP and require API key on remote HTTPS
        var insecureCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vllm:BaseUrl"] = "http://example.com/v1", ["Vllm:ApiKey"] = "sk-test" })
            .Build();
        ExpectError(() => VllmSettings.ResolveEndpoint(insecureCfg), "Remote plain HTTP must be rejected.");

        var missingKeyCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vllm:BaseUrl"] = "https://api.groq.com/openai/v1", ["Vllm:ApiKey"] = "PASTE_YOUR_API_KEY_HERE" })
            .Build();
        ExpectError(() => VllmSettings.ResolveEndpoint(missingKeyCfg), "Placeholder API key on remote host must be rejected.");

        // 3. Role-based tool isolation + no demo seeder tools
        foreach (var role in RoleNames.Primary)
        {
            var tools = AiChatWorkspaceActions.GetAuthorizedTools(role).Select(t => t.Name).ToHashSet();
            Require(tools.Contains("navigate_to_page"), $"{role} must include navigate_to_page.");
            Require(!tools.Any(n => n.Contains("demo", StringComparison.OrdinalIgnoreCase)),
                $"{role} must not contain any demo data seeding tools.");
        }

        var supplierTools = AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.Supplier).Select(t => t.Name).ToHashSet();
        Require(supplierTools.Contains("supplier_upsert_offer") && supplierTools.Contains("supplier_respond_invitation"),
            "Supplier tools missing expected entries.");
        Require(!supplierTools.Any(n => n.StartsWith("evaluator_") || n.StartsWith("manager_") || n.StartsWith("admin_") || n.StartsWith("officer_")),
            "Supplier must never receive internal committee, officer, manager, or admin tools.");

        var evalTools = AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.EvaluationCommittee).Select(t => t.Name).ToHashSet();
        Require(evalTools.Contains("evaluator_score_offers") && evalTools.Contains("evaluator_confirm_weight"),
            "EvaluationCommittee tools missing expected entries.");
        Require(!evalTools.Any(n => n.StartsWith("supplier_") || n.StartsWith("admin_") || n.StartsWith("manager_")),
            "EvaluationCommittee must not receive supplier, manager, or admin tools.");

        // 4. Remote HTTPS API-key flow + automatic 400 fallback + <think> stripping + Agentic navigation & action loop
        var cloudCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vllm:BaseUrl"] = "https://api.together.xyz/v1",
                ["Vllm:Model"] = "Qwen/Qwen3-32B",
                ["Vllm:ApiKey"] = "test_bearer_api_token_123",
                ["Vllm:EnableThinking"] = "false"
            })
            .Build();

        var cloudHandler = new AgenticCloudMockHandler();
        using var http = new HttpClient(cloudHandler);
        var chat = new AiChatClient(http, cloudCfg);

        var initialSnap = new AiChatWorkspaceSnapshot(
            new { FullName = "Rami Saleh", ActiveWorkspace = "Evaluation Committee" },
            new { TendersInWorkspaceCount = 0 },
            null,
            null,
            null);

        // 4a. Natural capability question -> smart reply without canned fallback
        var capResult = await chat.RunAsync(
            RoleNames.EvaluationCommittee,
            ["Dashboard", "Evaluations", "Combined Reports"],
            "what actions can you do for me here?",
            [],
            initialSnap,
            AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.EvaluationCommittee),
            (_, _) => Task.FromResult(new AiChatToolExecutionResult(false, false, "Should not run")),
            _ => Task.FromResult(initialSnap),
            CancellationToken.None);

        Require(!capResult.Mutated && capResult.NavigateUrl == null &&
                capResult.Answer.Contains("Evaluation Committee", StringComparison.OrdinalIgnoreCase),
            "Capability question should return a smart natural reply.");
        Require(cloudHandler.SawBearerAuth && cloudHandler.FallbackTriggered, "Bearer API key or 400 json_object fallback did not run.");

        // 4b. Agentic page transfer (navigate_to_page) -> propagates NavigateUrl
        var navToolExecuted = false;
        var navResult = await chat.RunAsync(
            RoleNames.EvaluationCommittee,
            ["Dashboard", "Evaluations", "Combined Reports"],
            "Transfer me to Combined Reports",
            [new AiChatHistoryTurn("user", "what actions can you do?"), new AiChatHistoryTurn("assistant", capResult.Answer)],
            initialSnap,
            AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.EvaluationCommittee),
            (call, _) =>
            {
                navToolExecuted = call.ToolName == "navigate_to_page";
                return Task.FromResult(new AiChatToolExecutionResult(
                    true,
                    false,
                    "Transferring user to Combined Reports (/EvaluationCommittee/Reports).",
                    "/EvaluationCommittee/Reports",
                    "Open Combined Reports",
                    "/EvaluationCommittee/Reports"));
            },
            _ => Task.FromResult(initialSnap),
            CancellationToken.None);

        Require(navToolExecuted && navResult.NavigateUrl == "/EvaluationCommittee/Reports",
            "Agentic page transfer failed to execute navigate_to_page and return NavigateUrl.");
        Require(cloudHandler.SawMultiTurnMessages, "Multi-turn conversation history was not sent to LLM.");

        Console.WriteLine("PASS: vLLM /v1/chat/completions, thinking=false, Bearer API key auth, role-scoped actions, page transfer (navigate_to_page), and Agentic AiChatClient.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void ExpectError(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception(message);
    }

    private sealed class AgenticCloudMockHandler : HttpMessageHandler
    {
        public bool SawBearerAuth;
        public bool FallbackTriggered;
        public bool SawMultiTurnMessages;
        private int _callCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _callCount++;
            SawBearerAuth = request.Headers.Authorization?.Scheme == "Bearer" &&
                            request.Headers.Authorization?.Parameter == "test_bearer_api_token_123";

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var rfType = body.RootElement.GetProperty("response_format").GetProperty("type").GetString();
            if (body.RootElement.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 2)
                SawMultiTurnMessages = true;

            if (_callCount == 1 && rfType == "json_schema")
            {
                FallbackTriggered = true;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"json_schema fallback test\"}", Encoding.UTF8, "application/json")
                };
            }

            var payload = _callCount switch
            {
                2 => "<think>hidden</think>{\"mode\":\"reply\",\"reply\":\"In your Evaluation Committee workspace, I can confirm your financial weight, save your offer marks, submit reports, and transfer you to any Evaluation Committee page.\",\"toolCalls\":[]}",
                3 => "{\"mode\":\"tool_calls\",\"reply\":\"\",\"toolCalls\":[{\"toolName\":\"navigate_to_page\",\"argumentsJson\":\"{\\\"page\\\":\\\"Reports\\\",\\\"recordId\\\":null}\"}]}",
                _ => "{\"answer\":\"Taking you to Combined Reports now.\"}"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { finish_reason = "stop", message = new { content = payload } } }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
