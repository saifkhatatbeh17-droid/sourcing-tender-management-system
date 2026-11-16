# Top Committee Phase 5 — Reports

Completed locally on `Mohammad`, 2026-11-13. Phase 5 built and passed its checks before Phase 6 implementation began.

## Implemented behavior

- Assignment-scoped, read-only reports, rechecking the authenticated user's active database account and Top Committee role.
- SQL-derived assigned total, matching total, pending, awarded, rejected, Manager-review and own completed-decision counts. Rejected and Manager-review counts can overlap; they are not additive categories.
- Server-side title search, existing tender status, own decision (`Approved`, `Rejected`, `NotVoted`), inclusive tender-created date range and deterministic 20-row pagination.
- Dates use UTC. The date filter is tender creation date, not decision date. The assigned total is unfiltered; other summary figures reflect the filters.
- Tender ID/title/status, nominated bid, awarded bid, recorded approval progress, final outcome and latest committee-decision/award date. Existing workflow statuses are used without introducing new values.
- PDF and `.xlsx` exports use the same authorized filtered query, independent of the displayed page. More than 5,000 matches returns an explicit narrow-the-filters error; records are never silently truncated.
- Empty results retain summary, headings and a meaningful empty message. Private clarification questions, responses, credentials and user secrets are excluded from report DTOs and exports.

## Packages and compatibility audit

The target framework is `net10.0`. The exact approved Core package IDs were verified before installation:

| Package ID | Pinned version | License |
| --- | --- | --- |
| PDFsharp-MigraDoc | 6.2.4 | MIT |
| ClosedXML | 0.105.1 | MIT |

Official package metadata: [PDFsharp-MigraDoc 6.2.4](https://www.nuget.org/packages/PDFsharp-MigraDoc/6.2.4), [ClosedXML 0.105.1](https://www.nuget.org/packages/ClosedXML/0.105.1). Restore and compilation verified compatible assets for this target. No GDI/WPF package or newer PDFsharp version was substituted.

Resolved export dependency audit from the NuGet packages:

| Dependency | Version | License |
| --- | --- | --- |
| PDFsharp | 6.2.4 | MIT |
| ClosedXML.Parser | 2.0.0 | MIT |
| DocumentFormat.OpenXml | 3.1.1 | MIT |
| DocumentFormat.OpenXml.Framework | 3.1.1 | MIT |
| ExcelNumberFormat | 1.1.0 | MIT |
| RBush.Signed | 4.0.0 | MIT |
| SixLabors.Fonts | 1.0.0 | Apache-2.0 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT |
| System.IO.Packaging | 8.0.1 | MIT |

The project's existing explicit `System.Security.Cryptography.Pkcs` 10.0.14 reference was retained. An audit project without that reference initially selected vulnerable 8.0.1; matching the real project's 10.0.14 resolution removed those findings. The resulting dependency audit reported no known vulnerabilities at review time. This is a point-in-time package advisory check, not a guarantee against future advisories. Preserve applicable MIT and Apache notices when distributing dependencies. Future package upgrades require a fresh license/security review; newer PDFsharp licensing must not be assumed equivalent.

## Export formatting

PDF uses landscape A4, repeatable table headings, automatic page breaks, title, UTC generation timestamp, filters, summary and page numbering. A 160-row sample produced eight pages; first and second pages were rendered and visually inspected, and text across all pages was parsed to verify first/last records.

Excel contains `Summary` and `Tenders` worksheets, typed dates/numeric cells, column widths, wrapping, frozen headings and a table filter. Text is written as values, never as formulas; a formula-injection sample was checked.

The PDF font resolver defaults to Windows `Fonts/arial.ttf`. For other deployments, configure `TOP_COMMITTEE_REPORT_FONT` with an accessible, appropriately licensed TrueType font path before first export. Font embedding permissions remain a deployment responsibility. Complex Arabic/RTL shaping has not been validated; the implemented report UI and tested documents are English.

## Files

Created:

- `Controllers/TopCommitteeController.Reports.cs`
- `Models/TopCommittee/CommitteeReportModels.cs`
- `Services/TopCommittee/TopCommitteeReports.cs`
- `Services/TopCommittee/TopCommitteeReportExport.cs`
- `Checks/TopCommitteePhase5Checks.cs`
- This document.

Updated: `Controllers/TopCommitteeController.cs` (partial class and replacement of Reports placeholder), `Services/TopCommittee/TopCommitteeServiceCollectionExtensions.cs`, `Views/TopCommittee/Reports.cshtml`, `Checks/TopCommitteeFoundationChecks.cs`, and `SourcingTenderManagement.CodeFirst.csproj`. Phase 6 subsequently added a Reports-to-Notifications navigation link.

## Validation

Phase 5 gate: build passed with zero warnings/errors; all 254 prior checks and 22 Phase 5 checks passed. Phase 5 has 14 non-SQL checks plus eight optional SQL checks. SQL checks used the existing isolated `SourcingTender_Phase46_b4e8d015226040b8a16507d33fda0bcc` database, without test writes to the original database.

Coverage includes real SQL totals/filtering/pagination and assignment scope, anonymous access, invalid filters, Razor output, empty exports, PDF pagination/content, workbook structure/types and formula injection. Actual MVC browser login, reports, filtering and both downloads were exercised; downloaded PDF text and workbook contents were parsed. Desktop and 390px mobile layouts were inspected with contained table scrolling and no document overflow.

The final combined report records later cross-phase HTTP/security tests and database integrity verification.
