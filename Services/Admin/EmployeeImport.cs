using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Services.Admin;

// Reads an Excel sheet of staff, previews the rows with any problems, and creates the accounts.
public sealed record EmployeeImportRow(
    int RowNumber,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Role,
    string? CommitteeRole,
    int? DepartmentId,
    string RoleDisplay);

public sealed record EmployeeImportProblem(int RowNumber, string Name, string Email, IReadOnlyList<string> Problems);

public sealed record EmployeeImportPreview(
    string? Token,
    int ReadyCount,
    IReadOnlyList<EmployeeImportRowSummary> Ready,
    IReadOnlyList<EmployeeImportProblem> Problems,
    IReadOnlyList<string> Notices);

public sealed record EmployeeImportRowSummary(int RowNumber, string Name, string Email, string Role);

public sealed record EmployeeImportResult(
    int CreatedCount,
    IReadOnlyList<EmployeeImportProblem> Failed,
    AiChatDownloadLink? Credentials);

/// <summary>Thrown for a file that cannot be read as an account list at all (wrong format, missing columns, too many rows).</summary>
public sealed class EmployeeImportFileException(string message) : Exception(message);

/// <summary>
/// Validated import rows waiting for the admin's confirmation. Kept on the server so a confirmation
/// can never create accounts the preview did not show. Each preview is single use and expires.
/// </summary>
public sealed class EmployeeImportPreviewStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (string AdminId, IReadOnlyList<EmployeeImportRow> Rows, DateTime CreatedUtc)> previews = new();

    public string Save(string adminId, IReadOnlyList<EmployeeImportRow> rows)
    {
        foreach (var expired in previews.Where(p => DateTime.UtcNow - p.Value.CreatedUtc > Lifetime)
            .Select(p => p.Key)
            .ToList())
            previews.TryRemove(expired, out _);
        var token = Guid.NewGuid().ToString("N");
        previews[token] = (adminId, rows, DateTime.UtcNow);
        return token;
    }

    /// <summary>Returns the rows once, only to the admin who created the preview.</summary>
    public IReadOnlyList<EmployeeImportRow>? Take(string token, string adminId)
    {
        if (string.IsNullOrWhiteSpace(token) || !previews.TryGetValue(token, out var entry)) return null;
        if (!string.Equals(entry.AdminId, adminId, StringComparison.Ordinal)) return null;
        if (!previews.TryRemove(token, out entry)) return null;
        return DateTime.UtcNow - entry.CreatedUtc > Lifetime ? null : entry.Rows;
    }
}

