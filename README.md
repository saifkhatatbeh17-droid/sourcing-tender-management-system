# Sourcing & Tender Management — Code First

ASP.NET Core MVC 10, SQL Server, and EF Core code-first migrations. This is a separate project from the earlier database-first starter. The latest merged pages, sidebars, login, roles, supplier History, and manager submission/chosen-offer screens are included.

The C# entities and `ApplicationDbContext` define the database. EF migrations create and update SQL Server. There is one context for Identity and all business entities; no database scaffolding is needed.

## Open and run

Open `SourcingTenderManagement.CodeFirst.sln` in Visual Studio, or open this project folder. Run commands in a terminal from the folder containing the `.csproj`.

Requirements: .NET 10 SDK and SQL Server. The default connection uses localhost and Windows Authentication:

```json
"DefaultConnection": "Server=localhost;Database=SourcingTenderManagementCodeFirst;Trusted_Connection=True;TrustServerCertificate=True;"
```

If your SQL Server name differs, override the connection in `appsettings.Local.json` (ignored by Git), or set environment variable `ConnectionStrings__DefaultConnection`.

First run on a teammate's computer:

```powershell
dotnet tool restore
dotnet restore
dotnet ef database update
.\scripts\create-admin.ps1 -Email admin@example.com
dotnet run --launch-profile http
```

The admin script prompts for a password without displaying it. The application seeds its 15 role records when it starts, including when running the admin command. There are no shared default credentials in the source.

Open **http://localhost:5072**. The optional HTTPS launch profile uses **https://localhost:7130**. These ports differ from the original project's 5062/7120 ports so the two versions can coexist. The new version also uses its own authentication cookie.

You can also run `scripts/setup-database.ps1`; it restores the tools/packages and applies the migrations. You do not need to manually create tables or run the old SQL scripts.

## Your local copy

The new database `SourcingTenderManagementCodeFirst` has already been created on the original computer. The 13 existing demo accounts, their role assignments, the demo department, and the demo supplier profile were copied locally. Their existing passwords still work. The original `SourcingTenderManagement` database and old project were left intact.

Those account records and password hashes are **not** included in the ZIP or Git repository. A teammate creates their own administrator using the command above.

## Where your team writes code

| File/folder | Purpose |
|---|---|
| `Models/Entities/` | 21 business entity classes; edit these directly |
| `Data/ApplicationUser.cs` | Identity account plus department/profile relationships |
| `Data/ApplicationDbContext.cs` | One context: relationships, keys, lengths, money precision, defaults, checks, and delete rules |
| `Data/Migrations/` | EF-generated migration, designer, and model snapshot; commit these together |
| `Controllers/` | Page actions, account processing, and future workflow logic |
| `Views/<Role>/` | Merged role pages and partial views; search for `TEAM TODO` |
| `Models/PageNavigation.cs` | Sidebar destinations for the seven roles |
| `ViewComponents/NotificationBellViewComponent.cs` | Account-specific notification display |
| `Checks/` | Identity, route/security, and SQL constraint checks |

The model creates **28 application tables**: 21 business tables and 7 standard Identity tables. SQL Server also contains `__EFMigrationsHistory`, so SSMS displays **29 tables**. The history table tells EF which migrations have already run.

Relationships are the same as the earlier starter: departments own purchase requests, requests contain items and lead to tenders, suppliers receive invitations and submit bids, committees evaluate bids and record approvals, and questions/documents/notifications link to the related records. See [PAGE-MAP.md](PAGE-MAP.md) for page ownership and routes.

## Changing the database later

1. Change the relevant entity and/or the configuration in `ApplicationDbContext`.
2. Create a migration with a descriptive name:

   ```powershell
   dotnet ef migrations add AddDeliveryNotes
   ```

3. Read the generated migration. Check whether it adds, changes, renames, or removes columns; migrations are not a guarantee against data loss.
4. Apply it:

   ```powershell
   dotnet ef database update
   ```

5. Commit the entity/context changes, migration, designer, and snapshot together. Teammates pull them and run `dotnet ef database update` against their own databases.

Do not run `Scaffold-DbContext` or `dotnet ef dbcontext scaffold` in this version. Do not manually change SQL Server's schema and expect C# to update automatically. Do not use `EnsureCreated` alongside migrations. Coordinate migration creation so two people do not create conflicting migrations at the same time.

