using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SourcingTenderManagement.Services.Chat;

// Speech for Safi: turns the user's voice into text and Safi's reply into audio.
public sealed record AiChatSpeechClip(byte[] Bytes, string ContentType, string Provider);

public sealed class AiChatSpeechClient(HttpClient http, IConfiguration configuration)
{
    private const int GroqInputLimit = 200;
    private const int LocalInputLimit = 96;
    public const int InputLimit = 4500;
    private string? ElevenLabsKey => configuration["Speech:ElevenLabs:ApiKey"] ?? configuration["ELEVENLABS_API_KEY"];
    private bool LocalEnabled => configuration.GetValue<bool>("Speech:Local:Enabled");
    public int ClipLimit => !string.IsNullOrWhiteSpace(ElevenLabsKey) ? InputLimit
        : LocalEnabled ? LocalInputLimit : GroqInputLimit;

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken ct)
    {
        var (endpoint, _, key) = VllmSettings.ResolveEndpoint(configuration);
        if (!string.Equals(endpoint.Host, "api.groq.com", StringComparison.OrdinalIgnoreCase) || key is null)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StreamContent(audio), "file", fileName);
        multipart.Add(new StringContent("whisper-large-v3-turbo"), "model");
        multipart.Add(new StringContent("json"), "response_format");
        multipart.Add(new StringContent("0"), "temperature");
        request.Content = multipart; // Omitting language lets Whisper detect it from this turn.
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return result.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()?.Trim()
            : null;
    }

    public async Task<AiChatSpeechClip?> SpeakAsync(string text, CancellationToken ct)
    {
        var arabic = text.Any(c => c is >= '\u0621' and <= '\u064A');
        if (ElevenLabsKey?.Trim() is { Length: > 0 } elevenLabsKey)
            return await SpeakWithElevenLabsAsync(text, arabic, elevenLabsKey, ct);
        if (text.Length > GroqInputLimit)
            return null;

        if (LocalEnabled)
        {
            if (text.Length > LocalInputLimit)
                return null;
            // The loopback worker reads a bounded body using Content-Length.
            using var body = new StringContent(
                JsonSerializer.Serialize(new { text, language = arabic ? "ar" : "en" }),
                Encoding.UTF8, "application/json");
            using var local = await http.PostAsync("http://127.0.0.1:8174/speech", body, ct);
            return await ReadWavAsync(local, "Chatterbox", ct);
        }

        var (endpoint, _, key) = VllmSettings.ResolveEndpoint(configuration);
        if (!string.Equals(endpoint.Host, "api.groq.com", StringComparison.OrdinalIgnoreCase) || key is null)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/speech")
        {
            Content = JsonContent.Create(new
            {
                model = arabic ? "canopylabs/orpheus-arabic-saudi" : "canopylabs/orpheus-v1-english",
                voice = arabic ? "abdullah" : "daniel",
                input = text,
                response_format = "wav"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request, ct);
        return await ReadWavAsync(response, "Orpheus", ct);
    }

    private static async Task<AiChatSpeechClip?> ReadWavAsync(
        HttpResponseMessage response, string provider, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length >= 44 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8) ? new(bytes, "audio/wav", provider) : null;
    }

    private async Task<AiChatSpeechClip?> SpeakWithElevenLabsAsync(
        string text, bool arabic, string apiKey, CancellationToken ct)
    {
        var voiceId = (arabic ? configuration["Speech:ElevenLabs:ArabicVoiceId"] : null)
            ?? configuration["Speech:ElevenLabs:VoiceId"];
        if (string.IsNullOrWhiteSpace(voiceId))
            return null;

        var encodedVoiceId = Uri.EscapeDataString(voiceId);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.elevenlabs.io/v1/text-to-speech/{encodedVoiceId}?output_format=mp3_44100_128")
        {
            Content = JsonContent.Create(new
            {
                text,
                model_id = configuration["Speech:ElevenLabs:Model"]
            })
        };
        request.Headers.Add("xi-api-key", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode ||
            !string.Equals(response.Content.Headers.ContentType?.MediaType, "audio/mpeg", StringComparison.OrdinalIgnoreCase))
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length > 0 ? new(bytes, "audio/mpeg", "ElevenLabs") : null;
    }
}
