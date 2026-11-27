using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using SkiaSharp;
using SourcingTenderManagement.Services.Ai;
using SourcingTenderManagement.Services.Chat;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SourcingTenderManagement.Checks;

internal static class AiChatAttachmentChecks
{
    public static async Task RunAsync()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Quotes");
        sheet.Cell("A1").Value = "Item";
        sheet.Cell("A2").Value = "Sample Quote";
        sheet.Cell("B1").Value = "Temporary password";
        sheet.Cell("B2").Value = "credential-must-not-leave";
        using var file = new MemoryStream();
        workbook.SaveAs(file);
        file.Position = 0;

        var handler = new ProviderFixture();
        using var http = new HttpClient(handler);
        var service = new AiChatAttachmentAnalysis(http, VllmAiChecks.LocalFixtureConfiguration(), AiGateway.Detached);
        var workbookReply = await service.AnalyzeAsync(file, ".xlsx", "Summarize the quote", "Department", default);
        if (workbookReply != "Summary." || !handler.SawWorkbook)
            throw new Exception("Spreadsheet analysis did not send the expected bounded excerpt.");

        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var imageStream = new MemoryStream(png.ToArray());
        await service.AnalyzeAsync(imageStream, ".png", "What is shown?", "Department", default);
        if (!handler.SawImage)
            throw new Exception("Image analysis did not send re-encoded image content through the gateway.");

        var pdf = new PdfDocumentBuilder();
        var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
        pdf.AddPage(595, 842).AddText("Delivery is 7 days.", 12, new PdfPoint(30, 700), font);
        using var pdfStream = new MemoryStream(pdf.Build());
        await service.AnalyzeAsync(pdfStream, ".pdf", "What is the delivery time?", "Department", default);
        if (!handler.SawPdf)
            throw new Exception("PDF analysis did not preserve the page reference and text.");

        using var invalid = new MemoryStream("not a PDF"u8.ToArray());
        try
        {
            await service.AnalyzeAsync(invalid, ".pdf", "Summarize", "Department", default);
            throw new Exception("Invalid PDF reached the model.");
        }
        catch (ValidationException) { }
        if (handler.Calls != 3)
            throw new Exception("An invalid or extra attachment reached the model.");
        Console.WriteLine("PASS: read-only attachment analysis, credential column redaction, image payload and invalid PDF rejection.");
    }

    private sealed class ProviderFixture : HttpMessageHandler
    {
        public bool SawWorkbook;
        public bool SawImage;
        public bool SawPdf;
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var content = payload.RootElement.GetProperty("messages")[1].GetProperty("content");
            if (content.ValueKind == JsonValueKind.String)
            {
                using var prompt = JsonDocument.Parse(content.GetString()!);
                var excerpt = prompt.RootElement.GetProperty("excerpt").GetString()!;
                SawWorkbook |= excerpt.Contains("Sample Quote") && excerpt.Contains("<untrusted_data") &&
                    !excerpt.Contains("credential-must-not-leave");
                SawPdf |= excerpt.Contains("[Page 1]") && excerpt.Contains("Delivery is 7 days.");
            }
            else
            {
                SawImage = content[1].GetProperty("image_url").GetProperty("url").GetString()!
                    .StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    choices = new[] { new { message = new { content = """{"answer":"Summary."}""" } } }
                })
            };
        }
    }
}