/// <summary>
/// Creates employee accounts from an Excel sheet. No AI is involved: the file is read and checked on
/// the server, the admin confirms a preview, and Tiryaq generates a temporary password for each account.
/// Passwords in the file are never read.
/// </summary>
public sealed partial class EmployeeImport(
    ApplicationDbContext db,
    EmployeeAccounts accounts,
    EmployeeImportPreviewStore previews,
    AiChatDownloadStore downloads)
{
    public const int MaxRows = 200;
    public const long MaxFileBytes = 1024 * 1024;

    private enum Column { Name, Email, Role, Department, Position, Phone, Password }

    // Header names are matched after lower-casing and removing spaces, dashes and underscores.
    private static readonly Dictionary<string, Column> HeaderNames = new(StringComparer.Ordinal)
    {
        ["fullname"] = Column.Name, ["name"] = Column.Name, ["الاسم"] = Column.Name, ["الاسمالكامل"] = Column.Name,
        ["email"] = Column.Email, ["emailaddress"] = Column.Email, ["البريدالإلكتروني"] = Column.Email,
        ["البريدالالكتروني"] = Column.Email, ["الإيميل"] = Column.Email, ["الايميل"] = Column.Email,
        ["role"] = Column.Role, ["الدور"] = Column.Role,
        ["department"] = Column.Department, ["القسم"] = Column.Department,
        ["committeeposition"] = Column.Position, ["position"] = Column.Position, ["المنصب"] = Column.Position, ["منصباللجنة"] = Column.Position,
        ["phone"] = Column.Phone, ["phonenumber"] = Column.Phone, ["الهاتف"] = Column.Phone, ["رقمالهاتف"] = Column.Phone,
        ["password"] = Column.Password, ["كلمةالمرور"] = Column.Password, ["كلمةالسر"] = Column.Password
    };

    public static IReadOnlyList<(string Code, string Display)> ImportRoleChoices { get; } =
        EmployeeAccounts.ImportRoles.Select(r => (r, RoleNames.DisplayName(r))).ToArray();

    public static IReadOnlyList<(string Code, string Display)> PositionChoices { get; } =
        RoleNames.EvaluationPositions.Concat(RoleNames.TopPositions)
            .Select(p => (p, RoleNames.DisplayName(p)))
            .ToArray();

    public async Task<EmployeeImportPreview> PreviewAsync(Stream file, string adminId, CancellationToken ct = default)
    {
        var sheet = ReadSheet(file, out var notices);
        var departments = await db.Departments.AsNoTracking()
            .Select(d => new { d.DepartmentId, d.Name, d.IsActive }).ToListAsync(ct);
        var existingEmails = (await db.Users.AsNoTracking().Select(u => u.NormalizedEmail).ToListAsync(ct))
            .Where(e => e != null).ToHashSet(StringComparer.Ordinal);

        var ready = new List<EmployeeImportRow>();
        var problems = new List<EmployeeImportProblem>();
        var emailsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positionsInFile = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in sheet)
        {
            var issues = new List<string>();
            var name = row.Name.Trim();
            var email = row.Email.Trim();
            var phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim();

            if (name.Length == 0) issues.Add("Full name is missing.");
            else if (name.Length > 150) issues.Add("Full name is longer than 150 characters.");

            if (email.Length == 0) issues.Add("Email is missing.");
            else if (email.Length > 256
                || !new EmailAddressAttribute().IsValid(email)) issues.Add("Email is not a valid address.");
            else if (!emailsInFile.Add(email)) issues.Add("This email appears more than once in the file.");
            else if (existingEmails.Contains(email.ToUpperInvariant())) issues.Add("An account with this email already exists.");

            if (phone != null && (phone.Length > 32 || !new PhoneAttribute().IsValid(phone)))
                issues.Add("Phone number is not valid.");

            var role = MatchRole(row.Role, out var roleProblem);
            if (roleProblem != null) issues.Add(roleProblem);

            int? departmentId = null;
            if (!string.IsNullOrWhiteSpace(row.Department))
            {
                var match = departments.FirstOrDefault(d => string.Equals(d.Name.Trim(), row.Department
                    .Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null) issues.Add($"Department “{row.Department.Trim()}” does not exist.");
                else departmentId = match.DepartmentId;
            }

            string? position = null;
            if (!string.IsNullOrWhiteSpace(row.Position))
            {
                position = MatchPosition(row.Position);
                if (position == null) issues.Add($"Committee position “{row.Position.Trim()}” is not recognised.");
            }

            if (role != null)
            {
                var input = new EmployeeAccountInput(name, email, phone, role, position, departmentId);
                foreach (var error in await accounts.ValidateAsync(input, EmployeeAccounts.ImportRoles, ct))
                {
                    // An unknown department name was already reported above.
                    if (error.Field == "DepartmentId"
                        && !string.IsNullOrWhiteSpace(row.Department)
                        && departmentId == null) continue;
                    issues.Add(error.Field == "DepartmentId" ? "Department accounts need an active department." : error.Message);
                }
            }

            if (position != null)
            {
                if (positionsInFile.TryGetValue(position, out var firstRow))
                    issues.Add($"{RoleNames.DisplayName(position)} is also assigned on row {firstRow}. Each position has one holder.");
                else
                {
                    positionsInFile[position] = row.RowNumber;
                    if (await accounts.ActivePositionHolderAsync(position, ct) is string holder)
                        issues.Add($"{RoleNames.DisplayName(position)} is already held by {holder}. Deactivate that account first.");
                }
            }

            if (issues.Count > 0)
                problems.Add(new EmployeeImportProblem(row.RowNumber, name, email, issues));
            else
                ready.Add(new EmployeeImportRow(row.RowNumber, name, email, phone, role!, position, departmentId,
                    RoleNames.DisplayName(role!) + (position != null ? $" ({RoleNames.DisplayName(position)})" : "")));
        }

        var token = ready.Count > 0 ? previews.Save(adminId, ready) : null;
        return new EmployeeImportPreview(token, ready.Count,
            ready.Select(r => new EmployeeImportRowSummary(r.RowNumber, r.FullName, r.Email, r.RoleDisplay)).ToArray(),
            problems, notices);
    }

    public async Task<EmployeeImportResult?> ConfirmAsync(string token, string adminId, CancellationToken ct = default)
    {
        var rows = previews.Take(token, adminId);
        if (rows == null) return null;

        var created = new List<(EmployeeImportRow Row, string Password)>();
        var failed = new List<EmployeeImportProblem>();
        foreach (var row in rows)
        {
            // Re-check at creation time: another admin may have used the email or position since the preview.
            var input = new EmployeeAccountInput(row.FullName, row.Email, row.PhoneNumber, row.Role, row.CommitteeRole, row.DepartmentId);
            var errors = await accounts.ValidateAsync(input, EmployeeAccounts.ImportRoles, ct);
            if (errors.Count > 0)
            {
                failed.Add(new EmployeeImportProblem(row.RowNumber, row.FullName, row.Email, errors.Select(e => e.Message)
                    .ToArray()));
                continue;
            }

            var password = EmployeeAccounts.GenerateTemporaryPassword();
            var result = await accounts.CreateAsync(input, password, requirePasswordChange: true, ct);
            if (result.PositionHolder is string holder)
                failed.Add(new EmployeeImportProblem(row.RowNumber, row.FullName, row.Email,
                    [$"{RoleNames.DisplayName(row.CommitteeRole!)} is already held by {holder}."]));
            else if (!result.Succeeded)
                failed.Add(new EmployeeImportProblem(row.RowNumber, row.FullName, row.Email, result.Errors));
            else
                created.Add((row, password));
        }

        AiChatDownloadLink? credentials = null;
        if (created.Count > 0)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            credentials = downloads.Save(adminId, RoleNames.Admin, $"tiryaq-new-accounts-{stamp}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "New account passwords (one-time download)", "excel", CredentialsWorkbook(created), oneTime: true);
        }
        return new EmployeeImportResult(created.Count, failed, credentials);
    }

    public async Task<byte[]> TemplateAsync(CancellationToken ct = default)
    {
        var departments = await db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync(ct);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Accounts");
        string[] headers = ["Full name", "Email", "Role", "Department", "Committee position", "Phone"];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = "Example officer";
        sheet.Cell(2, 2).Value = "officer.example@example.invalid";
        sheet.Cell(2, 3).Value = RoleNames.DisplayName(RoleNames.SourcingOfficer);
        sheet.Cell(3, 1).Value = "Example department user";
        sheet.Cell(3, 2).Value = "department.example@example.invalid";
        sheet.Cell(3, 3).Value = RoleNames.DisplayName(RoleNames.Department);
        sheet.Cell(3, 4).Value = departments.FirstOrDefault() ?? "Pharmacy";
        sheet.Columns().AdjustToContents();

        var values = workbook.Worksheets.Add("Valid values");
        values.Cell(1, 1).Value = "Role";
        values.Cell(1, 2).Value = "Committee position (Evaluation Committee, CEO & Chiefs; optional for Sourcing Manager)";
        values.Cell(1, 3).Value = "Department (required for Department accounts)";
        values.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < ImportRoleChoices.Count; i++) values.Cell(i + 2, 1).Value = ImportRoleChoices[i].Display;
        for (var i = 0; i < PositionChoices.Count; i++) values.Cell(i + 2, 2).Value = PositionChoices[i].Display;
        for (var i = 0; i < departments.Count; i++) values.Cell(i + 2, 3).Value = departments[i];
        values.Cell(Math.Max(ImportRoleChoices.Count, Math.Max(PositionChoices.Count, departments.Count)) + 3, 1).Value =
            "Do not add passwords. Tiryaq creates a temporary password for each account and asks the person to change it at first sign-in.";
        values.Columns().AdjustToContents();

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    internal sealed record SheetRow(int RowNumber, string Name, string Email, string Role, string Department, string Position, string Phone);

    internal static List<SheetRow> ReadSheet(Stream file, out List<string> notices)
    {
        notices = [];
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(file);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new EmployeeImportFileException("The file could not be read as an Excel workbook. Save it as .xlsx and try again.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new EmployeeImportFileException("The workbook has no sheets.");
            var used = sheet.RangeUsed();
            if (used == null) throw new EmployeeImportFileException("The first sheet is empty.");

            var columns = new Dictionary<Column, int>();
            var headerRow = used.FirstRow().RowNumber();
            foreach (var cell in sheet.Row(headerRow).CellsUsed())
            {
                if (HeaderNames.TryGetValue(NormalizeHeader(cell.GetString()), out var column)
                    && !columns
                    .ContainsKey(column))
                    columns[column] = cell.Address.ColumnNumber;
            }

            var missing = new[] { Column.Name, Column.Email, Column.Role }.Where(c => !columns.ContainsKey(c)).ToList();
            if (missing.Count > 0)
                throw new EmployeeImportFileException(
                    $"The first row must contain the column headings Full name, Email and Role (missing: {string.Join(", ", missing.Select(c => c.ToString()))}). " +
                            $"Download the template for the expected layout.");
            if (columns.ContainsKey(Column.Password))
                notices.Add("The Password column was ignored. Tiryaq creates a temporary password for each account; delete passwords from the file.");

            string Read(IXLRow row, Column column) => columns.TryGetValue(column, out var index) ? row
                .Cell(index)
                .GetFormattedString()
                .Trim() : "";

            var rows = new List<SheetRow>();
            for (var number = headerRow + 1; number <= used.LastRow().RowNumber(); number++)
            {
                var row = sheet.Row(number);
                var values = new[] { Column.Name, Column.Email, Column.Role, Column.Department, Column.Position, Column.Phone }
                    .Select(c => Read(row, c)).ToArray();
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                if (rows.Count == MaxRows)
                    throw new EmployeeImportFileException($"The file has more than {MaxRows} accounts. Split it into smaller files.");
                rows.Add(new SheetRow(number, values[0], values[1], values[2], values[3], values[4], values[5]));
            }
            if (rows.Count == 0) throw new EmployeeImportFileException("The sheet has headings but no account rows.");
            return rows;
        }
    }

    internal static string? MatchRole(string raw, out string? problem)
    {
        problem = null;
        var key = Key(raw);
        if (key.Length == 0) { problem = "Role is missing."; return null; }
        if (key == Key(RoleNames.Admin) || key is "administrator" or "مدير" or "مسؤول")
        {
            problem = "Administrator accounts can't be imported. Create them one at a time on the Users page.";
            return null;
        }
        if (key == Key(RoleNames.Supplier) || key == "مورد")
        {
            problem = "Supplier accounts can't be imported. Suppliers register their company themselves.";
            return null;
        }
        foreach (var (code, display) in ImportRoleChoices)
            if (key == Key(code) || key == Key(display)) return code;
        if (key is "topcommittee" or "ceoandchiefs") return RoleNames.TopCommittee;
        problem = $"Role “{raw.Trim()}” is not recognised. Use one of: {string.Join(", ", ImportRoleChoices.Select(c => c.Display))}.";
        return null;
    }

    internal static string? MatchPosition(string raw)
    {
        var key = Key(raw);
        foreach (var (code, display) in PositionChoices)
            if (key == Key(code) || key == Key(display)) return code;
        return null;
    }

    private static string NormalizeHeader(string value) => NonKeyChars().Replace(value.Trim().ToLowerInvariant(), "");

    // Letters and digits only, lower case: "CEO & Chiefs" and "ceo and chiefs" both become "ceoandchiefs"/"ceochiefs".
    private static string Key(string value) =>
        NonKeyChars().Replace(value.Trim().ToLowerInvariant().Replace("&", "and"), "");

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonKeyChars();

    private static byte[] CredentialsWorkbook(IReadOnlyList<(EmployeeImportRow Row, string Password)> created)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("New accounts");
        string[] headers = ["Full name", "Email", "Role", "Temporary password"];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < created.Count; i++)
        {
            sheet.Cell(i + 2, 1).Value = created[i].Row.FullName;
            sheet.Cell(i + 2, 2).Value = created[i].Row.Email;
            sheet.Cell(i + 2, 3).Value = created[i].Row.RoleDisplay;
            sheet.Cell(i + 2, 4).Value = created[i].Password;
        }
        sheet.Cell(created.Count + 3, 1).Value =
            "Give each person only their own password. They must change it at first sign-in. Delete this file once the passwords are shared.";
        sheet.Columns().AdjustToContents();
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
