# Phase 21 — Role-based UI/UX redesign (plan)

Status: Planned — audit and plan only, nothing implemented (2026-10-17 on `main` a3f2676) · Depends on: Phases 17–20

Source: the owner's master prompt (`docs/design/master-prompt-role-ui-ux.md`) and six mock-ups
(`docs/design/reference/`). Three audits back this plan and hold the detail: `docs/design/design-system-plan.md`,
`docs/design/audit-web-waiter-kitchen.md`, `docs/design/audit-desktop-cashier-admin.md`. This document is the
summary, the decisions, and the ordered step list. The owner asked for the plan first; implementation starts only on
approval of § 4.

## 1. Goal

Make every role's client look and flow like its mock-up — waiter on the phone, kitchen on the tablet or monitor,
cashier and admin on Windows — with one design system across WPF and Blazor, while preserving every API, DTO, status
transition, calculation, permission and real-time behaviour (prompt § 2). Where a mock-up element needs data the
server does not expose, it is listed as a gap, not invented.

## 2. What the audits found

- **The palette is already shared.** Desktop `Tokens.Light/Dark.xaml` and web `tokens.css` carry the same Phase 17
  values, so moving to the prompt's palette is one token edit per side (green `#078B45` → `#078747`, page, text,
  border) plus a new navy sidebar set. Two real defects surfaced: table/kitchen "Ready" is **red** on both clients
  (the prompt says green) and the focus ring differs (green desktop, blue web). Inter is bundled on desktop but listed
  after Segoe UI, so it never shows; the web loads no font, so Android shows Roboto.
- **Kitchen is already close.** `KitchenPage.razor` has the three columns, Accept/Start/Ready/Done/Recall, the late
  border, the Delayed and Completed counts. It needs a compact header, a KPI strip and a proper ticket card.
- **Waiter works but looks generic.** Solid colour tiles, Unicode icons, no order timeline. Order-level notes exist in
  the API but the web never sends them. Mock-up elements such as "Reserved", "Held", order type, "Waiter · Main Floor"
  and language have no data behind them.
- **Cashier has the pieces, not the layout.** Queue, bill detail and payment (split tenders, quick amounts, change,
  idempotent submit) all exist, but payment is an overlay; the mock-up's three-panel workspace is the one large step.
  Order types (dine-in/takeaway/delivery), wallet and bill-splitting by item do not exist.
- **Admin is WPF; the prompt says browser.** All 30 Admin/Manager/Cashier modules are WPF; the Blazor client is a
  waiter/kitchen phone shell with no sidebar, grid, charts or admin API clients. The mock-up's look is reachable in WPF
  with a token swap, a regrouped sidebar and four new dashboard panels fed by existing endpoints. The hard gaps —
  hourly sales, expenses, profit & loss, floor-plan geometry, staff presence, inventory, invented trends — are data
  gaps a browser would not close.
- **Status words and colours are defined twice** (one `StatusLabels` per client). One shared map in Contracts fixes
  that for good.

## 3. Scope

**In:** design tokens and shared status map; waiter phone screens; kitchen tablet/monitor screens; cashier three-panel
workspace; admin navy shell, regrouped navigation and dashboard widgets; deep links; tests; regenerated galleries.

**Out (prompt § 3 or no data):** modifier *management*, recipes, inventory and low-stock, customers & loyalty as a
module, branch management; order types, wallet, item-level bill split, reservations/"Reserved", "Held" orders,
hourly sales, expenses, profit & loss, floor-plan geometry, staff presence, language switching, kitchen prices,
printing from Android WebView. Each is recorded in the audits with the exact missing field or endpoint.

## 4. Decisions needed from the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | **Where Admin/Manager lives.** The prompt says browser; today it is WPF. A) restyle WPF (17–22 agent-days, one code base); B) new Blazor admin (12–16 days for dashboard + reports only; 45–60 for parity, then two admin code bases); C) hybrid (24–28, two shells) | **A.** If a browser admin is still wanted later, do A first; its widgets and gap list carry over |
| D2 | **Modifier choice sheet on the phone.** Mock-up 6 shows add-ons/spice/instructions; the prompt forbids a "Modifiers & Add-ons interface". The app already lets waiters *choose* existing modifier groups | Keep and restyle the existing choice sheet; add no management screens (prompt § 2 preserve) |
| D3 | **Customers.** The desktop has a Customers screen and bill picker (B2B invoices need a GSTIN); the prompt excludes Customers & Loyalty | Hide the nav item; keep the bill-level customer panel; no web customer code exists |
| D4 | **Kitchen "Delayed".** Mock-up 2 shows it as a fourth column, duplicating tickets; no such status exists | KPI tile + red border + a "Delayed" filter that narrows the three columns; no fourth column |
| D5 | **Waiter bottom navigation.** Prompt: Tables / My Orders / New Order / Notifications / Profile. Mock-ups: Home / Tables / Orders / Menu / More. Today: Home / Tables / My orders / Settings. "New Order" has no table context | Home / Tables / Orders / New order (opens a pick-a-table sheet) / More (notifications, settings, profile) |
| D6 | **Payment method buttons** from the configured list (`/api/payment-methods`) instead of hard-wired Cash/Card/UPI | Yes — behaviour, not calculation, and matches "only configured methods" |
| D7 | **Pages the prompt's navigation omits** (Licence, Backup, Server, This terminal, Import, Audit, Day end, Period close, GST returns) cannot be dropped | New "System" group; Finance → GST Returns; Analytics → Tax Reports |
| D8 | **Ready turns green** (was red since Phase 17); Billing orange per the rule although the admin mock-up draws it blue | Green Ready; orange Billing; note in release notes |
| D9 | **"Edit order" / "Hold order" / "Print KOT" on the phone** | Edit = guests, notes, cancel a line (existing endpoints); Hold omitted; Print KOT only on desktop browsers, never Android |
| D10 | **"Shifts" under People** are cash shifts, not rotas; "Recent activity" = this session's bell feed or the audit log | Relabel "Cash shifts"; bell feed with "View all" to Audit |
| D11 | **Pictures.** Item and ticket pictures appear only once the owner uploads them; no stock photos | Owner uploads a demo set before the galleries are regenerated |

