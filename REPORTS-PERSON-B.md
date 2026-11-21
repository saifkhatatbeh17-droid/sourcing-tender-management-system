# Reports work — Person B: suppliers, bids and evaluation, approvals, audit log, contracts, Top Committee stats

You build requirements **#3 Supplier reports**, **#4 Bids & evaluation reports**, **#5 Approvals & decisions reports** (including the **audit log** and **final contracts**), and the missing parts of **#6 Top Committee statistical reports**.
Person A builds the shared reports hub, access control, exports, purchase request and tender reports, and the Admin dashboard drill-down (see `REPORTS-PERSON-A.md`). Your reports plug into A's hub through the contract in section 2, so you never build page layout, filters or PDF/Excel export for them — A's hub does that from your `ReportResult`.

Project: TIRYAQ — ASP.NET Core MVC (.NET 10), EF Core code-first, SQL Server LocalDB, Razor views. Read `AGENTS.md` and `PROJECT-STATE.md` before starting.

---

## 1. The idea in one paragraph

We add **one Reports area**: a hub page with one card per report, and every report page uses the same layout — filter bar with PDF/Excel buttons, a KPI strip, one chart, one table, with drill-down from KPIs and chart bars into the table. The Admin sees every report with all data; other roles see only allowed reports and only their own data.

