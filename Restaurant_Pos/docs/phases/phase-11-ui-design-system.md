# Phase 11 — UI Design System

Status: In progress (started 2026-10-14) · Depends on: Phase 10 · Reference: BillingHub (`Billing-Application`)

## 1. Goal

Give HotelPOS the look and working style of BillingHub, the owner's GST billing application: its design system
(MaterialDesign base, zinc neutrals with a blue accent, Inter typography, spacing/radius tokens, Windows 11 Mica,
quiet motion, light and dark themes), its shell (collapsible categorised sidebar, top bar, Ctrl+K command palette,
tabbed hubs) and its building blocks (KPI tiles, report filter bar, empty states, busy overlay, charts) — while
keeping what a restaurant needs that a billing desk does not: large touch targets on waiter tablets and kitchen
screens, colour-coded table and ticket states, and real-time updates.

## 2. Prerequisites

- Phase 10 (desktop modules, rendering test harness). Decision of 2026-10-14: adopt BillingHub's design system,
  keep HotelPOS's multi-terminal architecture.

## 3. Scope

**In**
- MaterialDesignThemes 5.1 as the control base (`MaterialDesign2.Defaults`) and its icon set (`PackIcon`).
- Design tokens ported from BillingHub: neutral ramp (`Neutral.0`–`950`), accent blue `#2563EB`, success, warning,
  error, purple/teal accents, surfaces/layers, text tiers, borders, focus ring, shadows; typography scale
  (11/13/14/16/22/32/44), radii (input 6, button 8, card 12). Restaurant status colours (table and ticket states)
  become semantic tokens in the same palette, identical in meaning to today.
- Light and dark themes swapped at runtime; the choice is remembered per terminal.
- Windows 11 Mica backdrop where supported, plain surface elsewhere.
- Motion: 160 ms fade + 8 px slide on page change, animated sidebar collapse; off when Windows animations are off.
- Density per terminal type: **Comfort** (touch: waiter, kitchen; 48 px minimum targets) and **Compact** (desk:
  billing, admin), chosen from the terminal's device type, switchable in This Terminal.
- Shell: collapsible sidebar with category groups and icons, user card with sign-out, top bar (page title, search,
  theme toggle, notices bell with the session's server notices, help), status bar (connection, server, terminal,
  version).
- Command palette (Ctrl+K): every permitted module, actions (new order, open table by code, find bill or order by
  number, backup now, sign out), keyboard navigation.
- Hubs (tabbed pages) for areas that today have their own tab code: Menu, Floor, Billing setup, Settings.
- Building blocks: `BusyOverlay`, `EmptyState`, `KpiTile`/`KpiStrip`, `HubControl`, `ReportFilterBar` (date presets
  incl. business day, search), `PageHeader`.
- Dashboard: KPI strip and charts (7-day sales trend, payment-mode split, top items) with LiveChartsCore.
- Indian formatting everywhere: `₹1,23,456.00` grouping and `dd-MM-yyyy` dates via one culture setup.
- Keyboard shortcuts (desk terminals): Ctrl+K palette, Ctrl+D dashboard, Ctrl+T tables, Ctrl+B billing, Ctrl+R
  reports, F1 help, plus the existing cashier keys (F2/F3/F4/F9).
- Every existing screen restyled; no screen keeps the old look.

**Out**
- Server changes (Phase 12 onwards). New business features (Phases 13–16). Per-field inline validation rework.

## 4. Deliverables by project

| Project | Deliverables |
|---|---|
| Desktop | `Themes/Tokens.Light.xaml`, `Tokens.Dark.xaml`, `DesignSystem.xaml` (all styles; today's style keys kept as the public names so views migrate without churn), `ThemeService` (swap + persist + Mica), `Motion`, `Density`, controls listed in § 3, `CommandPaletteViewModel/View`, shell rewrite, dashboard charts, culture setup |
| Tests | Rendering test covers every screen in light **and** dark, both densities on two representative screens; zero binding errors; palette search tests; formatting tests |

## 5. Data model

None. Theme and density are per-terminal client settings (`ClientSettings.Theme`, new `Density`).

## 6. API endpoints

None new. The palette's "find" actions use existing endpoints (`/api/tables`, `/api/orders`, `/api/billing`).

## 7. Real-time events

None new. The notices bell lists `ServerNotice` events received since sign-in.

## 8. Business rules

- Touch terminals never fall below 48 × 48 px targets; primary actions 56 px (unchanged rule from `docs/05` § 9).
- Status colours keep their meaning on every screen and in both themes; contrast at least WCAG AA (4.5:1 text).
- The palette shows only modules and actions the user's permissions allow.
- Money is never formatted with a different grouping on different screens.

## 9. Desktop screens

All screens are restyled. New: command palette, notices panel, hub pages. Changed: shell, dashboard (charts).

## 10. Tests

- `ScreenRenderingTests` renders every screen in light and dark; binding errors fail the test.
- `CommandPaletteViewModel`: permission filtering, fuzzy matching, action execution.
- Formatting: Indian grouping and dates for amounts up to crores.

## 11. Manual demo script

1. Open the app on a desk terminal: new shell, sidebar collapses, Ctrl+K finds "Reports" and a table by code.
2. Toggle dark mode; restart: the choice is kept.
3. Waiter tablet (Comfort density): table map and order builder targets ≥ 48 px; kitchen display readable at 2 m.
4. Dashboard shows KPI strip and charts; settle a bill elsewhere: tiles and charts update.

## 12. Acceptance criteria

- [ ] Every screen uses the new design system in both themes; no old-style screen left.
- [ ] Command palette, hubs, notices panel and new dashboard working.
- [ ] Touch sizes and status colours preserved; rendering test green with zero binding errors.
- [ ] Definition of Done satisfied.

## 13. Risks and notes

- MaterialDesign's implicit styles restyle every control: views that relied on WPF defaults must be checked in the
  rendering screenshots, not only for binding errors.
- LiveChartsCore 2 is a release candidate; keep the chart area replaceable (charts are a view concern only).
- Inter is bundled only if its font files are in the repository (OFL licence); otherwise the stack falls back to
  Segoe UI Variable, which ships with Windows 11.

## 14. Changes during implementation

(fill in while building)
