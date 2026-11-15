# Committee evaluation rework

Use apple-design: system typography, restrained teal accents, a clear three-step workflow, quiet surfaces, immediate press feedback, responsive forms and reduced-motion support.

## Rules
- Four assigned members independently confirm a financial percentage; technical is its complement. Confirmation is immutable. The fourth confirmation locks the rounded average on the tender.
- Before consensus, scoring and AI comparison are unavailable. AI always uses server-stored consensus; posted weight values cannot override it.
- Every member gives every eligible offer a financial mark out of the financial weight and a technical mark out of the complementary weight. Accepting a form freezes those marks.
- Each offer displays its valid accepted form count out of four. No winner is selected until every offer has four accepted forms.
- Rank by the average of the four members' financial-plus-technical marks. Equal highest scores pause promotion; four explicit unanimous tie decisions are required to progress one of the tied offers. Original marks remain locked.
- The winner and a deterministic score report move automatically to the sourcing officer. AI remains advisory and cannot replace the scored winner.
- Completed historical records remain readable. Existing active evaluations must complete the new percentage and per-offer steps; old approvals do not count as scored forms.
- An explicit officer or manager return reopens offer reviews while retaining the frozen percentage; old tie decisions and aggregate approvals are cleared.

## Implementation and validation
1. Add weight proposals, frozen tender weights and direct per-offer marks through an EF migration; retain existing data and make a verified database backup before applying it.
2. Enforce member identity, limits, locks, clarification gates and concurrent writes using the existing tender transaction lock. Retire old manual selection/approval endpoints.
3. Rebuild the evaluation page with weight confirmations, per-offer member forms, acceptance counts, fixed AI fields and a score comparison/report.
4. Verify calculations and rejection paths in focused checks, exercise actual MVC endpoints with isolated SQL fixtures, build and inspect desktop/mobile rendering.

No external AI service is required to finish scoring. No database reset or automatic approval of real users' new forms.
