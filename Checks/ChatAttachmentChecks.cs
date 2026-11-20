using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Chat;
using SkiaSharp;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace SourcingTenderManagement.Checks;

// Explicit opt-in HTTP integration check. Creates only uniquely named QA accounts and removes them in finally.
public static class ChatAttachmentChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tag = "attachment-qa-" + Guid.NewGuid().ToString("N");
        var password = "Aa9!" + Guid.NewGuid().ToString("N");
        var adminEmail = tag + "-admin@example.invalid";
        var staffEmail = tag + "-staff@example.invalid";
        var supplierEmail = tag + "-supplier@example.invalid";
        var failedEmail = tag + "-blocked@example.invalid";
        var checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Attachment check failed: " + label); checks++; }
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(), ServerCertificateCustomValidationCallback = (req, _, _, _) => req.RequestUri?.Host == "localhost" }) { BaseAddress = new Uri("https://localhost:7130"), Timeout = TimeSpan.FromSeconds(100) };
        async Task<string> Token(HttpClient http, string path)
        {
            using var response = await http.GetAsync(path);
            var target = response.Headers.Location;
            var html = response.StatusCode == HttpStatusCode.Redirect && target != null && (!target.IsAbsoluteUri || target.Host == "localhost")
                ? await http.GetStringAsync(target) : await response.Content.ReadAsStringAsync();
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Check(match.Success, "antiforgery token present"); return WebUtility.HtmlDecode(match.Groups[1].Value);
        }
        async Task Login(HttpClient http, string email)
        {
            var token = await Token(http, "/Account/Login");
            using var response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token }));
            Check(response.StatusCode == HttpStatusCode.Redirect, "real password login succeeds");
        }
        byte[] Workbook(params string[][] rows)
        {
            using var workbook = new XLWorkbook(); var sheet = workbook.AddWorksheet("Accounts");
            for (var c = 0; c < AiChatAccountImport.Columns.Length; c++) sheet.Cell(1, c + 1).Value = AiChatAccountImport.Columns[c];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
            using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
        }
        async Task<HttpResponseMessage> Upload(HttpClient http, string token, byte[] data, string filename, string workspace, bool confirm = false, bool analyze = false)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(workspace), "workspace"); form.Add(new StringContent(filename.EndsWith("xlsx") && !analyze ? "أنشئ الحسابات من الملف" : "كم يوم مدة التسليم حسب الملف؟"), "question");
            form.Add(new StringContent(analyze ? "analyze" : "auto"), "intent");
            form.Add(new StringContent("SourcingOfficer"), "defaultRole"); form.Add(new StringContent(confirm.ToString()), "confirmImport");
            var file = new ByteArrayContent(data); file.Headers.ContentType = new MediaTypeHeaderValue(filename.EndsWith("xlsx") ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "image/png");
            form.Add(file, "file", filename);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/AiChat/Attachment") { Content = form };
            request.Headers.Add("RequestVerificationToken", token); return await http.SendAsync(request);
        }
        try
        {
            var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, FullName = "Attachment QA Admin", EmailConfirmed = true };
            Check((await users.CreateAsync(admin, password)).Succeeded, "QA administrator created");
            Check((await users.AddToRoleAsync(admin, RoleNames.Admin)).Succeeded, "QA administrator role assigned");
            using var http = Client(); await Login(http, adminEmail);
            var token = await Token(http, "/Admin/Dashboard");
            var page = await http.GetStringAsync("/Admin/Dashboard");
            Check(page.Contains("data-chat-attachment") && page.Contains("Accounts template"), "admin attachment controls rendered");
            using var template = await http.GetAsync("/AiChat/AccountTemplate");
            Check(template.IsSuccessStatusCode && (await template.Content.ReadAsByteArrayAsync()).Length > 100, "downloadable XLSX template");
            var data = Workbook([staffEmail, password, "QA Imported Officer", "SourcingOfficer"], [supplierEmail, password, "QA Imported Supplier", "Supplier", "", "", "QA Supplies", "Pharmaceuticals", tag]);
            using var safeExcerpt = new MemoryStream(data);
            var excerpt = AiChatSpreadsheetClient.Extract(safeExcerpt);
            Check(!excerpt.Contains(password) && excerpt.Contains(staffEmail), "analysis removes password columns while preserving data");
            using var preview = await Upload(http, token, data, "accounts.xlsx", "Admin");
            var previewText = await preview.Content.ReadAsStringAsync();
            using var previewJson = JsonDocument.Parse(previewText);
            Check(preview.IsSuccessStatusCode && previewJson.RootElement.GetProperty("previewReady").GetBoolean(), "valid workbook preview");
            Check(!previewText.Contains(password), "password excluded from response");
            Check(await users.FindByEmailAsync(staffEmail) == null, "preview creates no accounts");
            using var commit = await Upload(http, token, data, "accounts.xlsx", "Admin", true);
            using var commitJson = JsonDocument.Parse(await commit.Content.ReadAsStringAsync());
            Check(commit.IsSuccessStatusCode && commitJson.RootElement.GetProperty("createdCount").GetInt32() == 2, "confirmed import creates two accounts");
            db.ChangeTracker.Clear();
            var imported = await users.FindByEmailAsync(staffEmail);
            Check(imported != null && imported.PasswordHash != password && await users.CheckPasswordAsync(imported, password), "Identity hash verifies workbook password");
            Check(await users.IsInRoleAsync(imported!, "SourcingOfficer"), "imported role assigned");
            var supplier = await users.FindByEmailAsync(supplierEmail);
            Check(await db.SupplierProfiles.AnyAsync(s => s.UserId == supplier!.Id && s.Status == "Pending"), "supplier profile pending approval");
            using var staff = Client(); await Login(staff, staffEmail);
            Check((await staff.GetAsync("/SourcingOfficer/Dashboard")).IsSuccessStatusCode, "imported account opens its dashboard");
            var staffToken = await Token(staff, "/SourcingOfficer/Dashboard");
            using var denied = await Upload(staff, staffToken, data, "accounts.xlsx", "SourcingOfficer", true);
            Check(denied.StatusCode == HttpStatusCode.Forbidden, "non-admin account import forbidden");
            using var duplicate = await Upload(http, token, data, "accounts.xlsx", "Admin", true);
            using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
            Check(duplicateJson.RootElement.GetProperty("createdCount").GetInt32() == 0 && !duplicateJson.RootElement.GetProperty("previewReady").GetBoolean(), "retry cannot duplicate accounts");
            Check(duplicateJson.RootElement.GetProperty("existingCount").GetInt32() == 2 && duplicateJson.RootElement.GetProperty("answer").GetString()!.Contains("موجودة مسبقًا"), "admin sees a clear existing-accounts message");
            var mixedData = Workbook([staffEmail, password, "Existing Officer", "SourcingOfficer"], [failedEmail, password, "New Officer", "SourcingOfficer"]);
            using var mixed = await Upload(http, token, mixedData, "mixed.xlsx", "Admin");
            using var mixedJson = JsonDocument.Parse(await mixed.Content.ReadAsStringAsync());
            Check(mixedJson.RootElement.GetProperty("existingCount").GetInt32() == 1 && await users.FindByEmailAsync(failedEmail) == null, "mixed workbook identifies existing accounts without partial creation");
            var badData = Workbook([failedEmail, password, "Valid Row", "SourcingOfficer"], [tag + "-weak@example.invalid", "x", "Bad Row", "UnknownRole"]);
            using var bad = await Upload(http, token, badData, "invalid.xlsx", "Admin", true);
            Check(bad.IsSuccessStatusCode && await users.FindByEmailAsync(failedEmail) == null, "invalid workbook saves no partial accounts");
            using var noToken = await Upload(http, "bad-token", data, "accounts.xlsx", "Admin");
            Check(noToken.StatusCode == HttpStatusCode.BadRequest, "CSRF rejected");
            using var spoofed = await Upload(http, token, [1, 2, 3], "fake.png", "Admin");
            Check(spoofed.StatusCode == HttpStatusCode.BadRequest, "spoofed image rejected");
            using var bitmap = new SKBitmap(128, 128);
            bitmap.Erase(SKColors.Blue);
            using var picture = SKImage.FromBitmap(bitmap);
            using var png = picture.Encode(SKEncodedImageFormat.Png, 100);
            var liveAi = Environment.GetEnvironmentVariable("TIRYAQ_QA_SKIP_LIVE_AI") != "1";
            if (liveAi) {
            using var vision = await Upload(staff, staffToken, png.ToArray(), "color.png", "SourcingOfficer");
            var visionText = await vision.Content.ReadAsStringAsync();
            Check(vision.IsSuccessStatusCode, "live image upload and vision response");
            using var visionJson = JsonDocument.Parse(visionText);
            var description = visionJson.RootElement.GetProperty("answer").GetString() ?? "";
            Check(Regex.IsMatch(description, "أزرق|ازرق|blue", RegexOptions.IgnoreCase), "AI actually reads image content");
            }
            using var anonymous = Client();
            using var anon = await Upload(anonymous, token, data, "accounts.xlsx", "Admin", true);
            Check(!anon.IsSuccessStatusCode, "anonymous upload forbidden");
            using var supplierHttp = Client(); await Login(supplierHttp, supplierEmail);
            var pdfBuilder = new PdfDocumentBuilder();
            var font = pdfBuilder.AddStandard14Font(Standard14Font.Helvetica);
            pdfBuilder.AddPage(595, 842).AddText("Tiryaq quotation: delivery is 7 days.", 12, new PdfPoint(30, 700), font);
            var pdfBytes = pdfBuilder.Build();
            using var pdfStream = new MemoryStream(pdfBytes);
            var extracted = AiChatPdfClient.Extract(pdfStream);
            Check(extracted.Pages == 1 && extracted.Text.Contains("7 days"), "PDF text extraction preserves evidence");
            if (liveAi) {
            using var pdfReply = await Upload(staff, staffToken, pdfBytes, "quotation.pdf", "SourcingOfficer");
            Check(pdfReply.IsSuccessStatusCode, "real PDF analysis response");
            using var pdfJson = JsonDocument.Parse(await pdfReply.Content.ReadAsStringAsync());
            Check(Regex.IsMatch(pdfJson.RootElement.GetProperty("answer").GetString() ?? "", "7|سبعة|سبع"), "PDF answer is grounded in uploaded text");
            }
            foreach (var workspace in RoleNames.Primary)
            {
                using var roleClient = Client();
                var email = workspace == RoleNames.Admin ? adminEmail : workspace == RoleNames.Supplier ? supplierEmail : workspace == RoleNames.SourcingOfficer ? staffEmail : tag + "-" + workspace + "@example.invalid";
                if (workspace is not (RoleNames.Admin or RoleNames.Supplier or RoleNames.SourcingOfficer))
                {
                    var member = new ApplicationUser { UserName = email, Email = email, FullName = "QA " + workspace, EmailConfirmed = true };
                    Check((await users.CreateAsync(member, password)).Succeeded, "temporary role account created");
                    Check((await users.AddToRoleAsync(member, workspace)).Succeeded, "temporary role assigned");
                }
                await Login(roleClient, email);
                var roleToken = await Token(roleClient, "/Account/EditProfile");
                using var imageAccess = await Upload(roleClient, roleToken, [1, 2, 3], "invalid.png", workspace);
                Check(imageAccess.StatusCode == HttpStatusCode.BadRequest, workspace + " reaches image validation");
                using var pdfAccess = await Upload(roleClient, roleToken, [1, 2, 3], "invalid.pdf", workspace);
                Check(pdfAccess.StatusCode == HttpStatusCode.BadRequest, workspace + " reaches PDF validation");
                using var excelAnalysisAccess = await Upload(roleClient, roleToken, [1, 2, 3], "invalid.xlsx", workspace, analyze: true);
                Check(excelAnalysisAccess.StatusCode == HttpStatusCode.BadRequest, workspace + " reaches Excel analysis validation");
                if (workspace == RoleNames.EvaluationCommittee && (liveAi || Environment.GetEnvironmentVariable("TIRYAQ_QA_LIVE_EXCEL") == "1"))
                {
                    using var book = new XLWorkbook();
                    var sheet = book.AddWorksheet("Offers");
                    sheet.Cell(1, 1).Value = "Supplier"; sheet.Cell(1, 2).Value = "DeliveryDays";
                    sheet.Cell(2, 1).Value = "QA Supplies"; sheet.Cell(2, 2).Value = 7;
                    using var output = new MemoryStream(); book.SaveAs(output);
                    using var analysisReply = await Upload(roleClient, roleToken, output.ToArray(), "offers.xlsx", workspace, analyze: true);
                    Check(analysisReply.IsSuccessStatusCode, "EvaluationCommittee can analyze a real workbook");
                    using var analysisJson = JsonDocument.Parse(await analysisReply.Content.ReadAsStringAsync());
                    Check(!analysisJson.RootElement.GetProperty("mutated").GetBoolean() && Regex.IsMatch(analysisJson.RootElement.GetProperty("answer").GetString() ?? "", "7|سبعة|سبع"), "Excel analysis is grounded and read-only");
                }
                if (workspace != RoleNames.Admin)
                {
                    using var excelAccess = await Upload(roleClient, roleToken, data, "accounts.xlsx", workspace, true);
                    Check(excelAccess.StatusCode == HttpStatusCode.Forbidden, workspace + " cannot create accounts from Excel");
                    using var templateAccess = await roleClient.GetAsync("/AiChat/AccountTemplate");
                    Check(!templateAccess.IsSuccessStatusCode, workspace + " cannot download admin template");
                }
            }
            Console.WriteLine($"PASS: {checks} chat attachment checks, real Excel import/password login, role isolation, preview, duplicate protection, atomic validation, pending supplier profile, CSRF and invalid image checks.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            var fixtures = await db.Users.Where(u => u.Email != null && u.Email.StartsWith(tag)).ToListAsync();
            var ids = fixtures.Select(u => u.Id).ToArray();
            await db.SupplierProfiles.Where(s => ids.Contains(s.UserId)).ExecuteDeleteAsync();
            foreach (var fixture in fixtures) await users.DeleteAsync(fixture);
            Console.WriteLine("Removed only this check's temporary accounts.");
        }
    }
}