## 5. Deliverables by step (files in the audits; sizes S/M/L; 1 agent-day ≈ one Sonnet/Opus agent run)

### 21.0 Design system (shared, first) — `design-system-plan.md` § 6

| # | Step | Size |
|---|---|---|
| 0.1 | Tokens on both sides: palette, navy sidebar set, `*Soft/*Text` tones, neutral, font-size keys; `Status.*` re-pointed (Ready → success); stray literals (`App.xaml` MaterialDesign blue, `index.html` theme-color, Android `App.xaml`) | M |
| 0.2 | `Contracts/Presentation/StatusPresentation.cs`: every status enum → label + tone + icon; both `StatusLabels.cs` delegate | S |
| 0.3 | Inter: desktop `Font.Body` order; web self-hosted woff2 (offline LAN, Android) | S |
| 0.4 | Web primitives: `StatusBadge`, `KpiTile`, `Tabs`, `Segmented`, `SearchBox`, `FilterChip`, `OrderTimeline`, `DetailPane`, `OrderLine`, `Icon` (inline SVG replacing Unicode glyphs), `BottomSheet` | M |
| 0.5 | Desktop primitives: `StatusBadge`, `Pager`, `DetailPane`, `KpiTile` restyle, `Button.Warning/PrimaryOutline/Large`, soft `TableTile`, `Tabs`, `Segmented`, `SearchBox`, `OrderLine`, `PaymentPanel` styles, `StatusToneToBrush` | L |
| 0.6 | Desktop navy shell: grouped sidebar with badges and `DeviceStatusCard` footer, white top bar, auto-collapse below 1440 px | M |
| 0.7 | Web shells: waiter header + five-tab nav (D5); kitchen compact header, no bottom nav (K1) | M |
| 0.8 | Contract tests: palette pinned, WCAG ≥ 4.5 on both themes, web/desktop token parity (`WebTokenContractTests`), every enum member mapped (`StatusPresentationTests`) | S |

### 21.1 Waiter (web/Android) — `audit-web-waiter-kitchen.md` § 4

| # | Step | Size |
|---|---|---|
| W1 | Shell: header, five-tab nav, pick-a-table sheet for New order | M |
| W2 | Tables: pastel `TableCard` (code, status, guests, timer, order no.), section chips | M |
| W3 | Home: KPI tiles + quick actions + recent activity (bell feed) | S |
| W4 | Order builder: picture `MenuItemCard`, star, sticky `CartBar`, `CartLine` with edit/delete, merged `ItemOptionsSheet` (D2), order-level notes wired to the existing field | L |
| W5 | My orders: Active / Completed tabs, `OrderCard` with four-step `KitchenProgress` derived from `OrderStatus` | M |
| W6 | Order details: Details / Items / Timeline tabs (ticket times via `GET api/kitchen/orders/{ticketId}`), `ActionList` (Add items, Edit = guests/notes/cancel line, Request bill, Served, Release, Cancel) | L |
| W7 | Notifications page, Settings/profile, Sign-in restyle, offline / error / back-online states (no "syncing" claim — there is no send queue) | M |
| W8 | bUnit tests, `web-flow.mjs`, gallery | M |

### 21.2 Kitchen (web/Android; desktop kitchen display follows with the same tokens)

| # | Step | Size |
|---|---|---|
| K1 | `KitchenHeader`: brand, station chips with counts (All stations only; per-station counts go stale by design), search, clock, connection, bell, theme, menu (Completed, Station, Settings), user/device | M |
| K2 | KPI strip (New, Preparing, Ready, Delayed, Completed today), column subtitles, "Oldest first" sort, per-column timers (Ready shows time since ready; late still counts from created), Delayed filter (D4) | M |
| K3 | `TicketCard`: big table code, station chip, order · waiter · items, elapsed pill, items with modifiers and notes, big ACCEPT / START / MARK READY / DONE / RECALL | M |
| K4 | `TicketDetailPane` (monitor ≥ 1440): items, guests, notes via `GET api/orders/{id}`, timeline with "Taking longer than usual"; no prices (D9 / `KotShowPrices`) | M |
| K5 | Completed and Station pages restyle | S |
| K6 | Tests and gallery | S |

