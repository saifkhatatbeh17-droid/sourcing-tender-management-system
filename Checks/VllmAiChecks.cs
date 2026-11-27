using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderManagement.Controllers;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services;
using SourcingTenderManagement.Services.Chat;
using SourcingTenderManagement.Services.Intelligence;
using SourcingTenderManagement.Services.TopCommittee;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website. Run it with: dotnet run -- --vllm-ai-checks

public static class VllmAiChecks
{
    internal static IConfiguration LocalFixtureConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vllm:BaseUrl"] = "http://localhost:8000/v1",
            ["Vllm:Model"] = "fixture/model"
        })
        .Build();

    public static async Task RunAsync()
    {
        await AiChatAttachmentChecks.RunAsync();
        await AiGatewayChecks.RunAsync();
        await OfferPreCheckChecks.RunAsync();
        await SpecMatchChecks.RunAsync();
        await CheckElevenLabsSpeechAsync();
        await CommitteeApprovalOrderChecks.RunAsync();
        await FinalReportChecks.RunAsync();
        await RankedReportChecks.RunAsync();
        await ReportContentChecks.RunAsync();

        // 1. Default local vLLM resolution + thinking disabled
        var defaultCfg = LocalFixtureConfiguration();
        var (localUri, isLoopback, _) = VllmSettings.ResolveEndpoint(defaultCfg);
        Require(isLoopback
            && localUri.ToString() == "http://localhost:8000/v1/chat/completions", "Default vLLM endpoint mismatch.");
        Require(VllmSettings.Model(defaultCfg) == "fixture/model", "The configured model must be used.");
        Require(!VllmSettings.EnableThinking(defaultCfg), "Thinking must be disabled by default.");

        // 2. Remote endpoint security: block plain HTTP and require API key on remote HTTPS
        var insecureCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vllm:BaseUrl"] = "http://example.com/v1", ["Vllm:ApiKey"] = "sk-test" })
            .Build();
        ExpectError(() => VllmSettings.ResolveEndpoint(insecureCfg), "Remote plain HTTP must be rejected.");

        var missingKeyCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,
                string?> { ["Vllm:BaseUrl"] = "https://api.groq.com/openai/v1",
                ["Vllm:ApiKey"] = "PASTE_YOUR_API_KEY_HERE" })
            .Build();
        ExpectError(() => VllmSettings.ResolveEndpoint(missingKeyCfg), "Placeholder API key on remote host must be rejected.");

        // 3. Role-based tool isolation + no demo seeder tools
        foreach (var role in RoleNames.Primary)
        {
            var tools = AiChatWorkspaceActions.GetAuthorizedTools(role).Select(t => t.Name).ToHashSet();
            Require(tools.Contains("navigate_to_page"), $"{role} must include navigate_to_page.");
            Require(tools.Contains("inspect_record_details"), $"{role} must include inspect_record_details.");
            Require(tools.Contains("export_downloadable_file"), $"{role} must include export_downloadable_file.");
            Require(!tools.Any(n => n.Contains("demo", StringComparison.OrdinalIgnoreCase)),
                $"{role} must not contain any demo data seeding tools.");
        }

        var supplierTools = AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.Supplier)
            .Select(t => t.Name)
            .ToHashSet();
        Require(supplierTools.Contains("supplier_upsert_offer") && supplierTools.Contains("supplier_respond_invitation"),
            "Supplier tools missing expected entries.");
        Require(!supplierTools.Any(n => n.StartsWith("evaluator_") || n.StartsWith("manager_") || n.StartsWith("admin_") || n.StartsWith("officer_")),
            "Supplier must never receive internal committee, officer, manager, or admin tools.");

        var evalTools = AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.EvaluationCommittee)
            .Select(t => t.Name)
            .ToHashSet();
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
        Require(cloudHandler.SawBearerAuth
            && cloudHandler.FallbackTriggered, "Bearer API key or 400 json_object fallback did not run.");

        // 4b. Agentic page transfer (navigate_to_page) -> propagates NavigateUrl
        var navToolExecuted = false;
        var priorTurns = Enumerable.Range(0, 6)
            .SelectMany(i => new[]
            {
                new AiChatHistoryTurn("user", $"Prior question {i}"),
                new AiChatHistoryTurn("assistant", $"Prior reply {i}")
            })
            .Concat([
                new AiChatHistoryTurn("user", "Which page has the reports?"),
                new AiChatHistoryTurn("assistant", new string('x', 800) + " Open Combined Reports.")
            ]).ToArray();
        var navResult = await chat.RunAsync(
            RoleNames.EvaluationCommittee,
            ["Dashboard", "Evaluations", "Combined Reports"],
            "Take me there",
            priorTurns,
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
        Require(cloudHandler.RequestsWithFullHistory == 2,
            "Decision and synthesis must both receive the latest 12 messages and preserve the prior reply.");

        // 5. Groq cooldown beyond the interactive retry budget
        var groqCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vllm:BaseUrl"] = "https://api.groq.com/openai/v1",
                ["Vllm:Model"] = "qwen/qwen3.8-27b",
                ["Vllm:FallbackModel"] = "openai/gpt-oss-120b",
                ["Vllm:ApiKey"] = "gsk_test_token_123",
                ["Vllm:EnableThinking"] = "false"
            })
            .Build();

        var groqMock = new GroqRateLimitMockHandler();
        using var groqHttp = new HttpClient(groqMock);
        var groqChat = new AiChatClient(groqHttp, groqCfg);
        var groqReply = await groqChat.RunAsync(
            RoleNames.SourcingOfficer,
            ["Dashboard", "Requests"],
            "Summarize my queue",
            [],
            initialSnap,
            AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.SourcingOfficer),
            (_, _) => throw new InvalidOperationException("No action should run during an outage."),
            _ => Task.FromResult(initialSnap),
            CancellationToken.None);

        Require(groqReply.WorkspaceOnly && !groqReply.Mutated && groqReply.Answer.Contains("Requests"),
            "A long Groq cooldown should return workspace help.");
        Require(groqMock.SawQwenEffortNone && groqMock.Calls == 2,
            "Safi must try Qwen first, then GPT-OSS 120B once during a long cooldown.");

        using var recoveryHttp = new HttpClient(new GroqRateLimitMockHandler(recover: true));
        var recoveryChat = new AiChatClient(recoveryHttp, groqCfg);
        var recovered = await recoveryChat.RunAsync(
            RoleNames.SourcingOfficer, ["Requests"], "Summarize my queue", [], initialSnap, [],
            (_, _) => throw new InvalidOperationException("No action was requested."),
            _ => Task.FromResult(initialSnap), CancellationToken.None);
        Require(!recovered.WorkspaceOnly && recovered.Answer == "Queue is clear.",
            "Safi should return the valid fallback reply after a Qwen rate limit.");

        using var scopeHttp = new HttpClient(new GroqRateLimitMockHandler(recover: true, outsideScope: true));
        var scopeChat = new AiChatClient(scopeHttp, groqCfg);
        var scopeResult = await scopeChat.RunAsync(
            RoleNames.EvaluationCommittee, ["Evaluations"], "اقترح لي هدية لعيد ميلاد", [], initialSnap, [],
            (_, _) => throw new InvalidOperationException("Off-topic questions must not call tools."),
            _ => Task.FromResult(initialSnap), CancellationToken.None);
        Require(scopeResult.Answer == "بقدر أساعدك فقط بأسئلة Tiryaq ضمن دورك." &&
                !scopeResult.WorkspaceOnly && !scopeResult.Mutated,
            "An out-of-scope model decision must produce the localized refusal.");

        // 5b. Recover from structured output rejection using the same model.
        var formatMock = new GroqFormatMockHandler();
        using var formatHttp = new HttpClient(formatMock);
        var formatChat = new AiChatClient(formatHttp, groqCfg);
        var formatReply = await formatChat.RunAsync(
            RoleNames.EvaluationCommittee,
            ["Dashboard", "Evaluations"],
            "Summarize my queue",
            [],
            initialSnap,
            AiChatWorkspaceActions.GetAuthorizedTools(RoleNames.EvaluationCommittee),
            (_, _) => Task.FromResult(new AiChatToolExecutionResult(false, false, "Should not run")),
            _ => Task.FromResult(initialSnap),
            CancellationToken.None);

        Require(formatReply.Answer.Contains("Recovered via fallback", StringComparison.OrdinalIgnoreCase),
            "Groq 400 plain-JSON recovery failed.");
        Require(formatMock.Saw400JsonObjectFallback && formatMock.SawNoResponseFormatRetry,
            "Expected same-model response format recovery did not execute.");

        // 6. Per-user download isolation + real PDF/Excel binary generation
        var sampleExport = new AiChatExportDocument(
            "Tender #191 Summary — Sterile Amber Vials",
            "Tender #191 · Sourcing Officer Workspace",
            "Tender-191-Summary",
            [("Tender ID", "#191"), ("Status", "Open"), ("Compliant Offers", "3")],
            "Three compliant supplier quotations have been submitted and are ready for Evaluation Committee handoff.",
            ["Lowest offer: 4,200.00 JOD (7 days)", "All mandatory specifications satisfied"],
            [
                new AiChatExportTable(
                    "Submitted Supplier Offers",
                    ["Offer", "Supplier", "Compliance", "Total", "Delivery"],
                    [
                        ["#301", "Al-Shifa Pharma", "Compliant", "4,200.00 JOD", "7 days"],
                        ["#302", "Petra Medical", "Compliant", "4,450.00 JOD", "10 days"]
                    ])
            ]);
        var pdfBytes = AiChatExportService.RenderPdfDocument(sampleExport, "Sourcing Officer");
        var xlsxBytes = AiChatExportService.RenderExcelDocument(sampleExport, "Sourcing Officer");
        Require(pdfBytes.Length > 500
            && pdfBytes[0] == (byte)'%'
            && pdfBytes[1] == (byte)'P', "Generated PDF binary invalid.");
        Require(xlsxBytes.Length > 500
            && xlsxBytes[0] == (byte)'P'
            && xlsxBytes[1] == (byte)'K', "Generated Excel XLSX binary invalid.");

        var store = new AiChatDownloadStore();
        var savedPdf = store.Save("user-1", RoleNames.SourcingOfficer, "Tender-191-Summary.pdf", "application/pdf", "Download PDF", "pdf", pdfBytes);
        var token = savedPdf.Url.Split('/')[^1];
        Require(store.TryGet(token, "user-1") != null, "Owner must be able to retrieve generated download.");
        Require(store.TryGet(token, "other-user") == null, "Another user must never retrieve someone else's generated download.");

        // 7. Standalone AI Intelligence page: accessible to all internal roles, strictly blocked for Suppliers, plus Excel/PDF extraction
        foreach (var internalRole in AiIntelligenceService.AllowedWorkspaces)
        {
            Require(PageNavigation.Menus[internalRole].Any(m => m.Action == "AiIntelligence"),
                $"{internalRole} menu must include AI Intelligence.");
            var internalPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, internalRole)], "TestAuth"));
            Require(AiIntelligenceService.ResolveAuthorizedWorkspace(internalPrincipal, internalRole) == internalRole,
                $"{internalRole} must be authorized on AI Intelligence.");
        }
        Require(!PageNavigation.Menus[RoleNames.Supplier].Any(m => m.Action == "AiIntelligence"),
            "Supplier menu must NEVER include AI Intelligence.");
        var supplierPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, RoleNames.Supplier)], "TestAuth"));
        Require(AiIntelligenceService.ResolveAuthorizedWorkspace(supplierPrincipal, null) == null &&
                AiIntelligenceService.ResolveAuthorizedWorkspace(supplierPrincipal, RoleNames.Supplier) == null,
            "Supplier principal must be blocked from AI Intelligence.");

        using (var xlsxStream = new MemoryStream(xlsxBytes))
        {
            var (xlText, xlHeaders, xlRows, _) = AiIntelligenceService.ExtractExcelContent(xlsxStream, "Tender-191-Summary.xlsx");
            Require(xlText.Contains("Al-Shifa Pharma", StringComparison.Ordinal) && xlHeaders.Length > 0 && xlRows.Length > 0,
                "Excel upload extractor failed to parse workbook sheets and rows.");
        }
        using (var pdfStream = new MemoryStream(pdfBytes))
        {
            var (_, _, _, pdfMetrics) = AiIntelligenceService.ExtractPdfContent(pdfStream, "Tender-191-Summary.pdf");
            Require(pdfMetrics.Length >= 2 && pdfMetrics[0].Value == "1",
                "PDF upload extractor failed to inspect generated PDF document.");
        }

        // 8. Top Committee Reject vs Ask-for-Edit cycle (Top Committee -> Manager -> Officer -> Manager -> Top Committee)
        var editVote = new CommitteeVoteInput { TenderId = 1,
            ExpectedNominatedBidId = 10,
            Decision = "RequestEdit",
            Notes = "Need cold-chain validation or alternate offer.",
            Confirmed = true };
        Validator.ValidateObject(editVote, new ValidationContext(editVote), validateAllProperties: true);
        var blankEditVote = new CommitteeVoteInput { TenderId = 1, ExpectedNominatedBidId = 10, Decision = "RequestEdit", Notes = "  ", Confirmed = true };
        Require(!Validator.TryValidateObject(blankEditVote, new ValidationContext(blankEditVote), [], validateAllProperties: true),
            "RequestEdit must require non-blank notes.");

        var sampleReports = new[]
        {
            new EvaluationReport { EvaluationReportId = 1,
                Status = "OfficerSubmitted",
                Tender = new Tender { TenderId = 1, Status = "AwaitingManagerReview", TopEditState = null } },
            new EvaluationReport { EvaluationReportId = 2,
                Status = "ManagerApproved",
                Tender = new Tender { TenderId = 2, Status = "AwaitingManagerReview", TopEditState = "WithManager" } },
            new EvaluationReport { EvaluationReportId = 3,
                Status = "OfficerSubmitted",
                Tender = new Tender { TenderId = 3, Status = "AwaitingManagerReview", TopEditState = "OfficerAnswered" } }
        }.AsQueryable();
        Require(sampleReports.InManagerReview().Select(r => r.EvaluationReportId).SequenceEqual([1]),
            "InManagerReview must exclude active TopEditState tenders so they route to the dedicated CEO & Chiefs edit-request workflow.");

        foreach (var (ctrl, methodName) in new (Type, string)[]
        {
            (typeof(SourcingManagerController), "AnswerTopEditRequest"),
            (typeof(SourcingManagerController), "PushTopEditToOfficer"),
            (typeof(SourcingManagerController), "ForwardRejection"),
            (typeof(SourcingOfficerController), "RespondToTopEdit")
        })
        {
            var m = ctrl.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            var hasCsrf = m?.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null ||
                          ctrl.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>() != null;
            Require(m != null && m.GetCustomAttribute<HttpPostAttribute>() != null && hasCsrf,
                $"{ctrl.Name}.{methodName} must exist with [HttpPost] and CSRF protection.");
        }

        // 8b. End-to-end TopCommitteeVotingService execution: 2 approvals -> RequestEdit -> Manager answers -> 4 fresh approvals -> Awarded
        var editStore = new TopEditCycleMemoryStore();
        var votingSvc = new TopCommitteeVotingService(editStore);
        static ClaimsPrincipal TopActor(int pos) => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, $"top-{pos}"),
            new Claim(ClaimTypes.Role, RoleNames.TopCommittee)
        ], "TopEditTest"));

        await votingSvc.SubmitAsync(TopActor(1), new CommitteeVoteInput { TenderId = 1, ExpectedNominatedBidId = 10, Decision = "Approved", Confirmed = true });
        await votingSvc.SubmitAsync(TopActor(2), new CommitteeVoteInput { TenderId = 1, ExpectedNominatedBidId = 10, Decision = "Approved", Confirmed = true });
        Require(editStore.Tender.TenderApprovals.Count(a => a.Stage == "Top") == 2, "Expected 2 approvals before RequestEdit.");

        var reqEditRes = await votingSvc.SubmitAsync(TopActor(3), new CommitteeVoteInput
        {
            TenderId = 1,
            ExpectedNominatedBidId = 10,
            Decision = "RequestEdit",
            Notes = "Clarify cold-chain validation or switch to second offer.",
            Confirmed = true
        });
        Require(reqEditRes.Decision == "RequestEdit" && !reqEditRes.IsAwarded &&
                editStore.Tender.Status == "AwaitingManagerReview" &&
                editStore.Tender.TopEditState == "WithManager" &&
                editStore.Tender.TopEditRequestedByUserId == "top-3" &&
                !editStore.Tender.TenderApprovals.Any(a => a.Stage == "Top"),
            "RequestEdit must return tender to AwaitingManagerReview (TopEditState=WithManager) and clear prior round Top approvals.");

        // Simulate Manager answering (or Officer switching offer -> Manager answering) and returning to Top Committee
        editStore.Nomination.BidId = 11;
        editStore.Tender.EvaluationReport!.RecommendedBidId = 11;
        editStore.Tender.TopEditState = null;
        editStore.Tender.Status = "AwaitingCommitteeDecision";

        for (var pos = 1; pos <= 4; pos++)
        {
            var r = await votingSvc.SubmitAsync(TopActor(pos), new CommitteeVoteInput { TenderId = 1,
                ExpectedNominatedBidId = 11,
                Decision = "Approved",
                Confirmed = true });
            if (pos == 4)
                Require(r.IsAwarded && editStore.Tender.Status == "Awarded" && editStore.Tender.AwardedBidId == 11,
                    "After edit cycle returns to Top Committee, 4 fresh approvals must award the (updated) nominated offer.");
        }

        Console.WriteLine("PASS: vLLM /v1/chat/completions, thinking=false, Bearer API key auth, Groq 120B fallback and cooldown handling, AI Intelligence hub (Supplier-excluded + Excel/PDF upload/download), downloadable PDF/Excel exports, Top Committee complete-reject vs ask-for-edit cycle, role-scoped actions, page transfer (navigate_to_page), and Agentic AiChatClient.");
    }

    private static async Task CheckElevenLabsSpeechAsync()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Speech:ElevenLabs:ApiKey"] = "speech-fixture",
            ["Speech:ElevenLabs:Model"] = "eleven_v4_turbo",
            ["Speech:ElevenLabs:VoiceId"] = "default-fixture",
            ["Speech:ElevenLabs:ArabicVoiceId"] = "arabic-fixture"
        }).Build();
        var reply = string.Concat(Enumerable.Repeat("مرحباً بك في ترياق. ", 15));
        using var http = new HttpClient(new ElevenLabsSpeechFixture(reply));
        var speech = new AiChatSpeechClient(http, config);
        var clip = await speech.SpeakAsync(reply, CancellationToken.None);
        Require(speech.ClipLimit == AiChatSpeechClient.InputLimit &&
                clip is { ContentType: "audio/mpeg" }, "ElevenLabs should return one MP3 for the full reply.");
    }

    private sealed class ElevenLabsSpeechFixture(string reply) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Require(request.RequestUri?.Host == "api.elevenlabs.io" &&
                    request.RequestUri.AbsolutePath.EndsWith("/arabic-fixture", StringComparison.Ordinal) &&
                    request.Headers.GetValues("xi-api-key").Single() == "speech-fixture" &&
                    body.RootElement.GetProperty("model_id").GetString() == "eleven_v4_turbo" &&
                    body.RootElement.GetProperty("text").GetString() == reply, "Unexpected speech request.");
            var audio = new ByteArrayContent([1, 2, 3]);
            audio.Headers.ContentType = new("audio/mpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = audio };
        }
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
        public int RequestsWithFullHistory;
        private int _callCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _callCount++;
            SawBearerAuth = request.Headers.Authorization?.Scheme == "Bearer" &&
                            request.Headers.Authorization?.Parameter == "test_bearer_api_token_123";

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var rfType = body.RootElement.GetProperty("response_format").GetProperty("type").GetString();
            var messages = body.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() == AiChatClient.HistoryMessageLimit + 2 &&
                messages[1].GetProperty("content").GetString() == "Prior question 1" &&
                messages[11].GetProperty("content").GetString() == "Which page has the reports?" &&
                messages[12].GetProperty("role").GetString() == "assistant" &&
                messages[12].GetProperty("content").GetString() == new string('x', 800) + " Open Combined Reports.")
                RequestsWithFullHistory++;

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

    private sealed class GroqRateLimitMockHandler(bool recover = false, bool outsideScope = false) : HttpMessageHandler
    {
        public bool SawQwenEffortNone;
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var model = body.RootElement.GetProperty("model").GetString() ?? "";
            var effort = body.RootElement.TryGetProperty("reasoning_effort", out var e) ? e.GetString() : null;

            Require(model == (Calls == 0 ? "qwen/qwen3.8-27b" : "openai/gpt-oss-120b"),
                "Unexpected model or fallback order.");
            Calls++;
            if (Calls == 1)
                SawQwenEffortNone = effort == "none";
            else if (recover)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        choices = new[] { new
                        {
                            finish_reason = "stop",
                            message = new { content = outsideScope
                                ? """{"mode":"out_of_scope","reply":"","toolCalls":[]}"""
                                : """{"mode":"reply","reply":"Queue is clear.","toolCalls":[]}""" }
                        } }
                    })
                };
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return response;
        }
    }

    private sealed class GroqFormatMockHandler : HttpMessageHandler
    {
        public bool Saw400JsonObjectFallback;
        public bool SawNoResponseFormatRetry;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var model = body.RootElement.GetProperty("model").GetString() ?? "";
            var hasResponseFormat = body.RootElement.TryGetProperty("response_format", out _);

            Require(model == "qwen/qwen3.8-27b", "Unexpected response-format retry model.");
            if (hasResponseFormat)
            {
                Saw400JsonObjectFallback = true;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"json_validate_failed\"}}", Encoding.UTF8, "application/json")
                };
            }

            if (!hasResponseFormat)
            {
                SawNoResponseFormatRetry = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        choices = new[]
                        {
                            new
                            {
                                finish_reason = "stop",
                                message = new { content = "{\"mode\":\"reply\",\"reply\":\"Recovered via fallback.\",\"toolCalls\":[]}" }
                            }
                        }
                    }), Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException($"Unexpected request on model {model} (hasResponseFormat={hasResponseFormat}).");
        }
    }

    private sealed class TopEditCycleMemoryStore : ITopCommitteeVotingStore
    {
        public Tender Tender { get; } = new()
        {
            TenderId = 1,
            Title = "Oncology Cold-Chain Biologics",
            Description = "Edit cycle test",
            Status = "AwaitingCommitteeDecision",
            EvaluationReport = new EvaluationReport { EvaluationReportId = 1,
                TenderId = 1,
                Status = "ManagerApproved",
                RecommendedBidId = 10,
                ManagerSummary = "Initial referral" }
        };
        public Bid Nomination { get; } = new() { BidId = 10,
            TenderId = 1,
            Status = "Compliant",
            ComplianceStatus = "Compliant",
            SubmittedAt = DateTime.UtcNow,
            Currency = "JOD" };
        public List<CommitteeMember> Members { get; } = Enumerable.Range(1, 4)
            .Select(i => new CommitteeMember(i, $"top-{i}", $"Chief {i}", i, true, true))
            .ToList();
        public List<CommitteeManager> Managers { get; } = [new("mgr-1", "Sourcing Manager", true, true, DateTime.UtcNow)];

        public TopEditCycleMemoryStore()
        {
            var inv = new Invitation { SupplierProfile = new SupplierProfile { CompanyName = "Al-Shifa Pharma" } };
            inv.Bids.Add(Nomination);
            Tender.Invitations.Add(inv);
            Tender.TenderApprovals.Add(new TenderApproval { TenderId = 1, Stage = "Manager", Decision = "Approved", UserId = "mgr-1" });
        }

        public Task<T> ExecuteAsync<T>(string memberId, int tenderId, Func<ICommitteeVotingSession, CancellationToken, Task<T>> action, CancellationToken ct)
        {
            var access = new CommitteeClarificationContext(Tender, true, [RoleNames.TopCommittee], Members, Managers);
            return action(new Session(this, access), ct);
        }

        private sealed class Session(TopEditCycleMemoryStore store, CommitteeClarificationContext access) : ICommitteeVotingSession
        {
            public CommitteeVotingContext Context => new(access, store.Nomination);
            public Task AssertNotPausedAsync(CancellationToken ct) => Task.CompletedTask;
            public Task<bool> IsDatabaseReadyAsync(CancellationToken ct) => Task.FromResult(true);
            public void Add(TenderApproval vote) => store.Tender.TenderApprovals.Add(vote);
            public void ClearTopVotes()
            {
                foreach (var v in store.Tender.TenderApprovals.Where(a => a.Stage == "Top" && a.Decision != "Rejected")
                    .ToList())
                    store.Tender.TenderApprovals.Remove(v);
            }
            public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        }
    }
}
