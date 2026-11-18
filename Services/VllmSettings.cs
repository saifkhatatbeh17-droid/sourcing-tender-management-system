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

    private static readonly string[] DefaultGroqFallbackModels =
    [
        "openai/gpt-oss-120b",
        "openai/gpt-oss-20b"
    ];

    public static IReadOnlyList<string> CandidateModels(IConfiguration configuration, Uri endpoint)
    {
        var primary = Model(configuration);
        var candidates = new List<string> { primary };

        var configuredFallbacks = FirstNonEmpty(
            configuration["Vllm:FallbackModels"],
            Environment.GetEnvironmentVariable("VLLM_FALLBACK_MODELS"));

        if (!string.IsNullOrWhiteSpace(configuredFallbacks))
        {
            foreach (var item in configuredFallbacks.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!candidates.Contains(item, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(item);
            }
        }
        else if (endpoint.Host.Contains("groq.com", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in DefaultGroqFallbackModels)
            {
                if (!candidates.Contains(item, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(item);
            }
        }

        return candidates;
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
        var models = CandidateModels(configuration, endpoint);
        var enableThinking = EnableThinking(configuration);
        var schemaInstruction = "\n\nRespond ONLY with a valid JSON object conforming strictly to this JSON Schema:\n" +
                                JsonSerializer.Serialize(schema, Json);

        // Groq's qwen3 constrained json_schema FSM can prematurely close long string fields;
        // using json_object mode with the schema in the system prompt yields complete, fast JSON in one call.
        var preferDirectJsonObject = !isLoopback && endpoint.Host.Contains("groq.com", StringComparison.OrdinalIgnoreCase);

        const int maxRounds = 3;
        var skippedModels = new HashSet<int>();

        for (var round = 0; round < maxRounds; round++)
        {
            var shortestDelay = double.MaxValue;
            var sawRetryable = false;

            for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                if (skippedModels.Contains(modelIndex))
                    continue;

                var activeModel = models[modelIndex];
                var hasRemainingModels = Enumerable.Range(modelIndex + 1, models.Count - modelIndex - 1)
                    .Any(i => !skippedModels.Contains(i));

                var formatMode = preferDirectJsonObject ? ResponseFormatMode.JsonObject : ResponseFormatMode.JsonSchema;
                var includeVllm = isLoopback || IsLikelyVllmOrQwenHost(endpoint);

                var attemptResult = await SendWithFormatFallbacksAsync(
                    http,
                    endpoint,
                    apiKey,
                    activeModel,
                    systemPrompt,
                    schemaInstruction,
                    userPrompt,
                    schemaName,
                    schema,
                    maxTokens,
                    enableThinking,
                    includeVllm,
                    formatMode,
                    isLoopback,
                    round,
                    conversationHistory,
                    ct);

                using var lastResponse = attemptResult.LastResponse;

                if (attemptResult.Content != null)
                    return attemptResult.Content;

                if (attemptResult.IsRetryable)
                {
                    sawRetryable = true;
                    shortestDelay = Math.Min(shortestDelay, attemptResult.RetryDelaySeconds);
                    if (round == maxRounds - 1 && !hasRemainingModels)
                        lastResponse!.EnsureSuccessStatusCode();
                    continue;
                }

                if (attemptResult.IsModelSkippedStatus)
                {
                    skippedModels.Add(modelIndex);
                    if (!hasRemainingModels && !sawRetryable && skippedModels.Count == models.Count)
                        lastResponse!.EnsureSuccessStatusCode();
                    continue;
                }

                lastResponse!.EnsureSuccessStatusCode();
            }

            if (skippedModels.Count == models.Count || !sawRetryable)
                break;

            if (round < maxRounds - 1)
            {
                var waitSeconds = shortestDelay < double.MaxValue ? shortestDelay : 2.5 * (round + 1);
                waitSeconds = Math.Clamp(waitSeconds, 0.5, 22.0);
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
            }
        }

        throw new HttpRequestException("The AI endpoint remained rate-limited or unavailable after retrying.");
    }

    private enum ResponseFormatMode
    {
        JsonSchema,
        JsonObject,
        None
    }

    private sealed record AttemptOutcome(
        string? Content,
        HttpResponseMessage? LastResponse,
        bool IsRetryable,
        bool IsModelSkippedStatus,
        double RetryDelaySeconds);

    private static async Task<AttemptOutcome> SendWithFormatFallbacksAsync(
        HttpClient http,
        Uri endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string schemaInstruction,
        string userPrompt,
        string schemaName,
        object schema,
        int maxTokens,
        bool enableThinking,
        bool includeVllmExtensions,
        ResponseFormatMode initialMode,
        bool isLoopback,
        int round,
        IReadOnlyList<(string Role, string Content)>? conversationHistory,
        CancellationToken ct)
    {
        var steps = new List<(ResponseFormatMode Mode, bool IncludeVllm)>();
        steps.Add((initialMode, includeVllmExtensions));
        if (!isLoopback)
        {
            if (initialMode == ResponseFormatMode.JsonSchema)
                steps.Add((ResponseFormatMode.JsonObject, false));
            if (initialMode != ResponseFormatMode.None)
                steps.Add((ResponseFormatMode.None, false));
        }

        HttpResponseMessage? lastResponse = null;
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var (mode, useVllm) = steps[i];
                var effectiveSystem = mode == ResponseFormatMode.JsonSchema
                    ? systemPrompt
                    : systemPrompt + schemaInstruction;

                var response = await http.SendAsync(
                    BuildRequest(
                        endpoint,
                        apiKey,
                        model,
                        effectiveSystem,
                        userPrompt,
                        schemaName,
                        schema,
                        maxTokens,
                        enableThinking,
                        useVllm,
                        mode,
                        conversationHistory),
                    ct);

                lastResponse?.Dispose();
                lastResponse = response;

                if (IsRetryableOrRateLimited(response.StatusCode))
                {
                    var delay = ParseRateLimitDelaySeconds(response, round);
                    return new AttemptOutcome(null, lastResponse, true, false, delay);
                }

                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    return new AttemptOutcome(null, lastResponse, false, true, 0);
                }

                if (response.StatusCode == HttpStatusCode.BadRequest && i < steps.Count - 1)
                {
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.BadRequest && !isLoopback)
                {
                    return new AttemptOutcome(null, lastResponse, false, true, 0);
                }

                response.EnsureSuccessStatusCode();
                var content = ParseCompletionContent(await response.Content.ReadAsStringAsync(ct));
                lastResponse.Dispose();
                lastResponse = null;
                return new AttemptOutcome(content, null, false, false, 0);
            }

            return new AttemptOutcome(null, lastResponse, false, true, 0);
        }
        catch
        {
            lastResponse?.Dispose();
            throw;
        }
    }

    private static bool IsRetryableOrRateLimited(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ||
        (int)statusCode == 529;

    private static double ParseRateLimitDelaySeconds(HttpResponseMessage resp, int round)
    {
        if (resp.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 } delta)
            return delta.TotalSeconds + 0.25;

        if (resp.Headers.RetryAfter?.Date is { } date)
        {
            var diff = (date - DateTimeOffset.UtcNow).TotalSeconds;
            if (diff > 0) return diff + 0.25;
        }

        if (resp.Headers.TryGetValues("x-ratelimit-reset-tokens", out var values))
        {
            foreach (var raw in values)
            {
                if (TryParseDurationHeaderSeconds(raw, out var parsed) && parsed > 0)
                    return parsed + 0.25;
            }
        }

        return 2.0 * (round + 1);
    }

    private static bool TryParseDurationHeaderSeconds(string? raw, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var plain))
        {
            seconds = plain;
            return true;
        }

        var match = GoDurationRegex().Match(trimmed);
        if (!match.Success) return false;

        var total = 0.0;
        var matchedAny = false;
        if (match.Groups["m"].Success && double.TryParse(match.Groups["m"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mins))
        {
            total += mins * 60.0;
            matchedAny = true;
        }
        if (match.Groups["s"].Success && double.TryParse(match.Groups["s"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
        {
            total += secs;
            matchedAny = true;
        }
        if (match.Groups["ms"].Success && double.TryParse(match.Groups["ms"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms))
        {
            total += ms / 1000.0;
            matchedAny = true;
        }

        seconds = total;
        return matchedAny;
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
        ResponseFormatMode formatMode,
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
            ["messages"] = messages
        };

        if (formatMode == ResponseFormatMode.JsonObject)
        {
            payload["response_format"] = new { type = "json_object" };
        }
        else if (formatMode == ResponseFormatMode.JsonSchema)
        {
            payload["response_format"] = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = schemaName,
                    strict = true,
                    schema
                }
            };
        }

        if (!enableThinking)
        {
            if (includeVllmExtensions)
            {
                // Native vLLM / Qwen3 flag to bypass chain-of-thought thinking tokens for instant JSON output.
                payload["chat_template_kwargs"] = new { enable_thinking = false };
            }
            else if (ResolveReasoningEffort(endpoint, model) is { } effort)
            {
                payload["reasoning_effort"] = effort;
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
        if (!first.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentElement))
            throw new InvalidOperationException("The AI response contained no message content.");

        var cleaned = CleanJsonContent(contentElement.GetString() ?? "");

        if (first.TryGetProperty("finish_reason", out var finishReason))
        {
            var reason = finishReason.GetString();
            if (!string.IsNullOrEmpty(reason) && reason is not ("stop" or "end_turn"))
            {
                if (string.IsNullOrWhiteSpace(cleaned) || !IsCompleteJsonObject(cleaned))
                    throw new InvalidOperationException("The AI response was incomplete. Try again.");
            }
        }

        return cleaned;
    }

    private static bool IsCompleteJsonObject(string candidate)
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
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

        var firstBrace = cleaned.IndexOf('{');
        var lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace && (firstBrace > 0 || lastBrace < cleaned.Length - 1))
        {
            cleaned = cleaned[firstBrace..(lastBrace + 1)];
        }

        return cleaned;
    }

    private static bool IsLikelyVllmOrQwenHost(Uri endpoint) =>
        endpoint.Host.Contains("together", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Host.Contains("deepinfra", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Host.Contains("fireworks", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveReasoningEffort(Uri endpoint, string model)
    {
        if (endpoint.Host.Contains("groq.com", StringComparison.OrdinalIgnoreCase))
        {
            if (model.Contains("qwen3", StringComparison.OrdinalIgnoreCase))
                return "none";
            if (model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
                return "low";
        }

        if (endpoint.Host.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
            return "none";

        return null;
    }

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

    [GeneratedRegex(@"^(?:(?<m>\d+(?:\.\d+)?)m(?!s))?(?:(?<s>\d+(?:\.\d+)?)s)?(?:(?<ms>\d+(?:\.\d+)?)ms)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GoDurationRegex();
}