## Delete behaviour

Business relationships use **NO ACTION**. If another record still references a department, request, tender, supplier, or offer, SQL Server blocks deleting that parent. Reassign or explicitly remove dependent records as appropriate. Deleting a parent does not silently delete business records or clear their foreign keys.

Identity retains its standard cascading rules for its own account/role-link tables. A user referenced by business records is still protected by the business foreign keys. Prefer the existing active/inactive controls to deleting accounts.

## What is complete and what remains

Working features carried over: Identity login/logout, role checks, employee/department creation and editing, supplier registration/correction/approval, account-specific notification display, supplier list/details, and reading an officer's tenders/chosen offer.

Business pages are still a team starter. Purchase/tender/offer submission, scoring, report generation, exports, legal-document handling, clarification messages, notification generation/read updates, Ollama calls, and the approval state machine are implementation areas. Popup and tab bodies marked `TEAM TODO` are deliberately unfinished. Supplier History is for the supplier's own submitted offers and personal summaries; suppliers do not send evaluation reports.

The agreed decision flow is: officer evaluation/summary → manager writes a separate summary → Top Committee chooses an offer with four approvals → manager approves the shared report → officer closes the process and involved roles see permitted status updates. The manager does not edit the supplier's submitted offer. Approval buttons remain template controls until that workflow is implemented.

The two committees each retain four member positions and four separate approvals. Audit logs, face login, deployment setup, and extra architecture are excluded. Optional brief features and fingerprint login remain undecided.

## Verify

```powershell
dotnet build
dotnet ef migrations has-pending-model-changes
dotnet run --no-launch-profile -- --self-test
```

With the app running separately on port 5072, optionally run:

```powershell
python Checks/http-smoke.py
sqlcmd -S localhost -E -C -I -b -i Checks/schema-checks.sql
```

The HTTP checks require Python 3 and sqlcmd; they use the default local code-first database. They create randomly named fixtures and remove only those fixtures. The SQL checks roll back their temporary rows. These tests do not prove completion of the unfinished business workflows.

## GitHub handoff

Commit this project, its migrations, and its tool manifest. Do not upload `bin/`, `obj/`, local connection overrides, database backups/data files, or passwords. Each teammate applies the same migrations to their own database; GitHub does not synchronize database records.

Official references: [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) and [ASP.NET Core Identity model](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0).


### AI bid recommendations

The Evaluation Committee page uses local Ollama, with no API key or cloud provider. The default model is `qwen2.5:7b` at `http://localhost:11434/api/generate`. Keep Ollama running, enter the financial and technical percentages under **Tender & offers**, then select **Find best offer**. Use whole percentages between 0 and 100, totaling 100%.

The AI compares submitted offers against purchase requirements and returns one recommendation using the entered percentages. Insufficient evidence can produce no recommendation. It does not use committee scores, fill score fields, approve, award, or advance the tender. The input contains tender requirements, bid items/specifications, totals, delivery/payment terms and validity; uploaded attachments are not read. The local model can make factual or comparative errors; committee members must check its reasons against the bids.

Optional `appsettings.Local.json` configuration (merge with any existing settings):

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5:7b"
  }
}
```

Only local HTTP endpoints are accepted. The recommendation and chosen percentages are saved in the existing `AiAnalyses` table. Changed bids hide the old result until regenerated. Invalid bid IDs, mismatched weights, incomplete comparisons, mixed-currency financial recommendations, financial-only selections above the lowest total, provider errors and incomplete output are rejected. Requests and model output use Ollama's [structured JSON format](https://docs.ollama.com/capabilities/structured-outputs). Apply the included `AllowCustomAiWeights` migration with `Update-Database` or `dotnet ef database update` to allow custom percentages.


### Committee evaluation basis

The shared form edits one price weight and one technical-requirements weight for the entire tender. Both must be positive, have at most two decimals, and sum to 100%. Assigned committee members share these weights; changing them clears existing evaluation approvals. Four distinct member approvals complete the report and advance the tender to `AwaitingOfficerSummary`. No manually entered per-supplier scores are required, and approval does not automatically select a supplier. Existing historical score records remain available in older reports. AI comparisons are advisory and use separate percentages. Members can post or update a shared committee comment without clearing approvals.
