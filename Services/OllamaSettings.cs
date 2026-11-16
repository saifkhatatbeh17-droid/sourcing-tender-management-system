namespace SourcingTenderManagement.Services;

internal static class OllamaSettings
{
    public static string Model(IConfiguration configuration) => configuration["Ollama:Model"] ?? "qwen2.5:7b";

    public static Uri GenerateEndpoint(IConfiguration configuration)
    {
        var url = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http")
            throw new InvalidOperationException("Ollama must use a local HTTP address.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/api/generate");
    }
}
