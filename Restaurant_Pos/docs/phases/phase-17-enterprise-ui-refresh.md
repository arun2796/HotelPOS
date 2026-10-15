# Phase 17 — Enterprise UI Refresh

Status: Not started (specified 2026-10-15) · Depends on: Phase 11 (design system), Phase 16 (all screens exist) ·
References: `docs/07-menus-screens-and-fields.md` (functional source of truth), owner's master prompt of 2026-10-15,
reference dashboard image (visual inspiration only, not a screen specification)

## 1. Goal

Restyle the whole desktop application into a light, restrained, enterprise restaurant-POS look — green primary,
white surfaces on a cool grey background, compact sidebar, food-image menu cards, strong numeric alignment on bills
— while changing **nothing** the application does. Every menu, screen, field, column, button, dialog, rule,
shortcut, permission and real-time behaviour listed in `docs/07` stays exactly as it is. Only the presentation layer
(`src/HotelPOS.Desktop`: `Themes/`, `Controls/`, `Shell/*.xaml`, `Modules/**/*.xaml`, and the minimum code-behind a
style needs) changes.

Phase 11 already moved every screen onto named tokens (`Brush.*`, spacing, radius, density) and shared styles, so most
of this phase is a token re-tune plus a layout pass per screen, not a rewrite.

## 2. Prerequisites

- Phase 11 tokens, `DesignSystem.xaml`, `Controls.xaml`, `Density.*.xaml`, `Controls/` building blocks.
- Phase 16 complete: 28 sidebar screens and the dialogs in `docs/07` exist; rendering test covers 73 screens.
- Compact menu already carries `ImageUrl` per item (used by Menu > Preview), so Order Builder cards can show pictures
  without an API change.
- Decisions in § 13 confirmed by the owner.

## 3. Scope

**In**
- New token values (light and dark), typography scale, spacing/radius constants.
- Shared styles: buttons (primary/secondary/ghost/danger, sizes, states), inputs, dropdowns, search box, filter bar,
  cards, data grid (header, row, hover, selection, numeric columns, pagination), tabs/segmented control, dialog frame,
  badges/status chips, tooltips and keyboard hints, empty/loading/error/offline/disabled states.
- Shell: sidebar, header, status bar, cash-shift bar, command palette, banners.
- Every screen and dialog in `docs/07`, in the order of § 9.
- 1920×1080, 1600×900 and 1366×768 layout pass at 100 % and 125 % Windows scaling.
- Keyboard focus visuals and tab order checks (no behaviour change).

**Out**
- Any change to view-models' commands, bindings, validation, services, Contracts, API, domain, database, SignalR,
  printing, licence, backup, update or import behaviour.
- New features, removed fields, reordered navigation, changed permissions or home screens.
- New icon library (keep Segoe Fluent Icons / Segoe MDL2); no new NuGet packages.
- Animations beyond the existing quiet transitions.

## 4. Deliverables by project

| Project | Deliverable |
|---|---|
| Desktop `Themes/` | `Tokens.Light.xaml`, `Tokens.Dark.xaml` re-tuned (§ 8.1); `DesignSystem.xaml` typography scale; `Controls.xaml` shared styles incl. new `DialogFrame`, `StatusChip`, `DataGrid.Enterprise`, `Pagination` styles |
| Desktop `Controls/` | Additions only where a style cannot do it: `StatusChip`, `SectionCard`, `NumericCell` text style helper; keep `KpiTile`, `PageHeader`, `ReportFilterBar`, `EmptyState`, `BusyOverlay` (restyled) |
| Desktop `Shell/` | `ShellView.xaml`, `MainWindow.xaml` layout and styles only |
| Desktop `Modules/**` | XAML of all 54 views restyled; view-models untouched (allowed exceptions: a presentation-only property such as `HasImage` if a trigger needs it, recorded in § 14) |
| Desktop `Resources/` | Placeholder food-image tile (vector, neutral) for items without a picture |
| Tests `HotelPOS.Desktop.Tests` | Rendering test still 73 screens, zero binding errors; token-contract test (every `Brush.*` key present in both theme files); screenshot sizes measured, not eyeballed |
| Docs | `docs/05-desktop-application.md` § design system updated; § 14 of this document per step |

