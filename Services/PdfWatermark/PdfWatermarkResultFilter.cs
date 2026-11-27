using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SourcingTenderManagement.Services.PdfWatermark;

// Runs after any action that returns a PDF and stamps the watermark on it automatically.
public sealed class PdfWatermarkResultFilter(IPdfWatermarkService watermark, TimeProvider clock,
    ILogger<PdfWatermarkResultFilter> logger) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is not FileResult file || context.HttpContext.User.Identity?.IsAuthenticated != true ||
            context.HttpContext.Response.StatusCode is < 200 or >= 300 || !IsPdf(file))
        {
            await next();
            return;
        }

        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.Remove("ETag");
        response.Headers.Remove("Last-Modified");
        try
        {
            await using var original = file switch
            {
                FileContentResult content => new MemoryStream(content.FileContents, writable: false),
                FileStreamResult stream => stream.FileStream,
                PhysicalFileResult physical => new FileStream(physical.FileName, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan),
                _ => throw new InvalidDataException("Unsupported PDF response source.")
            };
            var bytes = await watermark.ApplyAsync(original, context.HttpContext.User, clock.GetUtcNow(),
                context.HttpContext.RequestAborted);
            // Original byte ranges/validators cannot describe these personalized bytes. Keep the
            // existing inline/attachment filename behavior, but execute a complete uncached response.
            context.Result = new FileContentResult(bytes, "application/pdf") { FileDownloadName = file.FileDownloadName };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            // Do not log usernames, document paths, content, passwords or parser exception messages.
            logger.LogWarning("PDF watermark processing failed ({FailureType}).", error.GetType().Name);
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "This PDF cannot be safely watermarked.",
                Detail = "The document may be unsupported, protected, damaged, or above processing limits. The original file has not been changed."
            }) { StatusCode = StatusCodes.Status422UnprocessableEntity };
        }
        await next();
    }

    private static bool IsPdf(FileResult file) =>
        string.Equals(file.ContentType.Split(';')[0].Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(file.FileDownloadName), ".pdf", StringComparison.OrdinalIgnoreCase) ||
        file is PhysicalFileResult physical
            && string.Equals(Path.GetExtension(physical.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);
}