### 21.3 Cashier (WPF) — `audit-desktop-cashier-admin.md` § 5

| # | Step | Size |
|---|---|---|
| C1 | `CashierWorkspaceView`: queue 400 px · bill · payment 340 px; `billing` module points at it; queue gains search, sort, chips Waiting / Invoiced / Partly paid (+ Paid → Closed bills) | L |
| C2 | Bill cards: status pill, waiter, wait time, total | M |
| C3 | Order column: pills, item thumbnails via `MenuCache`, options and notes, order-notes banner, real tax rows, actions (Print bill, Reopen for waiter, Void) | M |
| C4 | Payment column permanent: due hero, quick amounts, configured methods (D6), received, change, Complete, inline Paid state; keep `IdempotencyKey`/`PendingRequest`; shift bar stays (cash needs an open shift) | M |
| C5 | Quick actions, 1366×768 pass with the collapsed sidebar, `BillingViewModelTests` still guard duplicate submits, rendering + live gallery | S |

### 21.4 Admin / Manager (WPF, option A)

| # | Step | Size |
|---|---|---|
| A1 | Regroup `ModuleRegistry` to the prompt's groups + "System" (D7); new keys Categories, Menu items, Kitchen stations, Orders (all, over `GetActiveAsync`); relabels (D10) | M |
| A2 | Dashboard header (business day, Online pill) and KPI cards: Today's sales (+ delta vs yesterday from `LastDays`), Orders, Average order value (derived), Occupied tables with occupancy bar; no sparklines, no low stock | M |
| A3 | Live Order Status donut from `/api/orders/active` (`ChartGeometry.RingSegment`) | M |
| A4 | Active orders table: chips, sort, 10 per page (`Pager`), View → table | M |
| A5 | Table status cards via `TableTileViewModel`, "View floor plan" → Tables | M |
| A6 | Recent activity from `NoticeCenter` (last 8) + "View all" → Audit; Staff today + Terminals side by side; keep 7-day bars, payments donut, top items with thumbnails | M |
| A7 | Deep links: Reports by key, Settings by section, Menu by tab, Templates | M |
| A8 | 1366×768 pass, render/token tests, admin live gallery, `docs/07` | S |

### 21.5 Verification (prompt § 15 phases 7–8)

Full suite; rendering tests both themes; web and desktop galleries regenerated and compared with the mock-ups
side by side; device demo on phone, tablet and the cashier PC; 1366×768 and 125 % pass; no console errors on the web;
no business-logic diff (`git diff --stat src/HotelPOS.Application src/HotelPOS.Domain` must be empty except
`Contracts/Presentation`).

## 6. Order and effort

0.1 → 0.2 → 0.3 (one agent, then both tracks in parallel):
web track 0.4 → 0.7 → W1–W8 and K1–K6; desktop track 0.5 → 0.6 → C1–C5 → A1–A8; then 21.5.

| Track | Agent-days |
|---|---|
| Design system | 4–5 |
| Waiter | 7–9 |
| Kitchen | 4–5 |
| Cashier | 6–8 |
| Admin | 9–12 |
| Verification | 2–3 |
| **Total** | **32–42** |

## 7. Data model, API, events

None. `Contracts/Presentation/StatusPresentation.cs` is the only new shared code and is presentation only.

## 8. Tests

Token contract (both themes, WCAG), web token parity, status map completeness, bUnit per web page (keep existing
selectors or update tests together), desktop VM tests for the workspace and dashboard panels, rendering counts, the
live gallery harness for both clients.

## 9. Risks

- Dark theme: page and navy sidebar nearly merge; sidebar `#0A111C` in dark, soft fills re-derived.
- `BillDetailViewModel` (990 lines) assumes overlays; the permanent payment column is the riskiest step (C1/C4).
- Android WebView: no `window.print`, older WebViews lack `:has()`; fonts must be self-hosted; service-worker asset
  paths are still wrong (Phase 20 open item) and new assets make offline install worse — fix before W8.
- Licence seats: each browser profile and role is a terminal; galleries must reuse device identities.
- 1366×768: the cashier three-pane needs the sidebar collapsed; admin drops to two content columns.
- "Colour alone": badges, tiles and KPI deltas need text/icons; only the galleries catch this — review them.
- Meaning change: Ready goes from red to green on every client; tell the venue.

## 10. Acceptance criteria

- [ ] Every screen in the six mock-ups has a counterpart, or an entry in § 3 "Out" with the missing data named.
- [ ] One token set and one status map; contract tests green on both themes.
- [ ] No change under `src/HotelPOS.Application`, `Domain`, `Infrastructure`, `Api` except presentation contracts.
- [ ] Full suite green; galleries regenerated; device demo run on phone, tablet and cashier PC.
- [ ] Owner has signed off D1–D11 before the related step starts.

## 11. Changes during implementation

(filled while building)
