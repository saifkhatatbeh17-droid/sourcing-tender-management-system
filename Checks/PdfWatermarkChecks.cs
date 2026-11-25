using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SourcingTenderManagement.Services.PdfWatermark;

namespace SourcingTenderManagement.Checks;

// Invoke from a separate harness, never normal application/database startup.
public static class PdfWatermarkChecks
{
    public static async Task RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var passed = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            passed++;
        }
        async Task Fails(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { passed++; return; }
            throw new InvalidOperationException(name);
        }
        static ClaimsPrincipal Actor(string name = "qa@example.invalid", string id = "qa-account") =>
            new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, id)], "Synthetic"));
        var clock = new FixedClock();
        var service = new PdfWatermarkService();
        var filter = new PdfWatermarkResultFilter(service, clock, NullLogger<PdfWatermarkResultFilter>.Instance);
        var source = Fixture();
        var originalHash = System.Security.Cryptography.SHA256.HashData(source);
        async Task<byte[]> Stamp(ClaimsPrincipal user) => await service.ApplyAsync(new MemoryStream(source), user, clock.GetUtcNow());
        var result = await Stamp(Actor());
        using (var before = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Import))
        using (var after = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import))
        {
            Check(after.PageCount == before.PageCount, "Page count retained");
            for (var i = 0; i < after.PageCount; i++)
            {
                Check(before.Pages[i].MediaBox == after.Pages[i].MediaBox &&
                    before.Pages[i].CropBox == after.Pages[i].CropBox &&
                    before.Pages[i].Rotate == after.Pages[i].Rotate, "Boxes and rotation retained");
                var content = Text(after.Pages[i]);
                Check(content.Contains("CONFIDENTIAL"), "Every page has watermark");
                Check(content.Contains("qa@example.invalid") && content.Contains("2026-11-14 12:34:56 UTC"), "Account and UTC time");
                Check(content.Contains("ORIGINAL CONTENT"), "Original content retained");
            }
        }
        Check(originalHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(source)), "Source buffer unchanged");
        var other = await Stamp(Actor("other@example.invalid"));
        using (var pdf = PdfReader.Open(new MemoryStream(other), PdfDocumentOpenMode.Import))
            Check(Text(pdf.Pages[0]).Contains("other@example.invalid") &&
                !Text(pdf.Pages[0]).Contains("qa@example.invalid"), "No other user's watermark reused");
        var unicode = await Stamp(Actor("اسم المستخدم"));
        using (var pdf = PdfReader.Open(new MemoryStream(unicode), PdfDocumentOpenMode.Import))
            Check(Text(pdf.Pages[0]).Contains("qa-account"), "Non-Latin name uses safe account ID");
        var escaped = await Stamp(Actor("name(quoted)\\account"));
        using (var pdf = PdfReader.Open(new MemoryStream(escaped), PdfDocumentOpenMode.Import))
            Check(pdf.PageCount == 3, "PDF string metacharacters safely encoded");
        await Fails(() => service.ApplyAsync(new MemoryStream([1, 2, 3]), Actor(), clock.GetUtcNow()), "Invalid input rejected");
        await Fails(() => service.ApplyAsync(new MemoryStream(new byte[PdfWatermarkService.MaxInputBytes + 1]), Actor(), clock.GetUtcNow()), "Oversized input rejected");
        await Fails(() => service.ApplyAsync(new MemoryStream(source), new ClaimsPrincipal(), clock.GetUtcNow()), "Service requires authenticated identity");
        await Fails(() => service.ApplyAsync(new MemoryStream(source), Actor(), clock.GetUtcNow(), new CancellationToken(true)), "Cancellation honored");
        await Fails(() => service.ApplyAsync(new MemoryStream(Fixture(encrypted: true)), Actor(), clock.GetUtcNow()), "Protected input rejected");
        await Fails(() => service.ApplyAsync(new MemoryStream(Fixture(signed: true)), Actor(), clock.GetUtcNow()), "Signature-bearing input rejected");
        await Fails(() => service.ApplyAsync(new MemoryStream(Fixture(pages: 501)), Actor(), clock.GetUtcNow()), "Excess page count rejected");

        var report = new Services.TopCommittee.TopCommitteeReportExport().Pdf(new Models.TopCommittee.CommitteeReportPage(
            new(), new(0, 0, 0, 0, 0, 0, 0), [], clock.GetUtcNow().UtcDateTime));
        var reportStamped = await service.ApplyAsync(new MemoryStream(report), Actor(), clock.GetUtcNow());
        using (var before = PdfReader.Open(new MemoryStream(report), PdfDocumentOpenMode.Import))
        using (var after = PdfReader.Open(new MemoryStream(reportStamped), PdfDocumentOpenMode.Import))
            Check(after.Info.Title == before.Info.Title && after.PageCount == before.PageCount &&
                Enumerable.Range(0, after.PageCount).All(i => Text(after.Pages[i]).Contains("CONFIDENTIAL") &&
                    after.Pages[i].MediaBox == before.Pages[i].MediaBox), "Existing report generator and page layout compatible");
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "watermarked-report.pdf"), reportStamped);

        async Task<ResultExecutingContext> Run(IActionResult file, ClaimsPrincipal? user = null, int status = 200)
        {
            var http = new DefaultHttpContext { User = user ?? Actor() };
            http.Response.StatusCode = status;
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var context = new ResultExecutingContext(action, [], file, new object());
            var called = 0;
            await filter.OnResultExecutionAsync(context, () =>
            {
                called++;
                return Task.FromResult(new ResultExecutedContext(action, [], context.Result, context.Controller));
            });
            Check(called == 1, "Result delegate called once");
            return context;
        }
        foreach (var mime in new[] { "text/csv", "image/png", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/msword" })
        {
            var nonPdf = new FileContentResult([1, 2, 3], mime);
            var context = await Run(nonPdf);
            Check(ReferenceEquals(nonPdf, context.Result) && context.HttpContext.Response.Headers.Count == 0, "Non-PDF response untouched");
        }
        var named = await Run(new FileContentResult(source, "application/pdf") { FileDownloadName = "existing-name.pdf", EnableRangeProcessing = true });
        Check(named.Result is FileContentResult { FileDownloadName: "existing-name.pdf", EnableRangeProcessing: false }, "Filename retained; stale original ranges disabled");
        Check(named.HttpContext.Response.Headers.CacheControl.ToString().Contains("no-store"), "Personalized response not cached");
        var inline = await Run(new FileContentResult(source, "application/pdf"));
        Check(inline.Result is FileContentResult { FileDownloadName: "" }, "Inline result remains inline");
        var path = Path.Combine(outputDirectory, "source-upload.pdf");
        await File.WriteAllBytesAsync(path, source);
        var physical = await Run(new PhysicalFileResult(path, "application/octet-stream") { FileDownloadName = "upload.pdf" });
        Check(physical.Result is FileContentResult { ContentType: "application/pdf" }, "Generic MIME PDF download covered");
        var storedBytes = await File.ReadAllBytesAsync(path);
        Check(source.SequenceEqual(storedBytes), "Stored file unchanged");
        var stream = new MemoryStream(source);
        var streamed = await Run(new FileStreamResult(stream, "application/pdf"));
        Check(streamed.Result is FileContentResult && !stream.CanRead, "File stream transformed and disposed");
        var badStream = new MemoryStream([1, 2, 3]);
        var failed = await Run(new FileStreamResult(badStream, "application/pdf"));
        Check(failed.Result is ObjectResult { StatusCode: 422 } && !badStream.CanRead, "Failure closes stream and never falls back");
        var rejected = new FileContentResult(source, "application/pdf");
        Check(ReferenceEquals((await Run(rejected, status: 403)).Result, rejected), "Unsuccessful response untouched");
        Check(ReferenceEquals((await Run(rejected, new ClaimsPrincipal())).Result, rejected), "Anonymous result does not change authorization behavior");
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "watermarked-mixed-pages.pdf"), result);
        Console.WriteLine($"PASS: {passed} database-free PDF watermark checks. Synthetic PDFs only.");
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 14, 12, 34, 56, TimeSpan.Zero);
    }

    private static string Text(PdfPage page) => string.Join("\n", ContentReader.ReadContent(page)
        .OfType<COperator>().SelectMany(op => op.Operands.OfType<CString>()).Select(s => s.Value));

    private static byte[] Fixture(int pages = 3, bool encrypted = false, bool signed = false)
    {
        using var pdf = new PdfDocument();
        if (encrypted) { pdf.SecuritySettings.UserPassword = "synthetic-user"; pdf.SecuritySettings.OwnerPassword = "synthetic-owner"; }
        var font = new PdfDictionary(pdf);
        font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica");
        pdf.Internals.AddObject(font);
        if (signed)
        {
            var field = new PdfDictionary(pdf); field.Elements.SetName("/FT", "/Sig"); pdf.Internals.AddObject(field);
            pdf.Info.Elements["/SyntheticSignatureField"] = field.Reference!;
        }
        for (var i = 0; i < pages; i++)
        {
            var page = pdf.AddPage();
            if (i == 1) { page.Width = PdfSharp.Drawing.XUnit.FromPoint(842); page.Height = PdfSharp.Drawing.XUnit.FromPoint(595); }
            if (i == 2) { page.Rotate = 90; page.CropBox = new PdfRectangle(new PdfSharp.Drawing.XRect(10, 20, 490, 680)); }
            var fonts = new PdfDictionary(pdf); fonts.Elements["/OriginalFont"] = font.Reference!;
            page.Resources.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("q 0.95 g 40 40 350 180 re f Q\nBT /OriginalFont 14 Tf 0 g 1 0 0 1 45 180 Tm (ORIGINAL CONTENT) Tj ET\n"));
        }
        using var bytes = new MemoryStream(); pdf.Save(bytes, false); return bytes.ToArray();
    }
}
