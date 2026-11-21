# Reports work — Person A: reports hub, access control, purchase requests, tenders, dashboard

You build the **shared reports hub** that every report plugs into, plus requirements **#1 Purchase request reports**, **#2 Tender reports**, **#7 Reports management** and **#8 Analytics dashboard drill-down**.
Person B builds suppliers, bids & evaluation, approvals, audit log, contracts and Top Committee additions (see `REPORTS-PERSON-B.md`). B's reports plug into your hub through the contract in section 2, so build that contract **first**.

Project: TIRYAQ — ASP.NET Core MVC (.NET 10), EF Core code-first, SQL Server LocalDB, Razor views. Read `AGENTS.md` and `PROJECT-STATE.md` before starting.

---

## 1. The idea in one paragraph

Today, report-like pages are scattered across roles and several required reports don't exist. We add **one Reports area**: a hub page with one card per report, and every report page uses the **same layout** — filter bar (date range, department, supplier, status, search) with PDF/Excel buttons, a KPI strip, one chart, one table. Clicking a KPI or chart bar filters the table (drill-down). The Admin sees every report with all data; other roles see only the reports they are allowed and only their own data. The Admin decides which roles can open which report.

**One place for reports (owner's decision).** The hub is the only reporting area in the app. Every role has exactly one "Reports" menu item, and it opens the hub. Existing statistical pages are folded into the hub, not linked beside it — see section 3.4a. Pages that are part of the workflow (reviewing and writing a tender's evaluation report) stay where the work happens but are renamed so they are not mistaken for reports.

---

## 2. Shared contract (identical in both briefs — do not change without telling Person B)

Create `Services/Reports/Hub/ReportContracts.cs` on day one, commit it, and push your branch so Person B can pull it. Person B will copy the same file if yours is not available yet, so keep it exactly as below.

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
    string Key { get; }                         // url slug, e.g. "purchase-requests"
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
- The report itself applies `scope` (role/department/user) to its data. The hub never shows data the report did not filter.
- Page size 25. When `query.ForExport` is true, return all rows (cap 5,000, add a note if capped).
- Dates are UTC; show `dd MMM yyyy`. Money: `N2` + currency code (JOD shown as JD where the app already does).
- Register with `builder.Services.AddScoped<IReport, YourReport>();` in `Program.cs`.

---

## 3. Your tasks

Work on branch `reports-hub`, created from `ghaith-changes`.

### 3.1 Hub infrastructure (#7)
- `Services/Reports/Hub/ReportRegistry.cs`: injects `IEnumerable<IReport>`, finds by key, returns the reports a user may open.
- **Access rules (Admin-controlled):** new entity `ReportAccessRule { ReportAccessRuleId, ReportKey (nvarchar 100), Role (nvarchar 64), Allowed (bit), UpdatedByUserId, UpdatedAt }`, unique index on `(ReportKey, Role)`. No row = use `IReport.DefaultRoles`. Admin is always allowed. Suppliers never get the hub.
  Migration: `AddReportAccessRules`. Configuration class in `Data/Configurations/`, follow `AiInteractionConfiguration.cs`.
- `Controllers/ReportsController.cs`, `[Authorize(Roles = "Admin,Department,SourcingOfficer,EvaluationCommittee,SourcingManager,TopCommittee")]`:
  - `GET /Reports` — hub: one card per report the user may open. No link cards to old report pages (they are folded in, see 3.4a).
  - `GET /Reports/{key}` — run the report with the query string as `ReportQuery`; 404 for unknown key, 403 (`Forbid()`) when not allowed.
  - `GET /Reports/{key}/export?format=pdf|xlsx` — same query with `ForExport = true`.
  - `GET /Reports/Access` (Admin only) — grid of reports × roles with checkboxes; `POST /Reports/Access` saves (anti-forgery token). Show which entries are defaults.
  - Build `ReportScope` from the signed-in user: role = their workspace role, `DepartmentId` from `ApplicationUser.DepartmentId`, `IsAdmin` for Admin.
- **Validation:** `To >= From`, range ≤ 5 years, unknown status ignored, page clamped. Bad input → page with an inline message, not an exception.

### 3.2 Shared view template
- `Views/Reports/Index.cshtml` (hub) and `Views/Reports/Show.cshtml` (any report), plus partials `_ReportFilters`, `_ReportKpis`, `_ReportChart`, `_ReportTable`.
- Layout order: filter bar with Apply / Clear and PDF / Excel buttons → KPI strip → one chart → table with pagination → notes.
- KPI and chart points with a `Drill` value are links that keep the current filters and add `drill=...`; show the active drill as a removable chip above the table.
- Chart: reuse the CSS bar approach already used on AI Intelligence (`Views/AiIntelligence/Index.cshtml`, `wwwroot/css/ai-intelligence.css`); a donut can be an SVG. No new chart library unless needed.
- `wwwroot/css/reports.css`, `wwwroot/js/reports.js`. Light and dark mode (`[data-theme="dark"]`), no page-level horizontal scroll at 375 px (put `position: relative` on table scroll wrappers — known overflow issue in this project with visually-hidden header labels), visible focus, `aria-label`s on icon buttons.

