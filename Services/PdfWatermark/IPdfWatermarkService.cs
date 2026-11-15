using System.Security.Claims;

namespace SourcingTenderManagement.Services.PdfWatermark;

public interface IPdfWatermarkService
{
    Task<byte[]> ApplyAsync(Stream original, ClaimsPrincipal user, DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default);
}
