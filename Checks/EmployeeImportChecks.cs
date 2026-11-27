using ClosedXML.Excel;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Services.Admin;
using SourcingTenderManagement.Services.Chat;

namespace SourcingTenderManagement.Checks;

/// <summary>
/// Checks for the Excel account import that need no database: sheet reading, role and position
/// matching, temporary passwords, the one-time credentials download and the forced-change filter.
/// Run with <c>dotnet run -- --account-import-checks</c>.
/// </summary>
public static class EmployeeImportChecks
{
    public static void Run()
    {
        // 1. English and Arabic headings are recognised; blank rows are skipped; a Password column is ignored with a notice.
        var rows = EmployeeImport.ReadSheet(Workbook(
            ["الاسم الكامل", "البريد الإلكتروني", "الدور", "القسم", "Password"],
            ["Rania Haddad", "rania@example.invalid", "Sourcing Officer", "", "Secret123!"],
            ["", "", "", "", ""],
            ["Omar Nasser", "omar@example.invalid", "Department", "Pharmacy", ""]), out var notices);
        Require(rows.Count == 2 && rows[0].Name == "Rania Haddad" && rows[1].Department == "Pharmacy" && rows[1].RowNumber == 4,
            "Rows must be read from Arabic headings with blank rows skipped and sheet row numbers kept.");
        Require(notices.Count == 1
            && notices[0].Contains("Password column was ignored"), "A Password column must be ignored with a notice.");
        Require(typeof(EmployeeImport.SheetRow).GetProperties().All(p => !p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase))
                && typeof(EmployeeImportRow).GetProperties().All(p => !p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)),
            "A parsed or previewed row must have no password field, so sheet passwords can never be carried forward.");

        // 2. Files that cannot be imported are rejected with a clear reason.
        ExpectFileError(() => EmployeeImport.ReadSheet(Workbook(["Name", "Email"], ["A", "a@example.invalid"]), out _), "Role");
        ExpectFileError(() => EmployeeImport.ReadSheet(Workbook(["Full name", "Email", "Role"]), out _), "no account rows");
        ExpectFileError(() => EmployeeImport.ReadSheet(new MemoryStream("not a workbook"u8.ToArray()), out _), ".xlsx");
        var tooMany = Enumerable.Range(1, EmployeeImport.MaxRows + 1)
            .Select(i => new[] { $"Person {i}", $"p{i}@example.invalid", "Sourcing Officer" }).ToArray();
        ExpectFileError(() => EmployeeImport.ReadSheet(Workbook(["Full name", "Email", "Role"], tooMany), out _), $"more than {EmployeeImport.MaxRows}");

        // 3. Roles match by code or display name; Admin and Supplier are refused on purpose.
        Require(EmployeeImport.MatchRole("SourcingOfficer", out _) == RoleNames.SourcingOfficer
                && EmployeeImport.MatchRole("sourcing officer", out _) == RoleNames.SourcingOfficer
                && EmployeeImport.MatchRole("CEO & Chiefs", out _) == RoleNames.TopCommittee
                && EmployeeImport.MatchRole("ceo and chiefs", out _) == RoleNames.TopCommittee
                && EmployeeImport.MatchRole("Evaluation Committee", out _) == RoleNames.EvaluationCommittee,
            "Roles must match by code or display name, ignoring case and spacing.");
        Require(EmployeeImport.MatchRole("Admin", out var adminProblem) == null && adminProblem!.Contains("Users page"),
            "Admin accounts must not be importable.");
        Require(EmployeeImport.MatchRole("Supplier", out var supplierProblem) == null && supplierProblem!.Contains("register"),
            "Supplier accounts must not be importable.");
        Require(EmployeeImport.MatchRole("Wizard", out var unknown) == null && unknown!.Contains("not recognised"),
            "An unknown role must be reported.");
        Require(!EmployeeAccounts.ImportRoles.Contains(RoleNames.Admin) && !EmployeeAccounts.ImportRoles.Contains(RoleNames.Supplier)
                && EmployeeAccounts.FormRoles.Contains(RoleNames.Admin),
            "The Users form may create admins; the import may not.");
        Require(EmployeeImport.MatchPosition("CEO & Chiefs member 2") == RoleNames.TopCommitteeMember2
                && EmployeeImport.MatchPosition("EvaluationFinancial") == RoleNames.EvaluationFinancial
                && EmployeeImport.MatchPosition("Chairman") == null,
            "Committee positions must match by code or display name.");

        // 4. Temporary passwords meet the Identity policy and are not repeated.
        var passwords = Enumerable.Range(0, 200).Select(_ => EmployeeAccounts.GenerateTemporaryPassword()).ToList();
        Require(passwords.All(p => p.Length == 16 && p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit)
                                   && p.Any(c => !char.IsLetterOrDigit(c)) && !p.Any(c => "0O1lI".Contains(c)))
                && passwords.Distinct().Count() == passwords.Count,
            "Temporary passwords must meet the policy, avoid ambiguous characters and be unique.");

        // 5. The credentials file can be downloaded once, only by the admin who created it.
        var store = new AiChatDownloadStore();
        var link = store.Save("admin-1", RoleNames.Admin, "new.xlsx", "application/octet-stream", "Passwords", "excel", [1, 2, 3], oneTime: true);
        var tokenValue = link.Url.Split('/').Last();
        Require(store.TryGet(tokenValue, "admin-2") == null, "Another user must not fetch the credentials file.");
        Require(store.TryGet(tokenValue, "admin-1") != null, "The admin who created the file must be able to fetch it once.");
        Require(store.TryGet(tokenValue, "admin-1") == null, "The credentials file must not be downloadable a second time.");
        var regular = store.Save("admin-1", RoleNames.Admin, "r.pdf", "application/pdf", "Report", "pdf", [1]);
        var regularToken = regular.Url.Split('/').Last();
        Require(store.TryGet(regularToken, "admin-1") != null && store.TryGet(regularToken, "admin-1") != null,
            "Ordinary Safi exports must stay downloadable more than once.");

        // 6. Until the password is changed, only the account security pages are reachable.
        Require(PasswordChangeRequiredFilter.IsAllowed("Account", "Profile") && PasswordChangeRequiredFilter.IsAllowed("Account", "ChangePassword")
                && PasswordChangeRequiredFilter.IsAllowed("Account", "Logout")
                && !PasswordChangeRequiredFilter.IsAllowed("Account", "GoToDashboard")
                && !PasswordChangeRequiredFilter.IsAllowed("SourcingOfficer", "Dashboard")
                && !PasswordChangeRequiredFilter.IsAllowed("AiChat", "Ask"),
            "Only Profile, ChangePassword, Logout, AccessDenied and Login may be reached with a temporary password.");

        Console.WriteLine("PASS: account import reads English/Arabic sheets, ignores password columns, rejects bad files, " +
                "refuses Admin/Supplier rows, matches roles and positions, generates policy-compliant one-time " +
                "credentials and locks accounts until the temporary password is changed.");
    }

    private static MemoryStream Workbook(string[] headers, params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Accounts");
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = rows[r][c];
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void ExpectFileError(Action action, string fragment)
    {
        try
        {
            action();
        }
        catch (EmployeeImportFileException ex) when (ex.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new Exception($"Account import check failed: expected a file error mentioning “{fragment}”.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Account import check failed: " + message);
    }
}