### 3.3 Generic export
- `Services/Reports/Hub/ReportExport.cs`:
  - **Excel** with ClosedXML (already referenced): sheet "Summary" (title, generated time, filters used, KPIs), sheet "Data" (table, header row bold, frozen, auto-fit, max width 60).
  - **PDF** with PDFsharp following the style of `Services/Reports/ReportPdfExport.cs` and returned through `ReportPdfDownload.Create(...)`: title, filters, KPIs, table (wrap long text, repeat header on new page), notes.
  - File names: `{key}-report-{yyyyMMdd}.pdf|xlsx`.

### 3.4 Navigation
- `Models/PageNavigation.cs` menus are keyed by controller and list actions of that controller (`Views/Shared/_Layout.cshtml`, `_AdminSidebar.cshtml`). Extend the entry type to allow an optional controller so a menu item can point to `ReportsController`.
- Every role except Supplier gets exactly one item named "Reports", pointing to the hub.
- Admin menu: remove "Supplier activity report"; add "Reports".
- Department, Sourcing Officer, Evaluation Committee: add "Reports".
- Sourcing Manager and Top Committee: their existing "Reports" item now points to the hub (their old pages redirect, see 3.4a).

### 3.4a Consolidation — no reports outside the hub
| Existing page | Becomes | Who builds the report | You (A) do |
|---|---|---|---|
| Admin → Supplier activity report (`/Admin/SupplierActivity`) | Hub report `suppliers`; per-supplier PDF kept as a row action | Person B | Remove the menu item; make `/Admin/SupplierActivity` (no `supplierId`) redirect to `/Reports/suppliers`. Keep `?supplierId=` PDF download working. |
| Top Committee → Reports (`/TopCommittee/Reports`, `ReportExport`) | Hub report `top-committee` | Person B | Menu item → hub. Old URLs redirect to `/Reports/top-committee` (export → hub export). |
| Sourcing Manager → Reports & decisions (`/SourcingManager/Reports`, `ReportsPdf`) | Hub report `approvals`, scoped to the manager's own decisions | Person B | Menu item → hub. Old URLs redirect to `/Reports/approvals`. |
| Sourcing Officer → "Evaluation reports" | **Stays** (workflow: review committee report, write summary, send to manager) | — | Rename the menu item and page heading to **"Report review"**. |
| Evaluation Committee → "Combined Reports" / "Evaluation reports" | **Stays** (workflow: the tender's final committee report) | — | Rename the menu item and page heading to **"Committee report"**. |
| AI Intelligence | **Stays** as the AI analysis page | — | Remove its own dashboard export buttons (`ExportDashboard`) from the page, or point them to the matching hub report. Its counts must come from the same queries as the hub reports (call the report services or a shared query class) so numbers never disagree. `ExportTender` (one tender's AI summary) can stay. |

Do the redirects only after Person B's matching report is merged, so no page disappears before its replacement exists. Remove the old views/actions in a follow-up commit once redirects have been checked.

### 3.5 Report #1 — Purchase requests (`Key = "purchase-requests"`)
- Data: `PurchaseRequests` with `Department`, requester, reviewer.
- Statuses in this app: `Submitted`, `UnderReview`, `Approved`, `Returned`, `Rejected`, `ConvertedToTender`. **There is no `Draft` status** (requests go straight to `Submitted`); show it in the status options with a note "Drafts are not saved in this system" rather than inventing data.
- KPIs: total, each status count (drillable), average approval time, returned rate.
- **Average approval time** = average of `ReviewedAt - SubmittedAt` for requests with both values (Approved/Rejected/Returned/ConvertedToTender). Show in days with one decimal; also per department in the table footer or a second KPI.
- Chart: requests per department (drill `department:{id}`). Table: #, title, department, requester, priority, status, submitted, reviewed, approval time; row link to the existing request page for that role.
- Default roles: Admin, Department, SourcingOfficer, SourcingManager.
- Scope: Department → own `DepartmentId` only; Sourcing Officer → the same set as `SourcingOfficerController.ReviewableRequests(userId)` (move that query into a reusable extension in `Services/ProcurementVisibilityQueries.cs` instead of copying it); Manager/Admin → all.

### 3.6 Report #2 — Tenders (`Key = "tenders"`)
- Tender statuses used: `Published`/`Open` (open), `Closed`, `UnderEvaluation`, `AwaitingOfficerSummary`, `AwaitingManagerReview`, `AwaitingCommitteeDecision`, `Awarded`, `Cancelled`.
- KPIs: open, closed / in evaluation, awarded, cancelled, **supplier participation rate** (submitted bids ÷ invitations, in range), **tenders with zero offers** (deadline passed, no submitted bid), **evaluation delays** (status `UnderEvaluation` and `ClosesAt` older than `Reports:EvaluationDelayDays`, default 7, from configuration).
- Chart: tenders by status (drillable). Table: #, title, category, status, opens, closes, invited, submitted, compliant, awarded supplier, days in evaluation; row link to the role's tender page.
- Note: the app does not record when each stage started; "days in evaluation" is measured from `ClosesAt`. Say so in `Notes`.
- Default roles: Admin, SourcingOfficer, SourcingManager, TopCommittee, EvaluationCommittee. Scope with the existing helpers in `Services/ProcurementVisibilityQueries.cs` (e.g. `InEvaluationWorkspace` for committee members); Department sees tenders created from its own requests.
- AI Intelligence already computes several of these (`Services/Intelligence/AiIntelligenceService.cs`: zero-offer tenders, participation). Reuse its logic where it fits; don't make the numbers disagree.

### 3.7 #8 Admin dashboard drill-down
- On `Views/Admin/Dashboard.cshtml`, make every KPI card and chart a link into the hub with the right drill, e.g. purchase requests count → `/Reports/purchase-requests`, pending supplier applications → existing Supplier Applications page, tenders count → `/Reports/tenders`.
- Add a small "Reports" panel on the Admin dashboard: the 3–4 most useful KPIs from the reports above, each drillable.

### 3.8 Tests — `Checks/ReportsHubChecks.cs`
Follow the existing checks style (`Checks/*.cs`, run from `Program.cs` switches). Cover:
1. Default access: a role in `DefaultRoles` can open; a role outside gets 403; Supplier never sees the hub.
2. Admin override row allows/denies a role; Admin is always allowed.
3. Department scope only returns its own department's requests.
4. Average approval time is correct for a known set (include one request without `ReviewedAt`).
5. Zero-offer and evaluation-delay counts for a known set.
6. Drill filter narrows the table; invalid filters do not throw.
7. PDF starts with `%PDF`, XLSX opens with ClosedXML and has "Summary" and "Data" sheets.
Use a throwaway database (name it `TiryaqReportsQA_<random>`), create test data only there, and drop it afterwards. Never seed the shared local database from tests.

---

## 4. Files you own (Person B will not edit these)
`Services/Reports/Hub/*` (except B's own report classes), `Controllers/ReportsController.cs`, `Views/Reports/*`, `wwwroot/css/reports.css`, `wwwroot/js/reports.js`, `Models/PageNavigation.cs`, `Views/Shared/_Layout.cshtml`, `Views/Shared/_AdminSidebar.cshtml`, `Views/Admin/Dashboard.cshtml`, `Controllers/AdminController.cs` (dashboard part), `Services/ProcurementVisibilityQueries.cs`, AI Intelligence page/export changes, the redirects and renames in 3.4a, your migration.

Shared file: `Program.cs` — only add registration lines; expect a trivial merge with B.

## 5. Working rules
- Run the app on https://localhost:7130 (`dotnet run --launch-profile https`). The running app locks `bin`: stop it before `dotnet build`, restart after, and confirm it returns 200.
- Build must have 0 errors and 0 warnings. Run existing checks you might affect (`dotnet run --no-build -- --vllm-ai-checks` and the harnesses in `Checks/`).
- Migrations: if Person B's migrations are merged before yours, delete your migration and re-create it on top (`dotnet ef migrations remove` / `add`) so the model snapshot stays clean.
- Do not commit `.config/`, `appsettings.Local.json`, `Data/Seed/`, screenshots or test output. Commit to `reports-hub` only; open a PR into `ghaith-changes` when done. Do not push to `ghaith-changes`, `develop` or `main` directly.
- Test accounts: ask the project owner for the shared password; don't put it in files.

## 6. Done means
- Hub, access page, template, exports, navigation, reports #1 and #2, dashboard drill-down working for Admin and checked for at least Department and Sourcing Officer scopes.
- Every role's sidebar has one "Reports" item; old report URLs redirect to the hub; workflow pages renamed; no other page in the app calls itself a report or offers report exports outside the hub (except the per-tender workflow documents).
- `ReportsHubChecks` pass; existing checks still pass.
- Checked in the browser at desktop and 375 px, light and dark.
- A short note in the PR: what was verified, what was not, and any numbers that differ from AI Intelligence and why.
