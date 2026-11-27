using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services;

// Bank guarantees and warranties that suppliers attach to offers: check, save, load and verify them.
public sealed class OfferGuarantees(ApplicationDbContext db, IWebHostEnvironment environment)
{
    private static (string Prefix, string Kind, GuaranteeInput Input)[] Inputs(OfferFormViewModel form) =>
        [(nameof(form.LegalWarranty), "Legal", form.LegalWarranty),
         (nameof(form.BankGuarantee), "Bank", form.BankGuarantee)];

    public string PathFor(string stored) => Path.Combine(environment.ContentRootPath,
        "App_Data", "private-files", "guarantees", Path.GetFileName(stored));

    public async Task LoadAsync(OfferFormViewModel form)
    {
        if (!form.BidId.HasValue) return;
        var saved = await db.BidGuarantees.AsNoTracking().Where(g => g.BidId == form.BidId).ToListAsync();
        foreach (var (_, kind, input) in Inputs(form))
        {
            var guarantee = saved.SingleOrDefault(g => g.Kind == kind);
            if (guarantee is null) continue;
            input.Enabled = true;
            input.ReferenceNumber = guarantee.ReferenceNumber;
            input.Issuer = guarantee.Issuer;
            input.Coverage = guarantee.Coverage;
            input.Scope = guarantee.Scope;
            input.StartsOn = guarantee.StartsOn;
            input.ExpiresOn = guarantee.ExpiresOn;
            input.ExistingFileName = guarantee.OriginalFileName;
        }
    }

