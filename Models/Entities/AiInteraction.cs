namespace SourcingTenderManagement.Models.Entities;

/// <summary>
/// One call to the AI model, recorded by <see cref="Services.Ai.AiGateway"/>.
/// The full prompt is not stored: only its size and SHA-256 hash, so confidential
/// record data is not copied into the log. The answer is kept in truncated form.
/// </summary>
public sealed class AiInteraction
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string LimitReached = "LimitReached";

    public long AiInteractionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Null for anonymous visitors (public Safi) and background work.</summary>
    public string? UserId { get; set; }

    /// <summary>The workspace the call was made from, such as SourcingOfficer, Public or System.</summary>
    public string Workspace { get; set; } = "";

    /// <summary>Stable feature key, such as safi.decision or reports.final.</summary>
    public string Feature { get; set; } = "";
    public string SchemaName { get; set; } = "";

    /// <summary>First 12 hex characters of the system prompt's SHA-256; changes whenever the prompt text changes.</summary>
    public string PromptVersion { get; set; } = "";
    public string? Model { get; set; }

    public string Outcome { get; set; } = Succeeded;
    public string? ErrorType { get; set; }

    /// <summary>Whether the answer matched the requested JSON schema. Null when no answer was received.</summary>
    public bool? SchemaConforms { get; set; }
    public string? SchemaIssues { get; set; }

    public int DurationMs { get; set; }
    public int InputChars { get; set; }
    public string InputSha256 { get; set; } = "";
    public int? OutputChars { get; set; }
    public string? OutputPreview { get; set; }

    /// <summary>Interactive calls (Safi, AI Intelligence) count toward the daily per-user limit.</summary>
    public bool IsInteractive { get; set; }

    /// <summary>Optional link to the record the call was about, such as Tender 12.</summary>
    public string? RecordType { get; set; }
    public int? RecordId { get; set; }

    /// <summary>Reserved for later: Used, Edited or Ignored, recorded from the user's response to the answer.</summary>
    public string? UserFeedback { get; set; }
}
