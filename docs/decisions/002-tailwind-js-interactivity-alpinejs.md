# ADR-002: Use Alpine.js as the JavaScript Interactivity Layer for the Tailwind Theme

**Status**: Proposed  
**Date**: 2026-05-01

## Decision

Use Alpine.js v3 as the sole JavaScript interactivity layer for the Tailwind theme, bundled into `wwwroot/themes/tailwind/site.js`. Bootstrap JS and jQuery are not loaded by the Tailwind layout; jQuery is retained only to satisfy ASP.NET Core's `jquery-validation-unobtrusive` dependency.

## Context

The Bootstrap 4 theme relies on Bootstrap's JavaScript for five interaction types: top-bar dropdowns, the mobile nav toggle, tab panel switching, dismissible alerts, and modal dialogs. Removing Bootstrap JS requires a replacement that:

1. Can be declared inline in Razor `.cshtml` files without a build step per-view
2. Handles DOM state (open/closed, active tab) without global JavaScript variables
3. Supports focus trapping in modals and Escape-key dismissal
4. Is small enough not to offset the performance gain from removing Bootstrap JS (~30 KB min+gzip)

**Alpine.js v3** satisfies all four requirements:

- `x-data`, `x-show`, `:class`, `@click`, `@keydown.escape`, and `x-trap` (Alpine Plugin: Focus) cover every required pattern
- Declarative HTML attributes mean Razor views remain readable without embedded `<script>` blocks
- ~15 KB minified + gzip
- No build step — the `alpine.min.js` bundle (plus the Focus plugin for modal focus trapping) can be compiled once into `site.js`

**Vanilla JS** was considered but rejected: building reusable tab managers, focus traps, and `click-outside` handlers in imperative vanilla JS introduces more custom code to maintain than Alpine's 15 KB runtime cost justifies.

**htmx** was considered but is inappropriate here — the interaction patterns are purely client-side state (open/closed menus, active tabs) rather than server-driven partial HTML replacement.

## Consequences

- **Positive:** Dropdowns, tabs, modals, and drawers are implemented with `x-data` blocks directly in `.cshtml` files — no separate `.js` files per component.
- **Positive:** The Focus plugin (`@alpinejs/focus`) provides `x-trap` for modal focus trapping with zero custom code.
- **Positive:** Alpine.js does not conflict with jQuery; both can coexist in the same page for the validation use case.
- **Negative:** Alpine.js is a new runtime dependency. The team must learn the `x-data` / `x-show` / `:class` API. The learning curve is shallow (one afternoon).
- **Negative:** Alpine.js `x-trap` requires the `@alpinejs/focus` plugin to be explicitly bundled into `site.js`.
- **Neutral:** The Bootstrap JS files remain in `wwwroot/lib/bootstrap/` because they are still needed by the Bootstrap default theme. They are simply not loaded by the Tailwind layout.
