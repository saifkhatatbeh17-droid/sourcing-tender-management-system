# Top Committee UI redesign

The final redesign is limited to Top Committee content. All Top Committee improvements remain intact: five dynamic KPI cards, consistent headers/actions, filters, readable tables, status badges, tender detail tabs, voting forms, bid modal, clarification threads, notifications, responsive layouts and light/dark styling.

Admin styles reused: teal #00a3a6, Segoe UI, shared surface/text/border tokens, 18px radii, 24px panel padding and 120px KPI cards.

## Retained modified files

- Views/TopCommittee/Dashboard.cshtml
- Views/TopCommittee/Decisions.cshtml
- Views/TopCommittee/History.cshtml
- Views/TopCommittee/Reports.cshtml
- Views/TopCommittee/Clarifications.cshtml
- Views/TopCommittee/Notifications.cshtml
- Views/TopCommittee/TenderDetails.cshtml
- Views/TopCommittee/_ClarificationThread.cshtml
- wwwroot/css/top-committee.css
- UI-REDESIGN-NOTES.md (this record)

## Face ID restoration

Restored the following files byte-for-byte from commit de45c37ae592c9c10e991a0fb9d9a778fc30b9e7 after inspecting their diffs:

- Views/FaceEnrollment/Index.cshtml
- Views/FaceVerification/Index.cshtml
- Views/FaceLogin/Index.cshtml
- Views/FaceLogin/_FaceIdModal.cshtml
- Views/FaceSetup/Index.cshtml
- Views/FaceSetup/_SetupModal.cshtml
- wwwroot/css/face-enrollment.css
- wwwroot/css/face-login-modal.css
- wwwroot/css/face-setup.css

Removed the redesign-only wwwroot/css/face-id.css after confirming it was absent from the baseline and its imports were removed by restoration. Face ID JavaScript, forms, authentication and backend integration remain unchanged.

## Verification

- SHA-256 comparison confirms every Top Committee view and top-committee.css is unchanged by the Face ID restoration.
- Git comparison confirms the nine restored Face ID files exactly match the specified baseline.
- Build passed with zero warnings/errors using dotnet build SourcingTenderManagement.CodeFirst.csproj --no-restore -p:UseAppHost=false -o C:/Users/USER/AppData/Local/Temp/tender-ui-build. Isolated output avoids the executable running in Visual Studio.
- All 70 existing Face ID client simulation checks passed.
- JavaScript syntax checks passed for all Face ID scripts and top-committee-workspace.js.
- git diff --check passed.
- Final diff contains only retained Top Committee views/CSS and this notes file. No backend, database, migrations, shared Sidebar, shared layout, Login page or other role changes were introduced.

Earlier Top Committee browser fixture checks covered desktop, tablet and mobile layouts, dark mode, tender tabs and the bid modal. No new browser or physical-camera testing was performed during this restoration. Real camera capture and live backend submissions remain manual checks. No database commands or Git history/branch mutations were performed.
