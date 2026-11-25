using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services;

namespace SourcingTenderManagement.Checks;

// Opt-in integration test against the local app. Deletes only its own accounts, tender and files.
public static class ProductPhotoChecks
{
    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var photos = scope.ServiceProvider.GetRequiredService<OfferProductPhotos>();
        var tag = "photo-qa-" + Guid.NewGuid().ToString("N");
        var password = "Aa9!" + Guid.NewGuid().ToString("N");
        var accounts = new List<ApplicationUser>();
        var files = new HashSet<string>();
        var checks = 0; int? requestId = null, tenderId = null;
        void Require(bool ok, string label) { if (!ok) throw new Exception("Product photo check failed: " + label); checks++; }
        HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(), ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri?.Host == "localhost" }) { BaseAddress = new Uri("https://localhost:7130") };
        async Task<ApplicationUser> Account(string suffix, string role)
        {
            var account = new ApplicationUser { Email = tag + suffix + "@example.invalid", UserName = tag + suffix + "@example.invalid", FullName = "Photo QA " + suffix, EmailConfirmed = true };
            Require((await users.CreateAsync(account, password)).Succeeded, "temporary account created"); accounts.Add(account);
            Require((await users.AddToRoleAsync(account, role)).Succeeded, "temporary role assigned"); return account;
        }
        async Task<string> Token(HttpClient http, string path)
        {
            var text = await http.GetStringAsync(path);
            var match = Regex.Match(text, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Require(match.Success, "antiforgery token available"); return WebUtility.HtmlDecode(match.Groups[1].Value);
        }
        async Task Login(HttpClient http, ApplicationUser user)
        {
            var token = await Token(http, "/Account/Login");
            using var response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = user.Email!, ["Password"] = password, ["__RequestVerificationToken"] = token }));
            Require(response.StatusCode == HttpStatusCode.Redirect, "real password sign-in");
        }
        byte[] Picture(SKColor color) { using var bitmap = new SKBitmap(64, 64); bitmap.Erase(color); using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); return png.ToArray(); }
        try
        {
            var officer = await Account("officer", RoleNames.SourcingOfficer);
            var supplier = await Account("supplier", RoleNames.Supplier);
            var other = await Account("competitor", RoleNames.Supplier);
            var profile = new SupplierProfile { UserId = supplier.Id, CompanyName = "Photo QA Supplies", ContactName = "QA Supplier", RegistrationNumber = tag, Category = "Pharmaceuticals", Status = "Approved", CreatedAt = DateTime.UtcNow };
            var competitor = new SupplierProfile { UserId = other.Id, CompanyName = "Photo QA Competitor", ContactName = "QA Supplier", RegistrationNumber = tag + "-2", Category = "Pharmaceuticals", Status = "Approved", CreatedAt = DateTime.UtcNow };
            db.SupplierProfiles.AddRange(profile, competitor);
            var department = await db.Departments.FirstAsync(d => d.IsActive);
            var request = new PurchaseRequest { DepartmentId = department.DepartmentId, RequestedByUserId = officer.Id, Title = tag, Reason = "Local product photo integration test", Priority = "Normal", TargetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ConvertedToTender", CreatedAt = DateTime.UtcNow };
            db.PurchaseRequests.Add(request); await db.SaveChangesAsync(); requestId = request.PurchaseRequestId;
            var item = new PurchaseRequestItem { PurchaseRequestId = request.PurchaseRequestId, ItemName = "QA product", Quantity = 2, Unit = "packs", Specifications = "Local test product" };
            db.PurchaseRequestItems.Add(item);
            var tender = new Tender { PurchaseRequestId = request.PurchaseRequestId, CreatedByUserId = officer.Id, Title = tag, Description = "Local QA fixture", Category = "Pharmaceuticals", Status = "Published", OpensAt = DateTime.UtcNow.AddDays(-1), ClosesAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow };
            db.Tenders.Add(tender); await db.SaveChangesAsync(); tenderId = tender.TenderId;
            var invitation = new Invitation { TenderId = tender.TenderId, SupplierProfileId = profile.SupplierProfileId, InvitedByUserId = officer.Id, Status = "Accepted", SentAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow };
            db.Invitations.Add(invitation); await db.SaveChangesAsync();
            using var owner = Client(); await Login(owner, supplier);
            var path = $"/Supplier/CreateOffer?invitationId={invitation.InvitationId}";
            var token = await Token(owner, path);
            Require((await owner.GetStringAsync(path)).Contains("Items[0].ProductPhoto"), "per-item photo upload rendered");
            async Task<HttpResponseMessage> Save(int? bidId, byte[]? photo, string command = "Draft")
            {
                using var form = new MultipartFormDataContent();
                var fields = new Dictionary<string, string> { ["InvitationId"] = invitation.InvitationId.ToString(), ["TenderId"] = tender.TenderId.ToString(), ["TenderTitle"] = tag, ["ClosesAt"] = tender.ClosesAt.ToString("O"), ["Currency"] = "JOD", ["DeliveryDays"] = "2", ["AdditionalCosts"] = "0", ["submissionCommand"] = command,
                    ["Items[0].PurchaseRequestItemId"] = item.PurchaseRequestItemId.ToString(), ["Items[0].ItemName"] = item.ItemName, ["Items[0].Unit"] = item.Unit, ["Items[0].RequestedQuantity"] = "2", ["Items[0].RequestedSpecifications"] = item.Specifications, ["Items[0].Quantity"] = "2", ["Items[0].UnitPrice"] = "10", ["Items[0].Specifications"] = "QA product response", ["__RequestVerificationToken"] = token };
                if (bidId.HasValue) fields["BidId"] = bidId.Value.ToString();
                foreach (var field in fields) form.Add(new StringContent(field.Value), field.Key);
                if (photo != null) form.Add(new ByteArrayContent(photo), "Items[0].ProductPhoto", "product.png");
                return await owner.PostAsync("/Supplier/SaveOffer", form);
            }
            using var saved = await Save(null, Picture(SKColors.Blue));
            Require(saved.StatusCode == HttpStatusCode.Redirect, "draft with photo saved");
            var bid = await db.Bids.AsNoTracking().SingleAsync(b => b.InvitationId == invitation.InvitationId);
            var document = await db.BidDocuments.AsNoTracking().SingleAsync(d => d.BidId == bid.BidId);
            var stored = photos.PathFor(document.StoredFileName); files.Add(stored);
            Require(document.DocumentType == OfferProductPhotos.Type(item.PurchaseRequestItemId) && document.ContentType == "image/jpeg" && File.Exists(stored), "private JPEG photo persisted with product association");
            using var imageReply = await owner.GetAsync("/OfferProductPhotos/" + document.BidDocumentId);
            Require(imageReply.IsSuccessStatusCode && imageReply.Content.Headers.ContentType?.MediaType == "image/jpeg", "owner can view photo");
            Require((await owner.GetStringAsync("/Supplier/Offer/" + bid.BidId)).Contains("Product photos"), "saved photo gallery rendered");
            Require((await owner.GetStringAsync(path + "&bidId=" + bid.BidId)).Contains("/OfferProductPhotos/" + document.BidDocumentId), "saved photo retained in draft form");
            using var outsider = Client(); await Login(outsider, other);
            Require((await outsider.GetAsync("/OfferProductPhotos/" + document.BidDocumentId)).StatusCode == HttpStatusCode.NotFound, "competitor cannot read product photo");
            using var officerHttp = Client(); await Login(officerHttp, officer);
            Require((await officerHttp.GetAsync("/OfferProductPhotos/" + document.BidDocumentId)).StatusCode == HttpStatusCode.NotFound, "officer cannot read draft photo");
            using var kept = await Save(bid.BidId, null);
            Require(kept.StatusCode == HttpStatusCode.Redirect && await db.BidDocuments.CountAsync(d => d.BidId == bid.BidId) == 1, "draft resave keeps existing photo");
            using var bad = await Save(bid.BidId, [1, 2, 3]);
            Require(bad.StatusCode == HttpStatusCode.OK && (await bad.Content.ReadAsStringAsync()).Contains("not a readable image"), "spoofed image rejected with validation message");
            Require(File.Exists(stored), "invalid replacement preserves original photo");
            using var replaced = await Save(bid.BidId, Picture(SKColors.Green));
            var replacement = await db.BidDocuments.AsNoTracking().SingleAsync(d => d.BidId == bid.BidId);
            files.Add(photos.PathFor(replacement.StoredFileName));
            Require(replaced.StatusCode == HttpStatusCode.Redirect && replacement.BidDocumentId != document.BidDocumentId && !File.Exists(stored), "replacement updates record and removes old file after commit");
            using var submitted = await Save(bid.BidId, null, "Submit");
            Require(submitted.StatusCode == HttpStatusCode.Redirect && await db.Bids.AnyAsync(b => b.BidId == bid.BidId && b.Status == "Submitted"), "offer submits with retained photo");
            Require((await officerHttp.GetAsync("/OfferProductPhotos/" + replacement.BidDocumentId)).IsSuccessStatusCode, "assigned officer can view submitted photo");
            using var anonymous = Client(); Require(!(await anonymous.GetAsync("/OfferProductPhotos/" + replacement.BidDocumentId)).IsSuccessStatusCode, "anonymous photo access blocked");
            Console.WriteLine($"PASS: {checks} product photo checks; real upload, persistence, draft retention, replacement, invalid image rejection, submission, gallery and owner/reviewer/competitor access.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (tenderId.HasValue)
            {
                var bids = db.Bids.Where(b => b.TenderId == tenderId).Select(b => b.BidId);
                foreach (var doc in await db.BidDocuments.Where(d => bids.Contains(d.BidId)).ToListAsync()) files.Add(photos.PathFor(doc.StoredFileName));
                await db.BidDocuments.Where(d => bids.Contains(d.BidId)).ExecuteDeleteAsync();
                await db.BidItems.Where(i => bids.Contains(i.BidId)).ExecuteDeleteAsync();
                await db.Bids.Where(b => b.TenderId == tenderId).ExecuteDeleteAsync();
                await db.Invitations.Where(i => i.TenderId == tenderId).ExecuteDeleteAsync();
                await db.Tenders.Where(t => t.TenderId == tenderId).ExecuteDeleteAsync();
            }
            if (requestId.HasValue) { await db.PurchaseRequestItems.Where(i => i.PurchaseRequestId == requestId).ExecuteDeleteAsync(); await db.PurchaseRequests.Where(r => r.PurchaseRequestId == requestId).ExecuteDeleteAsync(); }
            await db.Notifications.Where(n => n.Message.Contains(tag)).ExecuteDeleteAsync();
            var ids = accounts.Select(a => a.Id).ToArray();
            await db.SupplierProfiles.Where(s => ids.Contains(s.UserId)).ExecuteDeleteAsync();
            foreach (var account in accounts) await users.DeleteAsync(account);
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            Console.WriteLine("Removed only product-photo QA fixtures and files.");
        }
    }
}
