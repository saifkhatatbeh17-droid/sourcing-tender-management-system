using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SourcingTenderManagement.Services;

internal static partial class VllmSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Model(IConfiguration configuration) =>
        FirstNonEmpty(
            configuration["Vllm:Model"],
            Environment.GetEnvironmentVariable("VLLM_MODEL"),
            "Qwen/Qwen3-8B-AWQ")!;

    public static string? ApiKey(IConfiguration configuration) =>
        FirstNonEmpty(
            configuration["Vllm:ApiKey"],
            Environment.GetEnvironmentVariable("VLLM_API_KEY"));

    public static bool EnableThinking(IConfiguration configuration) =>
        bool.TryParse(configuration["Vllm:EnableThinking"], out var enabled) && enabled;

    public static (Uri Endpoint, bool IsLoopback, string? ApiKey) ResolveEndpoint(IConfiguration configuration)
    {
        var rawUrl = FirstNonEmpty(
            configuration["Vllm:BaseUrl"],
            Environment.GetEnvironmentVariable("VLLM_BASE_URL"),
            "http://localhost:8000/v1")!;

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("Vllm:BaseUrl must be a valid absolute URL.");

        if (IsBlockedHost(baseUri))
            throw new InvalidOperationException("The configured Vllm:BaseUrl host is not permitted.");

        var apiKey = ApiKey(configuration);
        if (IsPlaceholderKey(apiKey))
            apiKey = null;

        if (baseUri.IsLoopback)
        {
            if (baseUri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Local vLLM endpoints must use HTTP or HTTPS.");
        }
        else
        {
            if (baseUri.Scheme != "https")
                throw new InvalidOperationException("Remote AI endpoints must use HTTPS.");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Configure Vllm:ApiKey in appsettings.Local.json (or VLLM_API_KEY) to use a remote AI endpoint.");
        }

        var normalized = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var fullUrl = normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
                ? normalized + "/chat/completions"
                : normalized + "/v1/chat/completions";

        return (new Uri(fullUrl), baseUri.IsLoopback, apiKey);
    }

    public static async Task<string> CompleteJsonAsync(
        HttpClient http,
        IConfiguration configuration,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        object schema,
        int maxTokens,
        CancellationToken ct,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null)
    {
        var (endpoint, isLoopback, apiKey) = ResolveEndpoint(configuration);
        var model = Model(configuration);
        var enableThinking = EnableThinking(configuration);
        var schemaInstruction = "\n\nRespond ONLY with a valid JSON object conforming strictly to this JSON Schema:\n" +
                                JsonSerializer.Serialize(schema, Json);

        // Groq's qwen3 constrained json_schema FSM can prematurely close long string fields;
        // using json_object mode with the schema in the system prompt yields complete, fast JSON in one call.
        var preferDirectJsonObject = !isLoopback && endpoint.Host.Contains("groq.com", StringComparison.OrdinalIgnoreCase);

        using var primaryResponse = await SendWithRateLimitRetryAsync(
            http,
            () => BuildRequest(
                endpoint,
                apiKey,
                model,
                preferDirectJsonObject ? systemPrompt + schemaInstruction : systemPrompt,
                userPrompt,
                schemaName,
                schema,
                maxTokens,
                enableThinking,
                includeVllmExtensions: isLoopback || IsLikelyVllmOrQwenHost(endpoint),
                useJsonObjectFallback: preferDirectJsonObject,
                conversationHistory: conversationHistory),
            ct);

        // If another cloud API-key provider rejects vLLM's chat_template_kwargs or strict json_schema with HTTP 400,
        // automatically fall back to pure OpenAI json_object mode with the schema embedded in the system message.
        if (primaryResponse.StatusCode == HttpStatusCode.BadRequest && !isLoopback && !preferDirectJsonObject)
        {
            using var fallbackResponse = await SendWithRateLimitRetryAsync(
                http,
                () => BuildRequest(
                    endpoint,
                    apiKey,
                    model,
                    systemPrompt + schemaInstruction,
                    userPrompt,
                    schemaName,
                    schema,
                    maxTokens,
                    enableThinking,
                    includeVllmExtensions: false,
                    useJsonObjectFallback: true,
                    conversationHistory: conversationHistory),
                ct);
            fallbackResponse.EnsureSuccessStatusCode();
            return ParseCompletionContent(await fallbackResponse.Content.ReadAsStringAsync(ct));
        }

        primaryResponse.EnsureSuccessStatusCode();
        return ParseCompletionContent(await primaryResponse.Content.ReadAsStringAsync(ct));
    }

    private static async Task<HttpResponseMessage> SendWithRateLimitRetryAsync(
        HttpClient http,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = requestFactory();
            var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 2)
                return resp;

            var delaySeconds = resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? 2.5;
            delaySeconds = Math.Clamp(delaySeconds, 1.5, 6.0);
            resp.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
        }
    }

    private static HttpRequestMessage BuildRequest(
        Uri endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        object schema,
        int maxTokens,
        bool enableThinking,
        bool includeVllmExtensions,
        bool useJsonObjectFallback,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        if (conversationHistory != null)
        {
            foreach (var turn in conversationHistory)
            {
                if (turn.Role is ("user" or "assistant") && !string.IsNullOrWhiteSpace(turn.Content))
                    messages.Add(new { role = turn.Role, content = turn.Content });
            }
        }
        messages.Add(new { role = "user", content = userPrompt });

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = false,
            ["temperature"] = 0,
            ["max_tokens"] = maxTokens,
            ["messages"] = messages,
            ["response_format"] = useJsonObjectFallback
                ? new { type = "json_object" }
                : new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = schemaName,
                        strict = true,
                        schema
                    }
                }
        };

        if (!enableThinking)
        {
            if (includeVllmExtensions)
            {
                // Native vLLM / Qwen3 flag to bypass chain-of-thought thinking tokens for instant JSON output.
                payload["chat_template_kwargs"] = new { enable_thinking = false };
            }
            else if (SupportsReasoningEffortNone(endpoint, model))
            {
                // Standard parameter used by Groq (qwen3) and Gemini OpenAI-compatible endpoints to turn off thinking.
                payload["reasoning_effort"] = "none";
            }
        }

        request.Content = JsonContent.Create(payload, options: Json);
        return request;
    }

    private static string ParseCompletionContent(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("The AI service returned no completion choices.");

        var first = choices[0];
        if (first.TryGetProperty("finish_reason", out var finishReason))
        {
            var reason = finishReason.GetString();
            if (!string.IsNullOrEmpty(reason) && reason is not ("stop" or "end_turn"))
                throw new InvalidOperationException("The AI response was incomplete. Try again.");
        }

        if (!first.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentElement))
            throw new InvalidOperationException("The AI response contained no message content.");

        var text = contentElement.GetString() ?? "";
        return CleanJsonContent(text);
    }

    private static string CleanJsonContent(string text)
    {
        var cleaned = ThinkBlockRegex().Replace(text, "").Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline > 0)
                cleaned = cleaned[(firstNewline + 1)..];
            if (cleaned.EndsWith("```", StringComparison.Ordinal))
                cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
        }
        return cleaned;
    }

    private static bool IsLikelyVllmOrQwenHost(Uri endpoint) =>
        endpoint.Host.Contains("together", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Host.Contains("deepinfra", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Host.Contains("fireworks", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsReasoningEffortNone(Uri endpoint, string model) =>
        (endpoint.Host.Contains("groq.com", StringComparison.OrdinalIgnoreCase) &&
         model.Contains("qwen3", StringComparison.OrdinalIgnoreCase)) ||
        endpoint.Host.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholderKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ||
        key.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedHost(Uri uri)
    {
        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            // Block cloud metadata / link-local 169.254.0.0/16
            if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254)
                return true;
        }
        return false;
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();
}
