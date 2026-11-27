using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace SourcingTenderManagement.Services.PdfWatermark;

// Draws the watermark text onto every page of a PDF.
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
