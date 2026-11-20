# Safi chat attachments

## Image questions

Open Safi from an authenticated workspace, attach a PNG/JPEG/WebP image (up to 5 MB and 20 megapixels), and enter a question. The server validates and re-encodes the image, removes source metadata, and sends it with the question to the configured AI provider. Images are not saved in the application or chat history. Image analysis is advisory and cannot execute workspace actions. `Vllm:VisionModel` optionally overrides the primary model for image requests. The configured Qwen model was tested with real image input. Provider quotas still apply; longer cooldowns are reported without waiting indefinitely.

## Administrator account import

### PDF and Excel analysis for all accounts

All active account roles, including Supplier accounts pending approval, can attach a searchable `.pdf` or `.xlsx` workbook through the same plus button. Excel defaults to read-only analysis, with sensitive credential columns omitted. Only Admin can select **Create accounts (Admin)** or request account creation; the server rejects other roles' account-creation requests even if they tamper with the request fields. Analysis has no database tools and never creates accounts. Excel analysis samples up to five sheets, the first 200 rows and 30 columns per sheet, and 12,000 characters. Formula cells are marked as unevaluated. PDFs are limited to 5 MB and 30 pages. The server extracts up to 12,000 characters with page markers and sends that excerpt and the user's question to the configured AI provider. No attachment is saved. Scanned/image-only and encrypted PDFs receive a specific validation message; upload page images for visual analysis. Existing restrictions on procurement actions remain unchanged. The account-import template remains Admin-only.

1. Sign in as Admin and open Safi.
2. Download **Accounts template** next to Attach.
3. Fill the first sheet. Email and Password are required. FullName defaults to the email prefix. Role can be supplied in the workbook or selected in chat (default SourcingOfficer).
4. Attach the `.xlsx` file, select **Create accounts (Admin)** and send it. Review the validation table. The default **Analyze workbook** option only explains the file.
5. If all rows are valid, click **Confirm account creation**. Remove the attachment to cancel.
6. Accounts are created using ASP.NET Identity and the supplied passwords work in the normal login form. Passwords are hashed, never included in the preview/response, and never sent to an AI provider. Imported supplier profiles start Pending, following the normal approval workflow.

All records are validated again in a serializable transaction before creation. Any invalid row blocks the entire import. Existing accounts are never modified or duplicated. Import at most 100 rows; compressed uploads are limited to 5 MB, expanded workbook content to 20 MB. Formula cells are rejected.

Supported columns: `Email`, `Password`, `FullName`, `Role`, `DepartmentId`, `CommitteeRole`, `CompanyName`, `Category`, `RegistrationNumber`.

- Department requires an active DepartmentId.
- EvaluationCommittee and TopCommittee require an available matching CommitteeRole. An occupied active seat cannot be duplicated.
- Supplier requires CompanyName, Category and RegistrationNumber. Supported categories and committee roles are listed on the template's Instructions sheet.
- Passwords follow the existing site policy; do not remove password requirements or store plaintext passwords in the database.

## Verification

With the app running locally at https://localhost:7130, run:

```powershell
dotnet run --no-launch-profile -- --chat-attachment-checks
```

This opt-in HTTP integration suite creates uniquely named temporary QA accounts, tests real workbook preview/creation and password login, role isolation, duplicate rejection, atomic validation, CSRF, invalid image rejection and live vision. It removes only its own QA accounts in `finally`. Live vision requires provider quota. The ordinary regression suite remains `--vllm-ai-checks`.
