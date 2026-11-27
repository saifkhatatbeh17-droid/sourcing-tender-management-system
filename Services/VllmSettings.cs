using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SourcingTenderManagement.Services;

// Low-level connection to the AI model: reads the model settings and sends one JSON request.
// Features should go through AiGateway (Services/Ai) instead of calling this directly.
internal static partial class VllmSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Model(IConfiguration configuration) =>
        FirstNonEmpty(configuration["Vllm:Model"], Environment.GetEnvironmentVariable("VLLM_MODEL"))
        ?? throw new InvalidOperationException("Configure Vllm:Model with an AI model ID.");

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
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        bool retryWithSchema = false) =>
        (await CompleteJsonWithModelAsync(http, configuration, systemPrompt, userPrompt, schemaName,
            schema, maxTokens, ct, conversationHistory, retryWithSchema)).Content;

    public static async Task<(string Content, string Model)> CompleteJsonWithModelAsync(
        HttpClient http,
        IConfiguration configuration,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        object schema,
        int maxTokens,
        CancellationToken ct,
        IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
        bool retryWithSchema = false,
        string? imageDataUrl = null)
    {
        var (endpoint, isLoopback, apiKey) = ResolveEndpoint(configuration);
        var enableThinking = EnableThinking(configuration);
        var schemaInstruction = "\n\nRespond ONLY with a valid JSON object conforming strictly to this JSON Schema:\n" +
                                JsonSerializer.Serialize(schema, Json);

        // Groq's qwen3 constrained json_schema FSM can prematurely close long string fields;
        // using json_object mode with the schema in the system prompt yields complete, fast JSON in one call.
        var isGroq = IsGroq(endpoint);

        const int maxRounds = 3;
        var formatMode = isGroq ? ResponseFormatMode.JsonObject : ResponseFormatMode.JsonSchema;
        var primaryModel = imageDataUrl is not null
            && configuration["Vllm:VisionModel"]?.Trim() is { Length: > 0 } vision
            ? vision : Model(configuration);
        var fallbackModel = imageDataUrl is null ? configuration["Vllm:FallbackModel"]?.Trim() : null;
        var activeModel = primaryModel;

        for (var round = 0; round < maxRounds; round++)
        {
            AttemptOutcome attempt;
            try
            {
                attempt = await SendWithFormatFallbacksAsync(
                    http, endpoint, apiKey, activeModel, systemPrompt, schemaInstruction,
                    userPrompt, schemaName, schema, maxTokens, enableThinking,
                    formatMode, isLoopback, retryWithSchema, round, conversationHistory, imageDataUrl, ct);
            }
            catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConnectionError &&
                                                   round < maxRounds - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (round + 1)), ct);
                continue;
            }

            if (attempt.Content != null)
                return (attempt.Content, activeModel);

            if (isGroq && activeModel == primaryModel && !string.IsNullOrEmpty(fallbackModel) &&
                !string.Equals(primaryModel, fallbackModel, StringComparison.OrdinalIgnoreCase) &&
                round < maxRounds - 1)
            {
                activeModel = fallbackModel;
                continue;
            }

            // A longer provider cooldown cannot fit in an interactive retry.
            if (round == maxRounds - 1 || attempt.RetryDelaySeconds > 22)
                throw new HttpRequestException("The AI endpoint is temporarily unavailable.", null, attempt.StatusCode);

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(attempt.RetryDelaySeconds, 0.5)), ct);
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
        HttpStatusCode? StatusCode,
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
        ResponseFormatMode initialMode,
        bool isLoopback,
        bool retryWithSchema,
        int round,
        IReadOnlyList<(string Role, string Content)>? conversationHistory,
        string? imageDataUrl,
        CancellationToken ct)
    {
        var steps = new List<ResponseFormatMode> { initialMode };
        if (!isLoopback)
        {
            if (initialMode == ResponseFormatMode.JsonSchema)
                steps.Add(ResponseFormatMode.JsonObject);
            if (initialMode == ResponseFormatMode.JsonObject && retryWithSchema)
                steps.Add(ResponseFormatMode.JsonSchema);
            steps.Add(ResponseFormatMode.None);
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var mode = steps[i];
            var effectiveSystem = mode == ResponseFormatMode.JsonSchema
                ? systemPrompt
                : systemPrompt + schemaInstruction;

            using var request = BuildRequest(
                endpoint, apiKey, model, effectiveSystem, userPrompt, schemaName, schema,
                maxTokens, enableThinking, isLoopback, mode, conversationHistory, imageDataUrl);
            using var response = await http.SendAsync(request, ct);

            if (IsRetryableOrRateLimited(response.StatusCode))
                return new AttemptOutcome(null, response.StatusCode, ParseRateLimitDelaySeconds(response, round));

            var canTryAnotherFormat = i < steps.Count - 1;
            if (response.StatusCode == HttpStatusCode.BadRequest && canTryAnotherFormat)
                continue;

            response.EnsureSuccessStatusCode();
            var content = ParseCompletionContent(await response.Content.ReadAsStringAsync(ct));
            if (IsCompleteJsonObject(content))
                return new AttemptOutcome(content, null, 0);
            if (!canTryAnotherFormat)
                throw new InvalidOperationException("The AI service did not return a complete JSON object.");
        }

        throw new InvalidOperationException("No usable AI response format was available.");
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
        if (match.Groups["m"].Success
            && double.TryParse(match.Groups["m"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mins))
        {
            total += mins * 60.0;
            matchedAny = true;
        }
        if (match.Groups["s"].Success
            && double.TryParse(match.Groups["s"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
        {
            total += secs;
            matchedAny = true;
        }
        if (match.Groups["ms"].Success
            && double.TryParse(match.Groups["ms"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms))
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
        IReadOnlyList<(string Role, string Content)>? conversationHistory,
        string? imageDataUrl)
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
        object userContent = imageDataUrl is null ? userPrompt : new object[]
        {
            new { type = "text", text = userPrompt },
            new { type = "image_url", image_url = new { url = imageDataUrl } }
        };
        messages.Add(new { role = "user", content = userContent });

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

        return CleanJsonContent(contentElement.GetString() ?? "");
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

        return cleaned;
    }

    private static bool IsGroq(Uri endpoint) =>
        string.Equals(endpoint.Host, "api.groq.com", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveReasoningEffort(Uri endpoint, string model)
    {
        if (IsGroq(endpoint))
        {
            if (model.Contains("qwen3", StringComparison.OrdinalIgnoreCase))
                return "none";
            if (model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
                return "low";
        }

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