## 5. Data model

None.

## 6. API endpoints

None.

## 7. Real-time events

None. Existing events keep driving the same screens.

## 8. Business rules

None change. Presentation rules this phase adds:

### 8.1 Tokens (old → new, light theme)

| Token | Phase 11 | Phase 17 |
|---|---|---|
| `Brush.Background` | `#FAFAFA` | `#F4F7FA` |
| `Brush.Surface` / `SurfaceAlt` / `SurfaceSubtle` | `#FFFFFF` / `#F7F7F8` / `#F4F4F5` | `#FFFFFF` / `#F8FAFC` / `#F1F5F9` |
| `Brush.Border` / `BorderSubtle` / `BorderStrong` | `#E4E4E7` / `#EFF0F2` / `#D4D4D8` | `#E3E8EF` / `#EEF2F6` / `#CBD5E1` |
| `Brush.TextPrimary` / `TextSecondary` / `TextTertiary` | `#09090B` / `#52525B` / `#A1A1AA` | `#172033` / `#667085` / `#98A2B3` |
| `Brush.Primary` / `PrimaryHover` / `PrimarySoft` / `Focus` / `Link` | blue `#2563EB` family | green `#078B45` / `#06763B` / `#E9F8EF` / `#078B45` / `#078B45` |
| `Brush.Info` / `InfoSoft` | `#2563EB` / `#EFF6FF` | `#2563EB` / `#EFF6FF` (unchanged) |
| `Brush.Success` / `SuccessSoft` | `#059669` / `#ECFDF5` | `#16A34A` / `#E9F8EF` |
| `Brush.Warning` / `WarningSoft` | `#D97706` / `#FFFBEB` | `#F59E0B` / `#FFF7E6` |
| `Brush.Danger` / `DangerText` / `DangerSoft` | `#DC2626` / `#B91C1C` / `#FEF2F2` | unchanged |
| `Brush.Sidebar` … `SidebarDivider` | dark sidebar `#0A0A0B` family | light sidebar: `#FFFFFF` bg, text `#344054`, active text `#078B45`, muted `#98A2B3`, hover `#F1F5F9`, selected `#E9F8EF`, accent `#078B45`, divider `#E3E8EF` |
| `Brush.Chart.0..5` | blue-led | `#078B45`, `#2563EB`, `#F59E0B`, `#7C3AED`, `#0D9488`, `#E11D48` |
| `Brush.Status.*` (table/ticket states) | see `docs/07` legend | **unchanged hues** (available green, occupied blue, ordering purple, preparing amber, ready red, billing teal, out-of-service grey); only soft backgrounds added for chips |

Dark theme: same keys, derived counterparts (background `#0F1623`, surface `#161F2E`, border `#273246`, text
`#E6EAF0` / `#A3ACBA`, primary `#22B45F` with hover `#1DA354`, soft `#123A26`; sidebar surface-coloured). Both files
must define the identical key set (new test).

Typography (`DesignSystem.xaml`): `Font.Body` = `Segoe UI Variable Text, Segoe UI, Inter(bundled fallback)`;
scale: screen title 24, section title 16, body 14, table 13, secondary 12, KPI value 28, numeric columns use tabular
figures. Spacing stays on the 4 px tokens; corner radius tokens 6 (controls) / 8 (cards) / 10 (dialogs).

### 8.2 Layout rules

- Sidebar 240 px expanded / 64 px collapsed, light, groups in the fixed order Service, Kitchen, Billing, Management,
  Administration; active item = soft green fill + green text + 3 px left accent; user block at the bottom.
