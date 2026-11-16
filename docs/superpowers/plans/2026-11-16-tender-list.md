# Tender List Implementation Plan

> **For agentic workers:** Use executing-plans to implement this plan task-by-task.

**Goal:** Start Supplier offers with an explicit tender list and open a workspace only after selection.

**Architecture:** Return the existing Offers view with no selected tender when no tender or bid identifier is supplied. Render a searchable list partial in that state and replace the workspace picker modal with a list link.

**Tech Stack:** ASP.NET Core MVC, Razor, existing CSS and JavaScript.

**Spec:** docs/superpowers/specs/2026-11-16-tender-list-design.md

## Global Constraints

Preserve ownership checks, direct tender and bid links and procurement actions. Add no dependencies or schema changes. Preserve unrelated local work.

## Review Focus

- No tenders: show the existing preparation prompt.
- Many tenders: search the list and show a no-results message.
- Long titles and narrow screens: wrap content and retain accessible links.
- Direct links: tender and bid identifiers still open the requested workspace.
- Unauthorized tender: retain the existing not-found response.

## Task 1: List and navigation

- [x] Update the Offers action to return the list state before loading workspace data.
- [x] Add _TenderList.cshtml and render it for the unselected state.
- [x] Replace the chooser popup with Back to tenders and remove its unused script.
- [x] Add scoped responsive styling using existing color variables and search hooks.
- [x] Build Razor and application code; verify list rendering, search, navigation, direct links and mobile layout where runtime access is available.

## Verification and review

Final project build passed with zero warnings and zero errors. Browser verification on port 5074: 22 list rows, one match for #160, no-results messaging, tender workspace navigation, direct bid navigation and return to list, unknown tender 404, and no horizontal overflow at 390px. Desktop and mobile screenshots were inspected. Existing no-tender prompt and ownership checks remain in place.

Fresh reviewer identified an ambient bid route ID retained by Back to tenders. Browser reproduction returned /SourcingOfficer/Offers/250 before the fix. Explicitly clearing route values changed it to /SourcingOfficer/Offers; navigation and the complete browser flow then passed. No deferred findings. Unrelated local work remains uncommitted.
