# Committee scoring workflow

Each of the four assigned evaluation members confirms a financial percentage between 0.01 and 99.99. Technical is the remainder to 100. Confirmation locks that member's proposal. The fourth confirmation freezes the arithmetic average, rounded to two decimal places using midpoint rounding away from zero. The stored financial maximum and its complementary technical maximum apply to all offers and to AI comparison.

Every member enters both financial and technical marks for every compliant offer. Marks must be explicitly supplied, nonnegative, within their respective maxima, and have at most two decimal places. Zero is a valid mark. Accepting a form locks it. Acceptance counters count valid locked forms belonging to the four assigned members, not legacy tender-wide approvals.

After every offer has four accepted forms, each offer's result is the average of its four financial-plus-technical totals. Average results can have four decimal places; the comparison table and score report retain that precision. The result is not weighted a second time. Different currencies, inactive committee membership and unresolved relevant clarifications block acceptance.

A unique highest score automatically creates the committee report, records the four aggregate evaluation approvals and moves the tender to `AwaitingOfficerSummary`. The sourcing officer still reviews the result; scoring does not award the procurement. No AI call is needed for progression. AI comparison is advisory and cannot change the score winner or accept posted percentages that differ from consensus.

Equal highest scores pause progression. Members explicitly choose among the tied highest offers. All four must agree on the same offer before progression. A member can revise their tie choice while agreement is outstanding, but cannot change their accepted marks or confirmed percentage.

An explicit sourcing officer or manager return reopens the offer forms for fresh review. Previous marks remain as draft input, but no longer count as accepted; all members must review and accept again. The percentage remains frozen, and prior aggregate approvals and tie decisions are cleared.

Writes use the existing tender row lock and a version covering proposals, offer facts, marks, approvals and tie decisions. Only the authenticated assigned member's own proposal, score form or tie choice can be submitted. Old manual selection, shared-weight editing and bulk approval routes reject writes.

## Database and existing data

`CommitteeConsensusAndOfferScoring` adds nullable consensus/mark columns and separate proposal/tie tables. Apply with `dotnet ef database update` using the app's configured connection. Completed historical evaluations retain their original data and display their original report and approvals. Active evaluations start the new confirmation and scoring steps; old bulk approvals do not automatically become marks or proposals. Existing accounts, passwords and procurement records are retained.

## Verification

- `CommitteeScoringChecks.Run()` covers averages, bounds, locks, all-offer gating, ranking, ties, rounding and legacy approval exclusion.
- `python Checks/committee-scoring-checks.py` runs actual MVC requests, antiforgery and identity checks, concurrent duplicate submissions, PDF export and tie progression against a task-created random SQL database. It drops only that database when finished. It never runs production startup, resets the user's database or contacts a real AI service.
- Add `--serve` for the isolated synthetic browser preview; use the token-protected `/qa/stop` endpoint to stop it and clean up the test database.
- The rebuilt interface uses the apple-design skill: system typography, explicit state feedback, responsive forms, visible focus and reduced-motion support. It keeps the Tiryaq palette.

The implementation was verified locally with the calculation and historical ranking checks, 92 actual MVC checks (including the disabled scoring gate, per-offer acceptance headers, officer and manager returns, concurrent confirmations, and real PDF export), and light/dark desktop and narrow mobile rendering. The additive migration was applied after a verified SQL Server backup. Test databases were dropped on completion. Live Ollama generation was not exercised; the tests enforce zero external AI calls while verifying posted-weight tampering is rejected.

The normal Debug application on port 7130 was rebuilt and restarted after finding that it still served the old compiled Razor page. Live tender #149 was checked in the user's existing browser: the four-member proposal card is visible, Evaluation forms is disabled before consensus, both AI fields are read-only, each offer displays its acceptance counter above the supplier information, and there are no manual selection controls.
