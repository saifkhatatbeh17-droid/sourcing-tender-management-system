# Tender selection from a list

Approved direction: the user chose a tender list before entering the offers workspace.

Opening Supplier offers without a tender shows the officer's tenders, with search by title, number, category and status. Each row shows the tender title, number, status and UTC deadline and links to its existing offers workspace. The workspace provides Back to tenders instead of a picker modal. Preserve direct tender and bid links, ownership checks and procurement actions.

## Approaches

A searchable selector above the workspace would allow switching in place; the user declined it. The selected approach is a separate list as the starting point, so choosing a tender is an explicit step.

Use existing MVC views, styles and search behavior. No new dependencies or schema changes.
