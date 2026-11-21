# Spec match heatmap — implementation brief

Brief for the agent building this feature in TIRYAQ (ASP.NET Core MVC, .NET 10, EF Core code-first, SQL Server LocalDB).
Repo: `C:\Users\ghaith\Desktop\sourcing-tender-management-system`, branch `ghaith-changes`. Read `AGENTS.md` and `PROJECT-STATE.md` first.
Do not push, merge or commit unless the user asks.

---

## 1. Overview

### What it is
A colour-coded grid on the Evaluation Committee page for **one tender** (`/EvaluationCommittee/Evaluations/{id}`).
It shows, at a glance, whether each offer actually supplies what the department requested.

- **Rows** = the requested items of that tender (`PurchaseRequestItems` of the tender's purchase request).
- **Columns** = the eligible offers on that tender (compliant `Bids`, one per supplier).
- **Each cell** = one requested item × one offer. Its colour answers: "did this supplier offer this item with the requested specification?"
  - Green **Match**, amber **Partial**, red **Missing**, grey **Needs check** (AI claim could not be verified) / **Not assessed**.
- **Hover** a cell for a quick tooltip; **click** it to pin the detail under the grid (needed on mobile): the reason (one line) and a quote from the supplier's offer as evidence.
- **Coverage row** at the bottom: per offer, Match = 1, Partial = 0.5, others = 0, shown as `1.5 / 2`.

It is a reading aid. It never changes committee marks, the ranking, the winner or the report.

### Why
Today a committee member opens every offer card and compares specs in their head. With six offers and several items that is slow and error-prone. The heatmap does the comparison for all offers in one view.

### Worked example (tender 41, "DEMO EVAL 02 - Packaging line spare parts")
Rows: "Capping head assembly — 6 units, stainless steel 316L, torque 0.8–2.0 N·m" and "Filling nozzle kit — 10 kits, food-grade seals, 100 mL".
Columns: Rimal #49, Levant #50, Al-Shifa #51, Petra #58, Amman #59, Jordan Valley #60.
Example cell: Jordan Valley × Capping head = **Partial**, reason "Says 'equivalent parts' without naming the steel grade; torque range matches", evidence "Equivalent parts, six-month warranty."

---

## 2. Ground rules (non-negotiable)

1. **Code decides facts, AI only judges wording.** Whether an item was offered and whether the quantity is enough is computed in C#. The AI can never turn a code-red cell green.
2. **Every AI verdict needs evidence.** The quoted evidence must actually appear in that offer's text (normalised: collapse whitespace, lower-case). If not, the cell becomes **Needs check**. Same idea as `OfferPreCheck.Verify` in `Services/Compliance/OfferPreCheck.cs` — reuse its normalisation approach.
3. **All model calls go through `AiGateway`** (`Services/Ai/AiGateway.cs`) so they are logged in `AiInteractions` and visible on Admin → AI log.
4. **Supplier text is untrusted.** Wrap it with `AiUntrusted.Wrap(source, text)` and include `AiUntrusted.Rule` in the system prompt.
5. **Never fail the page.** If the AI is unavailable (Groq 429, timeout, bad JSON), show the code-only grid with the note "AI matching unavailable. Showing quantity and coverage checks only." No exception reaches the user.
6. **Drop, don't reject.** A malformed or unverifiable cell is downgraded on its own; never discard the whole AI answer because of one bad cell.
7. **One AI call per tender**, not per cell. Save the result; re-run only when the offers or request change.

---

## 3. Data

New entity `Models/Entities/SpecMatchAnalysis.cs`:

| Column | Type | Notes |
|---|---|---|
| `SpecMatchAnalysisId` | int, PK | identity |
| `TenderId` | int, FK → Tenders | index |
| `Fingerprint` | nvarchar(64) | SHA-256 hex, see below |
| `ResultJson` | nvarchar(max) | verified cells, serialized |
| `ModelName` | nvarchar(200), nullable | null when AI was unavailable |
| `AiAvailable` | bit | false = code-only result |
| `CreatedByUserId` | nvarchar(450) | FK → AspNetUsers |
| `CreatedAt` | datetime2 | UTC |

- Configuration in `Data/Configurations/SpecMatchAnalysisConfiguration.cs` (follow `AiInteractionConfiguration.cs`), `DbSet<SpecMatchAnalysis>` in `ApplicationDbContext`.
- Migration: `dotnet ef migrations add AddSpecMatchAnalysis` (latest existing is `20261120181334_AddAiInteractionLog`).
- **Fingerprint** = SHA-256 of a canonical JSON of: every requested item (`PurchaseRequestItemId`, `ItemName`, `Quantity`, `Unit`, `Specifications`) + every eligible offer (`BidId`, `TechnicalDetails`, and each `BidItem`'s `PurchaseRequestItemId`, `Quantity`, `OfferedSpecifications`), ordered by IDs. If it differs from the saved one, the result is **outdated**.

---

## 4. Service — `Services/Evaluation/SpecMatchService.cs`

Register like `OfferPreCheck` in `Program.cs`:
`builder.Services.AddHttpClient<SourcingTenderManagement.Services.Evaluation.SpecMatchService>(client => client.Timeout = TimeSpan.FromSeconds(90));`
Constructor: `(HttpClient http, IConfiguration configuration, AiGateway? gateway = null)` — same pattern as `BidRecommendationService` (use `gateway ?? AiGateway.Detached`).

### Public API
```csharp
public record SpecMatchCell(int BidId, int ItemId, string Verdict, string? Reason, string? Evidence, string Source); // Source: "code" | "ai"
public record SpecMatchResult(IReadOnlyList<SpecMatchCell> Cells, bool AiAvailable, string? ModelName);
public static string Fingerprint(CommitteeEvaluationPage page);
public Task<SpecMatchResult> RunAsync(CommitteeEvaluationPage page, CancellationToken ct);
```
Verdict values: `Match`, `Partial`, `Missing`, `NeedsCheck`, `NotAssessed`.

### Step 1 — code checks (no AI)
For each requested item × offer:
- No `BidItem` for that `PurchaseRequestItemId` → `Missing`, reason "Not offered", Source `code`.
- `BidItem.Quantity < requested Quantity` → `Partial`, reason "Offered {q} of {requested} {unit}", Source `code`.
- Otherwise → candidate for the AI step.

### Step 2 — one AI call
- `await Ai.CompleteJsonAsync(http, configuration, new AiRequest(Feature: "evaluation.spec-match", SystemPrompt: ..., UserPrompt: ..., SchemaName: "spec_match", Schema: schema, MaxTokens: 1800, Interactive: true, Workspace: "EvaluationCommittee", RecordType: "Tender", RecordId: tenderId), ct)`.
- Only send candidate pairs. Input per pair: requested item name, quantity, unit, specification; offered specification and the offer's `TechnicalDetails` (wrapped with `AiUntrusted.Wrap`).
- Schema:
```json
{ "type": "object", "additionalProperties": false, "required": ["cells"],
  "properties": { "cells": { "type": "array", "items": {
    "type": "object", "additionalProperties": false,
    "required": ["bidId", "itemId", "verdict", "reason", "evidence"],
    "properties": {
      "bidId": { "type": "integer" }, "itemId": { "type": "integer" },
      "verdict": { "type": "string", "enum": ["match", "partial", "missing"] },
      "reason": { "type": "string", "maxLength": 160 },
      "evidence": { "type": "string", "maxLength": 200 } } } } } }
```
- System prompt essentials: compare the requested specification with what the supplier states; `match` only when every stated requirement is addressed; `partial` when something is vague, generic ("equivalent", "compatible") or partly met; `missing` when the requirement is contradicted or absent; `evidence` must be copied word-for-word from the offer text; never judge price, delivery or warranty; plain English, no Markdown; output JSON only.
- If the input is larger than ~30 KB (UTF-8), split into one call per offer and merge.

### Step 3 — verify (server side)
- Drop cells with unknown `bidId`/`itemId`, or pairs that step 1 already decided.
- Evidence not found in that offer's normalised text → `NeedsCheck`, reason "AI quote not found in the offer".
- Candidate pairs the AI skipped → `NotAssessed`.
- Truncate reason/evidence to schema limits.
- On `HttpRequestException`, `TaskCanceledException` (not user-cancelled), `JsonException`, `AiLimitReachedException`: return code cells + all candidates `NotAssessed`, `AiAvailable = false`.

---

## 5. Controller — `Controllers/EvaluationCommitteeController.SpecMatch.cs`

The controller is a `partial class`; add `SpecMatchService specMatch` to the primary constructor in `EvaluationCommitteeController.cs`.

- `[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> RunSpecMatch(int id, CancellationToken ct)`
  - Load the tender with the existing private `TenderQuery()` (it already includes purchase request items and compliant bids with `BidItems`).
  - Allow only users with an `Evaluation` `CommitteeAssignment` on that tender (`Forbid()` otherwise).
  - Run the service, save a new `SpecMatchAnalysis`, set `TempData["CommitteeSuccess"]` ("Spec match updated") and redirect to `Evaluations` for that id.
  - Works in any tender status that is visible to the committee (read-only aid), including after weights lock.
- In `LoadPage`, when a tender is selected: load the latest `SpecMatchAnalysis` for it into new properties on `CommitteeEvaluationPage`:
  `SpecMatchResult? SpecMatch`, `bool SpecMatchOutdated` (fingerprint mismatch), `DateTime? SpecMatchCreatedAt`.

---

## 6. UI

- `Views/EvaluationCommittee/_SpecMatch.cshtml` — render in `Evaluations.cshtml` just above the `ev-offer-grid` (offer cards), after the "Eligible offers" heading block.
  - Header: title "Spec match", one-line subtitle, legend (Match / Partial / Missing / Needs check), button "Run spec match" (or "Run again" when outdated/present). Show "Updated {time} UTC" and an "Outdated — offers changed" note when relevant.
  - `<table>` with sticky first column (requested item name + qty/unit/spec in small text), one column per offer (supplier short name + `#BidId`), coverage row at the bottom.
  - Each cell is a `<button>` with `aria-label="{supplier}, {item}: {verdict}. {reason}"` and an icon (check / triangle / x / question) so colour is not the only signal.
  - Detail panel under the table showing supplier · item · verdict, reason, and the evidence quote; empty state "Select a cell to see why."
  - Empty state when never run: short explanation + the run button.
- `wwwroot/js/spec-match.js` — hover shows a tooltip; click/Enter pins the detail panel; arrow keys move between cells; button busy state ("Matching…") on submit.
- `wwwroot/css/spec-match.css` — green/amber/red/grey cell tokens with light and dark mode (`[data-theme="dark"]`, see `wwwroot/css/ai-log.css` for the pattern); horizontal scroll on narrow screens with `position: relative` on the scroll wrapper (an absolutely positioned visually-hidden label otherwise overflows the page — known issue in this project); no page-level horizontal scroll at 375 px.
- Include the CSS/JS via the view's `Styles`/`Scripts` sections with `asp-append-version="true"`.

---

## 7. Tests — `Checks/SpecMatchChecks.cs`

Register `await SpecMatchChecks.RunAsync();` in `Checks/VllmAiChecks.cs` → `RunAsync()` next to `OfferPreCheckChecks`. Use a fake `HttpMessageHandler` returning OpenAI chat-format responses (`choices[0].message.content`), as the existing checks do, and `VllmAiChecks.LocalFixtureConfiguration()`.

Required cases:
1. Item not offered → `Missing` even when the fake AI says `match`.
2. Short quantity → `Partial` from code, not sent to the AI.
3. Evidence not present in the offer → `NeedsCheck`.
4. AI omits a candidate pair → `NotAssessed`.
5. Unknown bid/item IDs from the AI are ignored.
6. Handler returns 429 / throws → code-only result, `AiAvailable == false`, no exception.
7. Changing an offer's `OfferedSpecifications` changes `Fingerprint`.
8. The request body contains `<untrusted_data` around supplier text (parse the request JSON, don't string-match escaped text).
9. The call is logged with feature `evaluation.spec-match` (use `InMemoryAiInteractionStore`, see `Checks/AiGatewayChecks.cs`).

Run: stop the app (it locks `bin`), then `dotnet build` and `dotnet run --no-build -- --vllm-ai-checks`. All existing PASS lines must still pass.

---

## 8. Manual verification

1. Restart the app on https://localhost:7130 (`dotnet run --launch-profile https`). Confirm it returns 200 before reporting done.
2. Log in as `eval1@tiryaq.com` (test password is in the user's local notes; ask the user if unknown). Open `/EvaluationCommittee/Evaluations/41` (6 offers, 2 items) and `/40`.
3. Click "Run spec match"; check colours, hover tooltip, click detail, coverage row, dark mode, and 375 px width.
4. Check Admin → AI log shows an `evaluation.spec-match` entry.
5. If Groq returns 429, confirm the code-only grid and the "AI matching unavailable" note appear.

Demo data scripts live in `Data/Seed/` (`demo-evaluation-tenders.sql`, `demo-extra-offers-tender-40.sql`, `demo-complete-evaluation-tender-41.sql`); run with `sqlcmd ... -E -I -i <file>`.

---

## 9. Out of scope

- Feeding coverage into marks, ranking, winner selection or reports.
- Other roles (Sourcing Officer / Manager) — can be added later by reusing the partial.
- Image or attachment reading (offer documents); only saved offer text is compared.

## 10. Done means

- Migration applied, service + controller + partial + JS/CSS in place, `dotnet build` with 0 errors/warnings.
- `SpecMatchChecks` pass along with every existing check.
- Verified in the browser on tenders 40 and 41, including the AI-unavailable fallback.
- Report back with what was verified and what was not; do not commit unless asked.