    public async Task ValidateAsync(OfferFormViewModel form, bool submitting, ModelStateDictionary errors)
    {
        var saved = await db.BidGuarantees.AsNoTracking()
            .Where(g => g.BidId == form.BidId).ToListAsync();
        foreach (var (prefix, kind, input) in Inputs(form))
        {
            input.ExistingFileName = saved.SingleOrDefault(g => g.Kind == kind)?.OriginalFileName;
            if (!input.Enabled) continue;
            void Error(string field, string message) => errors.AddModelError(prefix + "." + field, message);
            if (string.IsNullOrWhiteSpace(input.ReferenceNumber))
                Error(nameof(input.ReferenceNumber), "Enter the reference number printed on the document.");
            if (string.IsNullOrWhiteSpace(input.Issuer))
                Error(nameof(input.Issuer), kind == "Bank" ? "Enter the issuing bank." : "Enter the issuing legal authority.");
            if (input.Coverage is not ("Product" or "Service"))
                Error(nameof(input.Coverage), "Choose Product or Service.");
            if (string.IsNullOrWhiteSpace(input.Scope))
                Error(nameof(input.Scope), "Describe the product or service covered by this guarantee.");
            if (!input.StartsOn.HasValue) Error(nameof(input.StartsOn), "Enter the start date.");
            if (!input.ExpiresOn.HasValue) Error(nameof(input.ExpiresOn), "Enter the expiry date.");
            if (input.StartsOn > input.ExpiresOn) Error(nameof(input.ExpiresOn), "Expiry must be on or after the start date.");
            if (submitting && input.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow))
                Error(nameof(input.ExpiresOn), "An expired guarantee cannot be submitted.");
            if (input.Document is null && input.ExistingFileName is null)
                Error(nameof(input.Document), "Attach the guarantee document.");
            if (input.Document is not null)
            {
                try { await PrepareDocumentAsync(input.Document); }
                catch (ValidationException ex) { Error(nameof(input.Document), ex.Message); }
            }
        }
    }

    public static async Task<(byte[] Bytes, string Extension, string ContentType)> PrepareDocumentAsync(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length is <= 0 or > 5_000_000 || extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            throw new ValidationException("Choose a PDF, JPG or PNG document, up to 5 MB.");
        if (extension != ".pdf")
            return (await OfferProductPhotos.PrepareAsync(file), ".jpg", "image/jpeg");

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var bytes = stream.ToArray();
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
            if (pdf.NumberOfPages < 1) throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
        {
            throw new ValidationException("Choose a readable, unencrypted PDF document.");
        }
        return (bytes, ".pdf", "application/pdf");
    }

    // Runs inside SaveOffer's transaction and tender lock, sharing its file rollback/cleanup.
    public async Task SaveAsync(OfferFormViewModel form, Bid bid, List<string> createdFiles, List<string> replacedFiles)
    {
        var saved = await db.BidGuarantees.Where(g => g.BidId == bid.BidId).ToListAsync();
        foreach (var (_, kind, input) in Inputs(form))
        {
            var guarantee = saved.SingleOrDefault(g => g.Kind == kind);
            if (!input.Enabled)
            {
                if (guarantee is null) continue;
                replacedFiles.Add(PathFor(guarantee.StoredFileName));
                db.BidGuarantees.Remove(guarantee);
                continue;
            }
            guarantee ??= new BidGuarantee { BidId = bid.BidId, Kind = kind };
            var changed = guarantee.ReferenceNumber != input.ReferenceNumber!.Trim()
                || guarantee.Issuer != input.Issuer!.Trim() || guarantee.Scope != input.Scope!.Trim()
                || guarantee.Coverage != input.Coverage || guarantee.StartsOn != input.StartsOn!.Value
                || guarantee.ExpiresOn != input.ExpiresOn!.Value || input.Document is not null;
            if (input.Document is not null)
            {
                var document = await PrepareDocumentAsync(input.Document);
                Directory.CreateDirectory(Path.GetDirectoryName(PathFor("document"))!);
                if (guarantee.StoredFileName.Length > 0) replacedFiles.Add(PathFor(guarantee.StoredFileName));
                var stored = Guid.NewGuid().ToString("N") + document.Extension;
                var filePath = PathFor(stored);
                createdFiles.Add(filePath);
                await File.WriteAllBytesAsync(filePath, document.Bytes);
                guarantee.StoredFileName = stored;
                guarantee.OriginalFileName = Path.GetFileName(input.Document.FileName)[..Math.Min(255, Path.GetFileName(input.Document.FileName).Length)];
                guarantee.ContentType = document.ContentType;
                guarantee.FileSizeBytes = document.Bytes.Length;
            }
            guarantee.ReferenceNumber = input.ReferenceNumber!.Trim();
            guarantee.Issuer = input.Issuer!.Trim();
            guarantee.Scope = input.Scope!.Trim();
            guarantee.Coverage = input.Coverage;
            guarantee.StartsOn = input.StartsOn!.Value;
            guarantee.ExpiresOn = input.ExpiresOn!.Value;
            if (changed)
            {
                guarantee.VerificationToken = Guid.NewGuid().ToString("N");
                guarantee.UpdatedAt = DateTime.UtcNow;
            }
            if (guarantee.BidGuaranteeId == 0) db.BidGuarantees.Add(guarantee);
        }
        await db.SaveChangesAsync();
    }

    public static GuaranteeVerificationView Verification(BidGuarantee? guarantee, string? number, DateOnly today)
    {
        if (guarantee is null)
            return new(null, "Record not found", "The verification link is invalid or has been replaced.");
        if (number is not null && !string.Equals(number.Trim(), guarantee.ReferenceNumber, StringComparison.OrdinalIgnoreCase))
            return new(null, "Number does not match", "The entered reference does not match this verification link.", false);
        if (guarantee.Bid.Status is "Withdrawn" or "Cancelled" || guarantee.Bid.Invitation.Tender.Status == "Cancelled")
            return new(guarantee, "Withdrawn", "The associated offer or tender has been withdrawn or cancelled.");
        if (guarantee.ExpiresOn < today)
            return new(guarantee, "Expired", "The recorded guarantee has passed its expiry date.");
        if (guarantee.StartsOn > today)
            return new(guarantee, "Not yet active", "The recorded guarantee starts on a future date.");
        return new(guarantee, "Within validity period", "The reference was found in the Tiryaq demo registry and the recorded dates are currently valid.");
    }
}
