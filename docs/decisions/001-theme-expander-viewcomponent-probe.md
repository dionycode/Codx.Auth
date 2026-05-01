# ADR-001: Extend ThemeViewLocationExpander to Resolve ViewComponent Overrides

**Status**: Proposed  
**Date**: 2026-05-01

## Decision

Add a third probe path to `ThemeViewLocationExpander.ExpandViewLocations` so that ViewComponent default views can be overridden per-theme, enabling the `AdminNavigation` component (and any future ViewComponents) to have a Tailwind-specific render without touching the default Bootstrap component view.

## Context

ASP.NET Core's ViewComponent default view resolution looks in:

- `/Views/{controller}/Components/{component}/Default.cshtml`
- `/Views/Shared/Components/{component}/Default.cshtml`

`ThemeViewLocationExpander` currently prepends only controller-view and shared-view paths. It does not prepend a `Shared/Components` probe path, so placing `Themes/Tailwind/Views/Shared/Components/AdminNavigation/Default.cshtml` will never be found — the Bootstrap version is always served.

The alternative of putting theme-switching `@if` logic inside the existing `Views/Shared/Components/AdminNavigation/Default.cshtml` would pollute the default Bootstrap component with Tailwind concerns and violates the separation-of-concerns goal of the theme override system.

## Consequences

- **Positive:** Any ViewComponent can have a per-theme override with zero changes to the component's C# class or the default Bootstrap view. Consistent with the existing theme override philosophy.
- **Positive:** The `AdminNavigation` component renders Alpine.js dropdowns when Tailwind is active and Bootstrap dropdowns when it is not, purely through file presence.
- **Negative:** A small change to `Infrastructure/Theming/ThemeViewLocationExpander.cs` is required. This is a single additional line in `ExpandViewLocations` and carries no business-logic risk.
- **Neutral:** The probe path format mirrors the ASP.NET Core default ViewComponent path, just rooted under `Themes/{name}/` instead of the application root.
