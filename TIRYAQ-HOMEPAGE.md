# Tiryaq homepage and brand colors — handoff (16 Nov 2026)

## Homepage
`/` is the production-line journey: one bottle, lot TQ-0417, through formulation, filling, capping, labelling, inspection and packaging, ending at home. A canvas scene is driven by scroll.
- `Views/Home/Index.cshtml` holds the markup, cards and Razor links. It uses its own nav (`HidePublicNavigation`), and that nav keeps Login, Supplier registration, the dashboard, the notification bell and Sign out.
- `wwwroot/css/tiryaq-journey.css` holds the styles. Classes are prefixed `tj-` to avoid Bootstrap.
- `wwwroot/js/tiryaq-journey.js` holds the scene and timeline. Station timings live in `heroPath`, `CAM` and the `data-in`/`data-out` beats on the cards.

## Brand colors (whole site)
- Primary teal `#1d5560` (hover `#174750`, deep `#0c252d`) is used for sidebars, links, focus, selection and outlined buttons.
- Secondary honey `#efb85c` (hover `#e5a23e`, deep `#d98a2b`) is used for main buttons (`.btn-primary` and the known call-to-action classes), active-item markers, and accents on dark surfaces.
- `wwwroot/css/brand-theme.css` is loaded last in `_Layout` and enforces the theme. Its selectors use `body:not(#tq-x)` so they win over page rules that are scoped by ID.
- Legacy brand hex values in `wwwroot/css/*.css` and `wwwroot/js/login-scene.js` were mapped onto these two colors. Status colors (success, warning, danger) and greys were left as they were.

## Not verified
- The signed-in workspace pages and sidebars (no sign-in was used), and every role's dashboard charts.
- Dark mode, apart from the homepage.
- Nothing is committed. The work sits on a detached HEAD at `c2f3752`, so create a branch before committing.
