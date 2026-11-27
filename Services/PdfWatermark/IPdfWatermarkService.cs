using System.Security.Claims;

namespace SourcingTenderManagement.Services.PdfWatermark;

// Contract for adding a watermark (who downloaded it and when) to PDF files.
public interface IPdfWatermarkService
{
    Task<byte[]> ApplyAsync(Stream original, ClaimsPrincipal user, DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default);
}