- Header 56 px: toggle, title + description, search box (placeholder keeps "Ctrl K"), clock, theme, bell, help, user.
- Status bar 28 px; banners (licence, update, failed prints) are full-width strips under the header, never overlays.
- Data screens = `PageHeader` → filter bar → grid → footer (summary + pagination). Numeric columns right-aligned.
- Dialogs use one `DialogFrame`: title, optional subtitle, body, validation message row, actions right-aligned
  (primary last). Destructive primary is red.
- Touch screens (Tables, Order Builder, Kitchen, Bill, Shift count) keep Phase 11 Comfort-density minimum 44 px
  targets; Compact density unchanged for mouse screens.
- Menu item card: 4:3 image (placeholder when none), name (2 lines max), price, veg/non-veg or availability badge
  only if the data already exists, "+ options" hint, SOLD OUT overlay. Cart panel fixed 380 px on the right.

## 9. Desktop screens (build order)

Each step is one commit, rendering test green, 2–3 measured screenshots reviewed.

| Step | Screens (docs/07 §) | Files |
|---|---|---|
| 17.1 Foundation | tokens, typography, shared styles, dialog frame, chips, grid, pagination, states | `Themes/*`, `Controls/*` |
| 17.2 Shell | sidebar, header, status bar, cash-shift bar, palette, banners (§ 2.5–2.7) | `Shell/*.xaml` |
| 17.3 Service | Tables + details panel, Order Builder + options pop-up, My Orders (§ 3) | `Modules/Tables`, `Modules/Orders` |
| 17.4 Kitchen | Kitchen Display + sold-out panel, Completed Orders (§ 4) | `Modules/Kitchen` |
| 17.5 Billing | queue, bill, discount, customer picker, payment/split, PAID, invoice preview, approval, Closed Bills, refund, Customers (§ 5.1–5.3) | `Modules/Billing`, `Modules/Customers` |
| 17.6 Cash | Cash Shifts, count panel, pay in/out, shift report, Day End steps, Day End History (§ 5.4–5.6) | `Modules/Cash` |
| 17.7 Dashboard | tiles, 7-day chart, payments split, top items; own-figures view (§ 3.1) | `Modules/Dashboard` |
| 17.8 Management | Reports, Tax Returns, Period Close, Import wizard, Sections & Tables, Menu (6 tabs + editors), Discounts & Payments, Audit Log (§ 6) | `Modules/Reports`, `Gst`, `Licensing/Import*`, `Admin/Floor*`, `MenuAdmin`, `Billing/BillingSetup`, `Admin/AuditLogs` |
| 17.9 Administration | Users + editor, Roles, Devices, Licence, Settings + Template Studio, Backup, Server, Printers, This Terminal (§ 7) | `Modules/Admin`, `Licensing`, `Printing`, `Config` |
| 17.10 Auth + polish | first run, sign-in, update required, change password (§ 2.1–2.4); 1366×768 and 125 % pass; dark theme pass; docs | `Modules/Auth`, all |

## 10. Tests

- Existing desktop tests (245 + 1 skipped) unchanged and green after every step — they are the proof that commands,
  bindings and validation did not move.
- Rendering test: 73 screens in light and dark, zero binding errors, zero `BG1002`-masked compile errors.
- New `ThemeTokenContractTests`: every key in `Tokens.Light.xaml` exists in `Tokens.Dark.xaml` and vice versa; no
  hard-coded `#RRGGBB` colours in `Modules/**/*.xaml` outside an allow-list (status legends).
- Screenshot checks are measured with pixel probes (touch target ≥ 44 px in Comfort, sidebar widths, header height),
  not judged by eye.
- Full `dotnet test HotelPOS.sln` once at the end.

## 11. Manual demo script

