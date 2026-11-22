# Reports integration follow-up — Phase 8A.1

## Fixed compatibility boundaries

ReportRegistry applies fixed implementation boundaries before stored overrides:

- Admin always allowed; Supplier always denied.
- `top-committee`: only Admin and TopCommittee. TopCommittee permissions remain editable; other non-Admin roles are unavailable.
- `audit-log`: Admin only, matching the existing implementation and specification.
- Other registered reports retain DefaultRoles and stored overrides, with their existing data scopes.

Unsupported stored Allow rules remain stored but cannot grant access. Access management disables these cells and rejects unsupported changes on POST. Report pages and both exports use the same registry. The existing committee access-denied exception is also translated to 403, not 500. No Person B calculations or shared IReport contracts changed.

## Legacy endpoints deliberately retained

The following endpoints do not use ReportRegistry. A Reports Hub stored Deny does **not** revoke their existing role-authorized access:

| Endpoint | Current behavior / integration gap |
| --- | --- |
| `/Admin/SupplierActivity` | Legacy supplier statistics view; `?supplierId=...` returns a single-supplier PDF. |
| `/SourcingManager/Reports` | Legacy manager decision statistics and filters. |
| `/SourcingManager/ReportsPdf` | Exports those legacy decisions. |
| `/TopCommittee/Reports` | Legacy committee statistics, using existing committee authorization/scope. |
| `/TopCommittee/ReportExport?format=pdf\|xlsx` | Legacy committee export with its existing authorization. |
| `/AiIntelligence/ExportDashboard` | AI dashboard export, still linked from its existing view. |

Workflow documents such as Officer ReportPdf, Committee report documents, Manager SubmissionPdf and AI ExportTender also retain their own authorization. They are not automatically replaced by statistical hub exports. No legacy action, link, authorization or redirect was changed in this phase. Replacement needs an explicit compatibility decision and regression coverage before making the hub the sole statistical entry point.

## Person B issue: Pending compliance drill

Location: `Services/Reports/Data/BidsEvaluationReport.cs`, compliance chart and `selected` status switch.

Reproduction: a visible, non-excluded Submitted bid has `ComplianceStatus = null`. The chart counts it as Pending (`ComplianceStatus ?? "Pending"`), but the `status:Pending` drill compares the literal value to Bid.Status or ComplianceStatus. Neither matches; the chart's pending bid disappears from the table/export.

Person B should make the Pending drill use the same definition as the chart, preserving exclusion and scope rules, and add a null-compliance regression case. No calculation or file belonging to Person B was edited here.

## Tenders performance

Previously the report materialized every matching tender and all projected metadata before drill-down/pagination. Now SQL performs KPI aggregation, status grouping, drill filtering, result counting and ordered Skip/Take. Only 25 rows (normal page) or at most 5,000 rows (export) are materialized. KPIs remain based on base filters; drills narrow only the table/export. Total and the existing export-limit note remain intact.

This uses multiple read queries rather than a single materialization. Concurrent writes between queries can affect snapshot consistency, as with other report pages; no new transaction or isolation policy is introduced. Production-size SQL Server execution plans and latency still require integration testing. SQL translation checks are not execution-plan measurements.

## Phase 8B boundary

- No SQL Server query, database update, migration application or production startup is part of Phase 8A.1.
- `20261122194424_AddReportAccessRules.Up()` creates only dbo.ReportAccessRules and its unique ReportKey/Role index. It is unchanged. Existing SQL Server migration history has not been inspected in this phase.
- Before integration testing: explicitly identify a separate local test database, back up any existing data, inspect its migration history and every pending Up(), and obtain approval before any database writes.
- Verify actual SQL Server report queries, scope for each role, incompatible historical grants, Admin matrix POSTs, page/PDF/XLSX parity, and existing workflow pages using synthetic test records.
- Keep the Pending drill and legacy-route integration gaps visible; do not interpret isolated passing checks as closure of those outstanding requirements.

## Isolated regression checks

Existing ReportsAccessChecks, ReportsHubChecks, ReportsExportChecks, PurchaseRequestsReportChecks, TendersReportChecks and ReportsNavigationChecks require an explicitly supplied SQLite in-memory context. The temporary check host remains outside the repository and does not execute application startup/seed jobs. ReportsExportChecks.RunCompatibilityHttpChecksAsync additionally verifies the real Top Committee page/PDF/XLSX and Admin matrix/antiforgery POST using isolated loopback clients.

`ReportsDataChecks` is intentionally not executed here: it creates a SQL Server QA database and applies migrations. It requires separate Phase 8B approval. Workflow checks in this phase verify retained actions and role authorization; they do not replace authenticated end-to-end workflow testing on the test database.