**One place for reports (owner's decision).** The hub is the only reporting area. Existing statistical pages are folded into hub reports, not kept beside them: the Admin "Supplier activity report" becomes part of your `suppliers` report, the Top Committee "Reports" page becomes your `top-committee` report, and the Sourcing Manager "Reports & decisions" page becomes your `approvals` report (manager scope). Person A removes the old menu items and redirects the old URLs once your reports are merged. Your part supplies the data and the missing features behind four of those reports: supplier status and document checks, bid comparison and exclusions, an approvals view with a real audit trail, final contracts for awarded tenders, and richer supplier statistics for the Top Committee.

---

## 2. Shared contract (identical in both briefs — do not change without telling Person A)

Person A commits `Services/Reports/Hub/ReportContracts.cs` first. Pull it from A's `reports-hub` branch. If it is not there yet, create the file yourself with exactly this content (it will merge cleanly):

```csharp
namespace SourcingTenderManagement.Services.Reports.Hub;

public sealed record ReportQuery(
    DateOnly? From = null, DateOnly? To = null,
    int? DepartmentId = null, int? SupplierId = null, int? TenderId = null,
    string? Status = null, string? Search = null,
    string? Drill = null,          // e.g. "status:Approved", "department:3"; set by KPI/chart clicks
    int Page = 1, bool ForExport = false);

public sealed record ReportScope(string UserId, string Role, int? DepartmentId, bool IsAdmin);

public sealed record ReportKpi(string Label, string Value, string? Drill = null, string? Hint = null);
public sealed record ReportChartPoint(string Label, decimal Value, string? Drill = null);
public sealed record ReportChart(string Title, string Kind, IReadOnlyList<ReportChartPoint> Points); // Kind: "bar" | "donut"
public sealed record ReportRow(IReadOnlyList<string> Cells, string? Link = null);
public sealed record ReportTable(IReadOnlyList<string> Columns, IReadOnlyList<ReportRow> Rows, int Total);
public sealed record ReportResult(IReadOnlyList<ReportKpi> Kpis, ReportChart? Chart, ReportTable Table, IReadOnlyList<string> Notes);

public interface IReport
{
    string Key { get; }                         // url slug, e.g. "suppliers"
    string Title { get; }
    string Description { get; }                 // one line for the hub card
    string Icon { get; }                        // icon name used by the hub card
    IReadOnlyList<string> DefaultRoles { get; } // roles allowed when the Admin has not changed access; Admin is always allowed
    IReadOnlyList<string> Filters { get; }      // which filter controls to show: "date","department","supplier","tender","status","search"
    IReadOnlyList<string> StatusOptions { get; }
    Task<ReportResult> RunAsync(ReportQuery query, ReportScope scope, CancellationToken ct);
}
```

Rules every report follows:
- The report itself applies `scope` (role/department/user). The hub never shows data your report did not filter.
- Page size 25. When `query.ForExport` is true, return all rows (cap 5,000, add a note if capped).
- Dates UTC, shown `dd MMM yyyy`. Money `N2` + currency code.
- Register with `builder.Services.AddScoped<IReport, YourReport>();` in `Program.cs`.
- Until A's hub is merged, test your reports by calling `RunAsync` from your checks (section 4.7).

---

## 3. Your tasks

Work on branch `reports-data`, created from `ghaith-changes`. Put your report classes in `Services/Reports/Data/`.

### 3.1 Supplier status: add "Suspended" (#3)
- Today supplier statuses are `Pending`, `Approved`, `Rejected` (`SupplierProfile.Status`). Add `Suspended`.
- Admin actions in the supplier details popup: **Suspend** (reason required, max 1,000 chars) and **Reactivate**. Store the reason in `ReviewNotes` with reviewer and time (fields exist: `ReviewedByUserId`, `ReviewedAt`).
- **A suspended supplier must not be invited and must not submit or edit offers.** Find every eligibility check that uses `Status == "Approved"` (currently in `AccountController`, `AdminController`, `ClarificationController`, `SourcingManagerController`, `SourcingOfficerController`, `Services/Chat/AiChatContext.cs`, `AiChatExportService.cs`, `AiChatWorkspaceActions.cs`, `Services/Intelligence/AiIntelligenceService.cs`, `Services/FaceEnrollment/FaceSetupFlow.cs`) and confirm each one still does the right thing. Decide for existing open offers: keep them visible to the committee, block new changes, and show a "Supplier suspended" badge.
- Suspended suppliers can still sign in and read their history.

### 3.2 Supplier document verification and expiry (#3)
- `SupplierDocument` already has `ExpiresOn`. Add `VerificationStatus` (`Pending` default, `Verified`, `Rejected`), `VerifiedByUserId`, `VerifiedAt`, `VerificationNote`.
- Admin can verify/reject each document in the supplier details popup (`Views/Admin/_SupplierDetailsContent.cshtml`), note required on reject.
- Expiry states: Expired (`ExpiresOn < today`), Expiring soon (within `Reports:DocumentExpiryWarningDays`, default 30, from configuration), Valid, No expiry date.

### 3.3 Report #3 — Suppliers (`Key = "suppliers"`)
- KPIs: registered (all), approved, pending, suspended, rejected (each drillable `status:...`), documents expired, documents expiring soon, documents awaiting verification.
- Chart: suppliers by status.
- Table: supplier, category, status, documents (verified / total), next expiry date + state, invited, submitted, compliant, awarded, win rate (awarded ÷ submitted), average response time (invitation `SentAt` → bid `SubmittedAt`, days).
- This report **replaces** the Admin "Supplier activity report". Move its query and outcome rules out of `Controllers/AdminController.SupplierActivity.cs` into a service used by this report, and keep the per-supplier history PDF (`ReportPdfExport.SupplierActivity`) as a row action (row `Link` to `/Admin/SupplierActivity?supplierId={id}`, which keeps working). Add a drill `supplier:{id}` that lists that supplier's tender history in the table, so the old page's content is fully covered.
- Default roles: Admin, SourcingManager, TopCommittee. Only Admin sees document details; others see counts only.

### 3.4 Report #4 — Bids & evaluation (`Key = "bids-evaluation"`)
- Filter by tender (`TenderId`), date, supplier, status.
- **Price comparison** (when a tender is chosen): one row per offer — supplier, item subtotal, tax, extra costs, total (use `CommitteeEvaluationPage.Total` logic), difference from lowest, delivery days, compliance.
- **Evaluation summary**: per offer, average financial and technical marks and overall average from `Evaluations` (four members), the committee's recommended offer (`EvaluationReport.RecommendedBidId`), and the agreed weights (`Tender.CommitteeFinancialWeight`). Link to the existing unified report PDF for that tender instead of rebuilding it.
- **Excluded offers** (all tenders, or the chosen one): bids with `ComplianceStatus = "NonCompliant"`, `Status` `Rejected` or `Withdrawn`, with the reason (`ComplianceNotes`), who reviewed (`ComplianceReviewedByUserId`) and when.
- KPIs: offers received, compliant, excluded (drillable), average offers per tender, average spread between lowest and highest offer.
- Default roles: Admin, SourcingOfficer, EvaluationCommittee (only its own tenders — use `InEvaluationWorkspace` from `Services/ProcurementVisibilityQueries.cs`), SourcingManager, TopCommittee.
- Supplier prices are confidential: never allow the Department role.

### 3.5 Audit log (#5)
- New append-only entity `AuditEntry { AuditEntryId (long), OccurredAt, UserId?, UserName?, Role?, Action, EntityType, EntityId, TenderId?, Summary (nvarchar 500), ChangesJson (nvarchar(max), nullable), IpAddress? }`. Indexes on `OccurredAt`, `(EntityType, EntityId)`, `TenderId`, `UserId`.
- Capture changes with an EF Core **`SaveChangesInterceptor`** registered on `ApplicationDbContext`, for these entities: `PurchaseRequest`, `Tender`, `Invitation`, `Bid`, `EvaluationReport`, `Evaluation`, `TenderApproval`, `CommitteeWeightProposal`, `CommitteeTieVote`, `CommitteeAssignment`, `Clarification`, `SupplierProfile`, `SupplierDocument`, `Department`, and `ApplicationUser` (only `IsActive`, `DepartmentId`, role changes).
  - Record Added / Modified (changed properties old → new) / Deleted. Write audit rows in the same `SaveChanges` call.
  - **Never log secrets or bulky data:** exclude `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`, face credential data, tokens, file contents. Truncate long text to 500 chars.
  - Take the user from `IHttpContextAccessor` (already registered); background jobs log `UserName = "System"`.
- **Interceptors do not see bulk SQL.** These files use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` / `ExecuteSql…`: `AccountController`, `AdminController`, `ClarificationController`, `EvaluationCommitteeController`, `SourcingOfficerController`, `SourcingOfficerController.OfferExtensions`, `SupplierController`, `Services/TenderClosureService.cs`, `Services/Chat/AiChatWorkspaceActions.cs`, `Services/TopCommittee/TopCommitteeNotifications.cs`. Review each call; where it changes a business record (not just a row lock or a notification read flag), write an entry explicitly through a small `IAuditLog.WriteAsync(...)` service.
- Also log sign-in success/failure and Admin user changes (create, edit, password reset, import) as `Action = "Account.*"`.
- No edit or delete endpoints for audit entries.

### 3.6 Report #5 — Approvals & decisions (`Key = "approvals"`) and Audit log (`Key = "audit-log"`)
- **approvals** — one row per tender that reached evaluation: committee recommendation (recommended supplier, report status), committee approvals count, officer summary submitted, manager decision (`EvaluationReport.Status` `ManagerApproved` / `ManagerRejected`, manager summary), Top Committee votes (approve/reject counts from `TenderApprovals` with `Stage = "Top"`), final outcome, award date, contract (link if one exists, see 3.7). KPIs drillable by outcome. Default roles: Admin, SourcingManager, TopCommittee.
  - This report **replaces** the Sourcing Manager "Reports & decisions" page (`SourcingManagerController.Reports` / `ReportsPdf`, model `ManagerReportPage`). For the Sourcing Manager scope, add the columns that page shows (the manager's own decision, comments, date) and support its filters (title search, decision). Reuse its query instead of rewriting it, so the manager sees the same records.
- **audit-log** — Admin only (DefaultRoles = none besides Admin). Filters: date, user (search), tender, action/entity (status filter). Table: time, user, role, action, entity, record, summary; row expands or links to show `ChangesJson`.

### 3.7 Final contracts for awarded tenders (#5)
- New entity `TenderContract { TenderContractId, TenderId (unique), BidId, GeneratedAt, GeneratedByUserId, SignedFileName?, SignedStoredFileName?, SignedContentType?, SignedSizeBytes?, SignedUploadedByUserId?, SignedUploadedAt?, Notes? }`.
- **Generate** (Admin or Sourcing Manager, tender status `Awarded` only): a PDF award contract built from the tender, purchase request items, winning bid items, quantities, unit prices, tax, extra costs, total, delivery days, payment terms, validity, and parties (Tiryaq / supplier company, registration and tax numbers). Use PDFsharp in the style of `Services/Reports/ReportPdfExport.cs`. Regenerating replaces the generated PDF but keeps any signed copy.
- **Upload signed copy**: PDF only, max 10 MB, stored under `App_Data/private-files/contracts/` with a random stored name, like supplier and offer documents (see `AccountController` and `SupplierController` upload code). Download only for Admin, Sourcing Manager, Top Committee and the winning supplier.
- Show "Contract" on the tender's award area for those roles and in the approvals report.
- Out of scope: bank/legal guarantee documents and award emails (separate requirement).

### 3.8 #6 Top Committee statistics — move into the hub, add dealings and frequency
- Build hub report `top-committee` (`IReport`), default roles: TopCommittee and Admin. It **replaces** the page at `/TopCommittee/Reports` and its export: wrap `TopCommitteeReports.GetAsync(...)` (keep its access rules from `TopCommitteeAccessService`) and map its summary, tender rows and supplier rows into `ReportResult`. Map the existing filters (title, status, "my decision", created from/to) onto `ReportQuery` (`Search`, `Status`, `Drill = "decision:..."`, `From`/`To`).
- The hub's generic export produces the PDF/Excel. If the Top Committee needs the existing multi-sheet Excel layout (Summary, Tenders, Supplier Statistics, Decision Summary), tell Person A — the hub can let a report supply its own export; do not keep a second export button elsewhere.
- `Services/TopCommittee/TopCommitteeReports.cs` and `TopCommitteeReportExport.cs` currently give per supplier only "Awarded tenders".
- Add per supplier: **invited** (invitations), **dealings** (tenders where the supplier submitted an offer), **awarded**, **win rate**, **dealing frequency** = dealings ÷ months in the filter range (or since the supplier's first dealing when no range), shown with one decimal as "per month".
- Add registered supplier count by status to the Summary sheet/PDF.
- Update the screen, the Excel "Supplier Statistics" sheet and the PDF. Keep the existing filters and Top Committee access rules untouched.

### 3.9 Migrations
One migration per feature is fine: `AddSupplierSuspensionAndDocumentVerification`, `AddAuditLog`, `AddTenderContracts`. If Person A's migration is merged first, remove yours and re-add them on top so the model snapshot stays clean.

---

## 4. Tests — `Checks/ReportsDataChecks.cs`

Use a throwaway database (`TiryaqReportsDataQA_<random>`), create data only there, drop it afterwards. Cover:
1. Suspended supplier: cannot be invited, cannot submit or edit an offer; reactivated supplier can.
2. Document expiry states with a fixed "today" (expired, expiring in 30 days, valid, no date).
3. Supplier report numbers equal the Supplier activity report for the same data.
4. Excluded offers list includes NonCompliant/Rejected/Withdrawn with reasons, and the Department role cannot open bids reports.
5. Audit interceptor: Added/Modified/Deleted rows written, secrets never present in `ChangesJson`, user taken from the HTTP context.
6. At least two former bulk-update paths now write an audit entry.
7. Contract: cannot generate before `Awarded`; non-PDF and > 10 MB uploads rejected; download denied for an unrelated supplier.
8. Top Committee dealing frequency for a known data set and range.
9. Each `IReport.RunAsync` respects `scope` (a role outside `DefaultRoles` is not your concern — the hub blocks it — but scoped roles must only get their data).

Run existing checks too (`dotnet run --no-build -- --vllm-ai-checks`, the harnesses in `Checks/`, and the Top Committee checks) — the Top Committee report change and the suspension change touch shared code.

---

## 5. Files you own (Person A will not edit these)
`Services/Reports/Data/*`, audit log service + interceptor, `Models/Entities/AuditEntry.cs`, `TenderContract.cs`, changes to `SupplierProfile`/`SupplierDocument`, their configurations and migrations, `Services/TopCommittee/TopCommitteeReports.cs`, `TopCommitteeReportExport.cs`, `Views/Admin/_SupplierDetailsContent.cshtml`, supplier-eligibility checks listed in 3.1, contract controller actions/views.

Shared files: `Program.cs` and `Data/ApplicationDbContext.cs` — add lines only; expect a trivial merge with A.

## 6. Working rules
- Run the app on https://localhost:7130 (`dotnet run --launch-profile https`). The running app locks `bin`: stop it before `dotnet build`, restart after, confirm it returns 200.
- Build must have 0 errors and 0 warnings.
- Do not commit `.config/`, `appsettings.Local.json`, `Data/Seed/`, uploaded files under `App_Data/`, screenshots or test output. Commit to `reports-data` only; open a PR into `ghaith-changes` when done. Do not push to `ghaith-changes`, `develop` or `main` directly.
- Test accounts: ask the project owner for the shared password; don't put it in files.

## 7. Done means
- Suspension, document verification, audit log, contracts and Top Committee additions working; reports `suppliers`, `bids-evaluation`, `approvals`, `audit-log`, `top-committee` return correct, scoped data.
- Everything the old Supplier activity report, Top Committee Reports page and Manager "Reports & decisions" page showed is available in your hub reports, so Person A can redirect those pages without losing anything.
- `ReportsDataChecks` pass; existing checks still pass.
- After A's hub is merged: all four reports open from the hub, filter, drill and export.
- A short PR note: what was verified, what was not, and every eligibility check you changed for suspension.