Two machines (billing PC + waiter tablet), light then dark theme:
1. Sign in as each role; confirm the sidebar groups, order and visible screens match `docs/07` Appendix A.
2. Waiter: occupy a table, build an order with pictures, options and a note, send to kitchen.
3. Kitchen terminal: accept, start, ready; confirm amber/red timing colours still appear.
4. Cashier: open the bill, apply a discount above the limit (approval pop-up), pick a customer, split-pay, print.
5. Manager: X report, close shift with a variance, run Day End to step 4.
6. Admin: edit a setting, a role, a menu item picture; check the Template Studio preview.
7. Resize to 1366×768 and set Windows scaling to 125 %: no clipping on Bill, Kitchen, Order Builder, Settings.
8. Pull the network cable on the tablet: offline banner and queued items still behave as before.

## 12. Acceptance criteria

- [ ] All 28 screens and every dialog in `docs/07` present, same roles, same fields, columns, buttons and messages.
- [ ] Desktop test count unchanged or higher; rendering test 73 screens, zero binding errors, both themes.
- [ ] No change outside `src/HotelPOS.Desktop` presentation files except documented § 14 exceptions.
- [ ] Token contract test green; no stray hard-coded colours.
- [ ] 1920×1080, 1600×900, 1366×768 at 100 % and 125 %: no clipped or overlapping controls on the step-11 screens.
- [ ] Status colours for tables and tickets unchanged in meaning.
- [ ] Definition of Done satisfied; manual demo run.

## 13. Risks and notes (owner decisions marked ►)

- ► **Reference image colours vs existing status legend.** The image uses red for occupied and blue for reserved;
  HotelPOS has no "reserved" state and uses blue for occupied, red for ready. Spec keeps the existing meanings
  (§ 8.1). Confirm.
- ► **Font.** Prompt says Segoe UI; Inter is bundled. Spec uses Segoe UI Variable with Inter as fallback. Confirm.
- ► **Sidebar.** Phase 11 sidebar is dark; the new look is light. Dark theme gets a surface-coloured sidebar.
- ► **Veg / non-veg badge.** The image shows it; the menu item has no such field today. It is **out of scope**
  (would need a data change). Confirm or defer to a later phase.
- Food images: items without a picture show a neutral placeholder; large menus load thumbnails lazily (existing
  image cache from Phase 11 preview is reused).
- `BG1002` build glitch and locked `bin` folders: build with `-p:OutDir`, see `docs/dev-setup.md`.
- Token budget: work is done step by step on one model; screenshots limited to 2–3 per step; full suite once.
- Rollback: every step is one commit on `phase-17-enterprise-ui-refresh`; reverting a step reverts its screens only.

## 14. Changes during implementation

### 17.1 Foundation (2026-10-15)

- `Tokens.Light.xaml` / `Tokens.Dark.xaml`: values re-tuned exactly as § 8.1 (green primary, cool greys, light
  sidebar; dark counterparts). Same key set as before — no view had to change for the palette.
- `DesignSystem.xaml`: `Font.Body` now Segoe UI Variable → Segoe UI → bundled Inter; `Radius.Card` 12 → 8; new
  `Radius.Dialog` 10; `Text.Title` 22 → 24; `Text.Display` 32 → 28 (KPI values).
- `Controls.xaml`: new shared pieces `Dialog.Frame`, `Chip.Base/Success/Warning/Danger/Info/Primary`,
  `Text.Numeric` (right-aligned tabular digits), `Card.Section`. Not yet applied to screens; later steps use them.
- Two literal `#FFFFFF` foregrounds (Kitchen ticket timer, Menu preview SOLD OUT) replaced by `Brush.PrimaryText`.
  The invoice preview in `BillDetailView` keeps its white-paper/dark-ink literals on purpose (allow-listed).
- New `ThemeTokenContractTests`: light and dark define the same keys; `Brush.Status.*` identical in both themes;
  no literal colours in `Modules/**` and `Shell/**` XAML outside the allow-list.
- `ScreenRenderingTests`: the body-font assertion now accepts Segoe UI Variable / Segoe UI / Inter files (it
  asserted Inter before). Still 73 screens, zero binding errors, both themes.
- Reviewed: Dashboard (light), Order Builder (light), Bill (dark). Order Builder cards get pictures in 17.3.
