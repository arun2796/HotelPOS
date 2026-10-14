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
- Inter is bundled as static TTFs (OFL licence, `Resources/Fonts`); Segoe UI Variable / Segoe UI are the fallbacks.

## 14. Changes during implementation

### 11.1 Foundation (2026-10-14)

Built: `Themes/Tokens.Light.xaml`, `Tokens.Dark.xaml` (same keys; replace `Colors.*.xaml`),
`Density.Comfort.xaml`, `Density.Compact.xaml`, `DesignSystem.xaml` (replaces `Styles.xaml`; every old style key
kept, so all 47 screens took the new look without view changes), Inter Regular/Medium/SemiBold/Bold in
`Resources/Fonts`, `ThemeService` (theme + density + MaterialDesign base theme), `WindowBackdrop` (dark title bar,
Mica), `TransitioningContentControl` (page motion), `Startup/IndianCulture`, `ClientSettings.Density` with a
"Display density" choice on This Terminal. Packages: MaterialDesignThemes 5.1.0, MaterialDesignColors 3.1.0.

Decisions and deviations:
- **MaterialDesign palette is defined** (`CustomColorTheme`, accent `#2563EB`), unlike BillingHub, which merges only
  the defaults and so renders MaterialDesign checkboxes invisible. An empty implicit `ScrollViewer` style replaces
  MaterialDesign's, so it does not pad and re-template our scroll areas.
- **Cards are flat** (border, no shadow). A `DropShadowEffect` rasterises everything inside it, which blurs text and
  costs frame time on low-end tablets with many tickets or tables. Only floating layers (dialog, toasts, dropdowns)
  have a shadow, and it is drawn by a separate layer behind the content.
- **Status colours** keep their meaning and hue, with darker shades where white text failed WCAG AA: Available
  `#047857`, Preparing `#B45309`, Billing `#0F766E`. They are identical in both themes.
- **Dark theme**: fills that carry white text (primary `#2E6FF0`, danger `#E11D48`) stay dark enough for AA. Text-only
  colours get their own lighter tokens (`Brush.Link`, `Brush.DangerText`).
- **Hover and press** draw a translucent overlay rather than a fixed colour, so view-local coloured buttons (pay,
  ticket, table tiles) get the same feedback. The focus ring is a `FocusVisualStyle`, so it shows only for keyboard
  navigation, never after a tap.
- **Placeholders** use `md:HintAssist.Hint` through `TemplateBinding`; a `Binding` path with the `md:` prefix does
  not resolve inside dictionary templates (the rendering test caught the binding error).
- **Comfort controls are 48 px** (the old theme used 44 px everywhere, below the `docs/05` § 9 rule).
- **Mica** changes the window frame only; content keeps solid token surfaces, so contrast does not depend on the
  wallpaper.
- **Culture** is applied at startup, not in the test process; `Money.Format` follows it.

Fixed on the way:
- TextBox and PasswordBox padding was applied twice (by the template and by WPF's text host), so text started
  about 28 px in; it now starts 16 px in.
- `Brush.SurfaceSubtle` was used by three views but never defined; it is now a token.

Tests: the rendering test now checks that Inter backs all four weights and renders waiter and kitchen screens in
Comfort. New `IndianCultureTests` (grouping up to crores, parsing, dates) and `DensityTests` (automatic and
chosen density; changing it never signs the user out). Suite: 733 passed, 1 skipped (+16).

Left for later steps: report grids and totals format amounts without `Money.Format` (no grouping), to fix with the
report filter bar (11.3); the Users command bar clips "Log out everywhere" at 1366 px when the edit panel is open
(11.3 command bars); sidebar collapse, top bar and command palette (11.2).

### 11.2 Shell (2026-10-14)

Built:
- **Sidebar** collapses to icons (72 px; group headings become dividers, names become tooltips), remembered per
  terminal (`ClientSettings.SidebarCollapsed`). The width is set from state and animated only when the user toggles
  it, so a terminal that starts collapsed does not slide on start. The user card carries change-password and
  sign-out.
- **Top bar**: page icon, title and module description; a search field that opens the palette; clock; theme toggle;
  notices bell with an unread badge; help (F1).
- **Command palette** (`CommandPaletteViewModel`, Ctrl+K or the search field): the modules the user may open (the
  same permission-filtered list as the sidebar), shell actions (new order, back up now, theme, change password,
  shortcuts, sign out) and live results. A table code (`t5`, `T05`, `table 5`) opens that table; an order number
  (`1019`, `#1019`) opens its table; a bill number or the end of an invoice number opens the bill. Lookups load once
  per opening from the existing list endpoints, and only for modules the user has. Fuzzy ranking: title match first
  (prefix, word starts, consecutive letters), then search keywords per module (e.g. "gst" → Restaurant Settings).
  ↑/↓, Enter and Esc work as expected.
- **Notices** (`NoticeCenter`): `AppNavigator` keeps every server notice of the session (latest 50) next to its
  toast; the bell counts unread ones and the panel lists them; sign-out clears them.
- **Shortcuts** handled by `ShellViewModel.HandleShortcut` from the window's key stream: Ctrl+K, Ctrl+D/T/B/R (only
  when the user has that module), F1, Esc. The cashier keys (F2/F3/F4/F9) stay with the bill screen.

Decisions: "find bill" covers pending bills only; closed bills are searched on the Closed Bills screen. The palette
shows no server search beyond the three lists (no new endpoints in this phase).

Tests: `CommandPaletteTests` (permission filtering, ranking, table/order/bill lookups and their navigation, lookups
skipped without permission, keyboard wrap), `NoticeCenterTests`; the rendering test drives Ctrl+K/Esc and Ctrl+R/B
and adds screenshots 48–50 (palette, notices, collapsed sidebar). The rendering harness now uses explicit shutdown,
fixing an intermittent "Application object is being shut down" when the first test window closed. Desktop tests:
186 passed, 1 skipped.
