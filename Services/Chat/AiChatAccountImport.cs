using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Services.Chat;

public sealed record AccountImportRow(int Row, string Email, string FullName, string Role, int? DepartmentId,
    string? CommitteeRole, string CompanyName, string Category, string RegistrationNumber, string Password);
public sealed record AccountImportPreview(int Row, string Email, string FullName, string Role, string[] Errors, bool AlreadyExists = false);
public sealed record AccountImportResult(AccountImportPreview[] Rows, int Created);

// Passwords never enter an AI prompt, response, log, or staged file. Identity hashes them on creation.
public sealed class AiChatAccountImport(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public static readonly string[] Columns = ["Email", "Password", "FullName", "Role", "DepartmentId", "CommitteeRole", "CompanyName", "Category", "RegistrationNumber"];

    public static byte[] Template()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Accounts");
        for (var n = 0; n < Columns.Length; n++) sheet.Cell(1, n + 1).Value = Columns[n];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().Width = 24;
        sheet.Column(6).Width = 32;
        var instructions = book.AddWorksheet("Instructions");
        string[] help = [
            "Fill the Accounts sheet. Maximum 100 rows. Do not change the Email and Password headers.",
            "Email and Password are required. Preserve passwords as plain cell values, not formulas.",
            "Passwords follow the site's Identity policy: at least 8 characters, uppercase, lowercase, digit and symbol.",
            "Role is optional when the role is selected in chat. Allowed: " + string.Join(", ", RoleNames.Primary),
            "Department accounts need DepartmentId from the admin Departments page.",
            "EvaluationCommittee accounts need one free CommitteeRole: " + string.Join(", ", RoleNames.EvaluationPositions),
            "TopCommittee accounts need one free CommitteeRole: " + string.Join(", ", RoleNames.TopPositions),
            "Supplier accounts need CompanyName, RegistrationNumber and Category. They start Pending for admin review.",
            "Supplier categories: " + string.Join("; ", ProcurementCategoryInput.Options),
            "Review the chat preview, then confirm creation. If any row is invalid, no accounts are created.",
            "Existing emails are never overwritten. Passwords are hashed by Identity and are not sent to AI."
        ];
        for (var n = 0; n < help.Length; n++) instructions.Cell(n + 1, 1).Value = help[n];
        instructions.Column(1).Width = 110; instructions.Column(1).Style.Alignment.WrapText = true;
        instructions.Rows(1, help.Length).Height = 42;
        using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }

    public static List<AccountImportRow> Parse(Stream stream, string defaultRole)
    {
        // Bound expanded XLSX size as well as compressed upload size before opening it.
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 20 * 1024 * 1024)
                throw new ValidationException("Workbook is too large when expanded. Use the account template (maximum 100 rows).");
        stream.Position = 0;
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.FirstOrDefault() ?? throw new ValidationException("The workbook has no sheet.");
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (last > 101) throw new ValidationException("Import at most 100 account rows at a time.");
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in sheet.Row(1).CellsUsed())
            if (!headers.TryAdd(cell.GetString().Trim(), cell.Address.ColumnNumber))
                throw new ValidationException("Duplicate column headers are not allowed.");
        if (!headers.ContainsKey("Email") || !headers.ContainsKey("Password"))
            throw new ValidationException("The first row must contain Email and Password columns. Download the template.");
        var result = new List<AccountImportRow>();
        for (var n = 2; n <= last; n++)
        {
            string Read(string name) {
                if (!headers.TryGetValue(name, out var column)) return "";
                var cell = sheet.Cell(n, column);
                if (cell.HasFormula) throw new ValidationException($"Row {n}: formulas are not supported. Paste plain values.");
                var value = cell.GetString();
                if (value.Length > 500) throw new ValidationException($"Row {n}: a cell exceeds 500 characters.");
                return name == "Password" ? value : value.Trim();
            }
            var email = Read("Email"); var password = Read("Password");
            if (email.Length == 0 && password.Length == 0 && sheet.Row(n).IsEmpty()) continue;
            var role = Read("Role"); role = role.Length == 0 ? defaultRole : role;
            role = RoleNames.Primary.FirstOrDefault(r => r.Equals(role, StringComparison.OrdinalIgnoreCase)) ?? role;
            var department = Read("DepartmentId");
            var name = Read("FullName");
            result.Add(new(n, email, name.Length == 0 ? email.Split('@')[0] : name, role,
                department.Length == 0 ? null : int.TryParse(department, out var id) ? id : -1,
                Null(Read("CommitteeRole")), Read("CompanyName"), Read("Category"), Read("RegistrationNumber"), password));
        }
        if (result.Count == 0) throw new ValidationException("The workbook contains no accounts.");
        return result;
    }
    private static string? Null(string value) => value.Length == 0 ? null : value;

    public async Task<AccountImportResult> ProcessAsync(List<AccountImportRow> rows, bool create, CancellationToken ct)
    {
        // Serialize competing imports; revalidate inside the same transaction used to create users and roles.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var previews = new List<AccountImportPreview>();
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seats = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var errors = new List<string>();
            if (row.Email.Length > 256 || !new EmailAddressAttribute().IsValid(row.Email)) errors.Add("Enter a valid email address.");
            if (!emails.Add(row.Email)) errors.Add("Email is duplicated in this workbook.");
            var alreadyExists = await users.FindByEmailAsync(row.Email) != null || await users.FindByNameAsync(row.Email) != null;
            if (alreadyExists) errors.Add("An account with this email already exists.");
            if (row.FullName.Length is 0 or > 150) errors.Add("FullName must be 1–150 characters.");
            if (!RoleNames.Primary.Contains(row.Role)) errors.Add("Choose a valid primary Role.");
            if (!await db.Roles.AnyAsync(r => r.Name == row.Role, ct)) errors.Add("The requested role is not configured.");
            if (row.Role == RoleNames.Department && !await db.Departments.AnyAsync(d => d.DepartmentId == row.DepartmentId && d.IsActive, ct)) errors.Add("Department accounts require an active DepartmentId.");
            var validSeats = row.Role == RoleNames.TopCommittee ? RoleNames.TopPositions : RoleNames.EvaluationPositions;
            if (row.Role is RoleNames.TopCommittee or RoleNames.EvaluationCommittee && row.CommitteeRole == null) errors.Add("Committee accounts require CommitteeRole.");
            if (row.CommitteeRole != null)
            {
                if (row.Role is not (RoleNames.TopCommittee or RoleNames.EvaluationCommittee or RoleNames.SourcingManager) || !validSeats.Contains(row.CommitteeRole)) errors.Add("CommitteeRole does not match Role.");
                else if (!seats.Add(row.CommitteeRole) || await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id where u.IsActive && r.Name == row.CommitteeRole select u.Id).AnyAsync(ct)) errors.Add("This committee position already has an active member.");
            }
            if (row.Role == RoleNames.Supplier)
            {
                if (row.CompanyName.Length is 0 or > 200 || row.RegistrationNumber.Length is 0 or > 100) errors.Add("Supplier accounts require CompanyName and RegistrationNumber.");
                if (!ProcurementCategoryInput.Options.Contains(row.Category)) errors.Add("Supplier Category must match a supported procurement category.");
            }
            var candidate = new ApplicationUser { UserName = row.Email, Email = row.Email };
            foreach (var validator in users.PasswordValidators)
            {
                var checkedPassword = await validator.ValidateAsync(users, candidate, row.Password);
                errors.AddRange(checkedPassword.Errors.Select(e => e.Description));
            }
            previews.Add(new(row.Row, row.Email, row.FullName, row.Role, errors.Distinct().ToArray(), alreadyExists));
        }
        if (!create || previews.Any(p => p.Errors.Length > 0)) return new(previews.ToArray(), 0);
        foreach (var row in rows)
        {
            var account = new ApplicationUser { UserName = row.Email, Email = row.Email, FullName = row.FullName,
                EmailConfirmed = true, IsActive = true, DepartmentId = row.Role == RoleNames.Department ? row.DepartmentId : null };
            var created = await users.CreateAsync(account, row.Password);
            if (!created.Succeeded) throw new ValidationException($"Row {row.Row}: account creation failed. No accounts were saved. " + string.Join(" ", created.Errors.Select(e => e.Description)));
            var roles = new List<string> { row.Role };
            if (row.CommitteeRole != null) { roles.Add(row.CommitteeRole); if (RoleNames.EvaluationPositions.Contains(row.CommitteeRole)) roles.Add(RoleNames.EvaluationCommittee); }
            var assigned = await users.AddToRolesAsync(account, roles.Distinct());
            if (!assigned.Succeeded) throw new ValidationException($"Row {row.Row}: role assignment failed. No accounts were saved.");
            if (row.Role == RoleNames.Supplier)
                db.SupplierProfiles.Add(new SupplierProfile { UserId = account.Id, CompanyName = row.CompanyName, ContactName = row.FullName,
                    RegistrationNumber = row.RegistrationNumber, Category = row.Category, Status = "Pending", CreatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(previews.ToArray(), rows.Count);
    }
}
