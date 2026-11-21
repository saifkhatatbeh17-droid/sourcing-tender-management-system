using System.Collections.Concurrent;

namespace SourcingTenderManagement.Services.Chat;

public sealed record AiChatDownloadLink(
    string Url,
    string FileName,
    string Label,
    string Format);

public sealed record AiChatStoredFile(
    string Token,
    string UserId,
    string Workspace,
    string FileName,
    string ContentType,
    byte[] Bytes,
    DateTime CreatedUtc,
    bool OneTime = false);

public sealed class AiChatDownloadStore
{
    private const int MaxEntries = 120;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(60);

    // Sensitive files (such as new-account passwords) can be fetched once and expire sooner.
    private static readonly TimeSpan OneTimeRetention = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, AiChatStoredFile> _files = new(StringComparer.Ordinal);

    public AiChatDownloadLink Save(
        string userId,
        string workspace,
        string fileName,
        string contentType,
        string label,
        string format,
        byte[] bytes,
        bool oneTime = false)
    {
        PruneExpired();

        var token = Guid.NewGuid().ToString("N");
        var entry = new AiChatStoredFile(
            token,
            userId,
            workspace,
            fileName,
            contentType,
            bytes,
            DateTime.UtcNow,
            oneTime);

        _files[token] = entry;

        if (_files.Count > MaxEntries)
        {
            foreach (var oldest in _files.Values.OrderBy(f => f.CreatedUtc).Take(_files.Count - MaxEntries))
            {
                _files.TryRemove(oldest.Token, out _);
            }
        }

        return new AiChatDownloadLink(
            $"/AiChat/Download/{token}",
            fileName,
            label,
            format);
    }

    public AiChatStoredFile? TryGet(string token, string userId)
    {
        PruneExpired();
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!_files.TryGetValue(token, out var entry)) return null;
        if (!string.Equals(entry.UserId, userId, StringComparison.Ordinal)) return null;
        if (DateTime.UtcNow - entry.CreatedUtc > (entry.OneTime ? OneTimeRetention : Retention))
        {
            _files.TryRemove(token, out _);
            return null;
        }
        // A one-time file is removed as it is handed out, so a second request finds nothing.
        if (entry.OneTime && !_files.TryRemove(token, out _)) return null;
        return entry;
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _files)
        {
            if (now - pair.Value.CreatedUtc > (pair.Value.OneTime ? OneTimeRetention : Retention))
                _files.TryRemove(pair.Key, out _);
        }
    }
}
