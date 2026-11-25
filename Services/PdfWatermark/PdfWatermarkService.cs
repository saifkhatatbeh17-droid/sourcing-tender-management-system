using System.Security.Claims;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SourcingTenderManagement.Services.PdfWatermark;

public interface IPdfWatermarkService
{
    Task<byte[]> ApplyAsync(Stream original, ClaimsPrincipal user, DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default);
}

public sealed class PdfWatermarkService : IPdfWatermarkService
{
    public const int MaxInputBytes = 20 * 1024 * 1024;
    public const int MaxPages = 500;
    // Bound concurrent parser work; PDF parsing itself is synchronous and is not a sandbox.
    private static readonly SemaphoreSlim Slots = new(2);

    public async Task<byte[]> ApplyAsync(Stream original, ClaimsPrincipal user, DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default)
    {
        var account = AccountLabel(user);
        if (original.CanSeek && original.Length - original.Position > MaxInputBytes)
            throw new InvalidDataException("PDF exceeds the watermark size limit.");
        await Slots.WaitAsync(cancellationToken);
        try
        {
            using var input = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await original.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (input.Length + read > MaxInputBytes)
                    throw new InvalidDataException("PDF exceeds the watermark size limit.");
                await input.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            input.Position = 0;
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            if (document.SecuritySettings.IsEncrypted || document.PageCount is < 1 or > MaxPages ||
                document.Internals.GetAllObjects().OfType<PdfDictionary>().Any(d =>
                    d.Elements.GetName("/FT") == "/Sig" || d.Elements.ContainsKey("/ByteRange")))
                throw new InvalidDataException("Encrypted, signed, or excessive-page PDFs are not supported.");

            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            document.Internals.AddObject(font);
            var opacity = new PdfDictionary(document);
            opacity.Elements.SetReal("/ca", 0.12);
            opacity.Elements.SetReal("/CA", 0.12);
            document.Internals.AddObject(opacity);
            var key = "Wm" + Guid.NewGuid().ToString("N");
            var timestamp = accessedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

            foreach (var page in document.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var media = page.MediaBox;
                var crop = page.CropBox.IsZero ? media : page.CropBox;
                var x = Math.Max(media.X1, crop.X1); var y = Math.Max(media.Y1, crop.Y1);
                var w = Math.Min(media.X2, crop.X2) - x;
                var h = Math.Min(media.Y2, crop.Y2) - y;
                if (!double.IsFinite(w) || !double.IsFinite(h) || w < 72 || h < 72)
                    throw new InvalidDataException("Unsupported PDF page dimensions.");
                var resources = page.Resources;
                AddResource(resources, "/Font", "/" + key + "F", font, document);
                AddResource(resources, "/ExtGState", "/" + key + "G", opacity, document);
                var available = Math.Min(w / .819152, h / .573576) * .72;
                var size = Math.Min(Math.Clamp(Math.Min(w, h) * .07, 18, 40), available / (12 * .6));
                var small = Math.Min(Math.Clamp(Math.Min(w, h) * .022, 8, 13),
                    available / (Math.Max(account.Length, timestamp.Length + 9) * .6));
                if (small < 6) throw new InvalidDataException("Page is too small for a readable account watermark.");
                // Append a separate, balanced graphics state; original streams/boxes/rotation are retained.
                page.Contents.PrependContent().CreateStream(Encoding.ASCII.GetBytes("q\n"));
                var content = new StringBuilder(FormattableString.Invariant(
                    $"Q\nq /{key}G gs 0.25 g 0.819152 0.573576 -0.573576 0.819152 {x + w / 2:0.###} {y + h / 2:0.###} cm\n"));
                Line("CONFIDENTIAL", size, 20);
                Line(account, small, 0);
                Line("Accessed " + timestamp, small, -small * 1.6);
                content.Append("Q\n");
                page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content.ToString()));

                void Line(string text, double fontSize, double y)
                {
                    var estimatedWidth = text.Length * fontSize * .52;
                    content.Append(FormattableString.Invariant(
                        $"BT /{key}F {fontSize:0.###} Tf 1 0 0 1 {-estimatedWidth / 2:0.###} {y:0.###} Tm ({Escape(text)}) Tj ET\n"));
                }
            }
            document.Version = Math.Max(document.Version, 14); // Transparency requires PDF 1.4.
            using var output = new MemoryStream();
            document.Save(output, false);
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToArray();
        }
        finally { Slots.Release(); }
    }

    private static void AddResource(PdfDictionary resources, string kind, string name,
        PdfDictionary value, PdfDocument document)
    {
        var group = resources.Elements.GetDictionary(kind);
        if (group == null) resources.Elements[kind] = group = new PdfDictionary(document);
        group.Elements[name] = value.Reference!;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string AccountLabel(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) throw new InvalidDataException("Authenticated account required.");
        var name = user.Identity.Name;
        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.All(c => c is >= ' ' and <= '~'))
            return "User: " + name;
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Account identifier unavailable.");
        // Standard PDF fonts avoid changing the application's global font resolver. Non-Latin names
        // use a safe stable account identifier instead of incorrectly shaped/missing glyphs.
        return "Account: " + (id.Length <= 64 && id.All(c => c is >= ' ' and <= '~')
            ? id : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..24]);
    }
}

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
        file is PhysicalFileResult physical && string.Equals(Path.GetExtension(physical.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);
}

public static class PdfWatermarkExtensions
{
    public static IServiceCollection AddPdfWatermarks(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPdfWatermarkService, PdfWatermarkService>();
        services.AddScoped<PdfWatermarkResultFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<PdfWatermarkResultFilter>());
        return services;
    }
}
