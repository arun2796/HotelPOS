# Phase 21 — Role-based UI/UX redesign (plan)

Status: Implemented on branch `phase-21-role-ui-ux` (2026-10-17; galleries regenerated, full suite green) — owner review of the galleries against the mock-ups pending · Depends on: Phases 17–20

Source: the owner's master prompt (`docs/design/master-prompt-role-ui-ux.md`) and six mock-ups
(`docs/design/reference/`). Three audits back this plan and hold the detail: `docs/design/design-system-plan.md`,
`docs/design/audit-web-waiter-kitchen.md`, `docs/design/audit-desktop-cashier-admin.md`. This document is the
summary, the decisions, and the ordered step list. The owner asked for the plan first and then delegated the § 4
decisions; implementation can start.

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
kitchen ticket pictures (`KitchenTicketItemDto` has no image field), Print KOT from the phone or browser (no web print
code exists; desktop KOT print stays the path). Each is recorded in the audits with the exact missing field or endpoint.

## 4. Decisions (taken 2026-10-17)

The owner delegated D1–D11. Each recommendation was fact-checked against the code by three read-only audits (web,
desktop, shared) before being adopted; the amendments below come from that check. Paths are under `src/`.

| # | Decision | Decided | Evidence and amendments |
|---|---|---|---|
| D1 | **Where Admin/Manager lives.** The prompt says browser; today it is WPF. A) restyle WPF (17–22 agent-days, one code base); B) new Blazor admin (12–16 days for dashboard + reports + GST only; 45–60 for parity, then two admin code bases); C) hybrid (24–28, two shells) | **A — restyle WPF.** | `ModuleRegistry` holds 30 modules (Admin sees 29, Manager 21); `HotelPOS.Web`/`Client.Shared` have no report, dashboard, chart, sidebar or admin API client code. Effort figures are the audit's (`audit-desktop-cashier-admin.md` lines 17–19). **Deliberate deviation from the prompt's "desktop or laptop browser" target**: the admin runs in the WPF window. A browser admin later starts from A's widgets and gap list. |
| D2 | **Modifier choice sheet on the phone.** Mock-up 6 shows add-ons/spice/instructions; prompt § 3 bans modifier *management*, § 8 says "do not add a Modifiers & Add-ons interface" | **Restyle the existing picker only; nothing new.** | `OrderBuilderPage.razor` already has the picker (groups, min/max via `OrderComposer.CanSelectMore`, quantity stepper) and a per-line "Note for the kitchen" sheet writing `OrderItemInput.Notes` (500 chars). § 2 forbids removing a working feature. Mock-up "spice" = the seeded *Spice level* modifier group; "instructions" = the line note. No new route, nav item, spice field or editing; the merged `ItemOptionsSheet` (W4) is the picker plus the note sheet in one surface. |
| D3 | **Customers.** Desktop has a Customers screen and bill picker; the prompt excludes Customers & Loyalty | **Drop the nav item; keep the module key and the bill-level picker/panel; add a "Manage customers" link inside the bill picker.** | `customers` (`ModuleRegistry.cs:85`, Billing group, `BillingOperate`). `BillDetailViewModel` uses `ICustomersApi`; `SetBillCustomer` snapshots name/phone/GSTIN onto the bill; `GstData.cs:182` makes an invoice B2B when the GSTIN is valid, else B2C — the GSTIN is optional and can be typed directly. `ModuleDefinition` has no hidden flag: add `ShowInNav` (default true), false for `customers`, filtered in `ShellViewModel`'s nav build, so `Find`, the palette, the Import wizard's customer destination and `ShellTests` keep working. The picker link keeps repeat B2B buyers from re-typing GSTINs without a dedicated nav entry. No web customer code exists. |
| D4 | **Kitchen "Delayed".** Mock-up 2 shows a fourth column duplicating tickets; no such status exists | **KPI tile + red border (both exist) + a "Delayed" toggle that narrows the three columns; no fourth column, no new status.** | `KitchenPage.razor:43` already renders `Delayed @DelayedCount`; cards get `late`/`warn` from `KitchenRules.Timing`. Thresholds are the settings `kitchen.warn_minutes` / `kitchen.late_minutes` (defaults 10 / 20, `KitchenRules.cs:28-29`), matching prompt § 9 "per existing thresholds". `KitchenOrderStatus` has no Delayed value. Only the filter toggle is new (client-side `_delayedOnly` on the column `.Where`). Desktop KDS duplicates the defaults — point it at the same `KitchenRules`. |
| D5 | **Waiter bottom navigation.** Prompt: Tables / My Orders / New Order / Notifications / Profile. Mock-ups: Home / Tables / Orders / Menu / More. Today: Home / Tables / My orders / Settings | **Home / Tables / Orders / New order / More.** | An order cannot exist without a table (`CreateOrderRequest.TableId` is a non-nullable int; builder route needs `{TableId:int}`), so **New order** opens a pick-a-table sheet (available tables first, then the waiter's active tables) and navigates to `tables/{id}/order`. **More** is a new sheet holding Notifications (the existing `NotificationPanel` shown as a sheet — there is no notifications route, which satisfies § 8 "only supported routes"), Settings (`/settings`: device notification and sound preferences), Profile (name, role, sign-out, theme — moved from the topbar avatar). Bell stays in the header. |
| D6 | **Payment method buttons** from the configured list instead of hard-wired Cash/Card/UPI | **Yes: one button per active method from `GET /api/payment-methods`, in `SortOrder`.** | API and desktop client exist (`BillingControllers.cs:144`, `BillingApi.cs:41`); `BillingSetupViewModel` already manages methods; `BillDetailViewModel.cs:451` already loads `_methods`; the split overlay already iterates them. Only `BillDetailView.xaml:191-193` (literal CASH/CARD/UPI buttons, F2–F4) and the `PaymentMode` enum in `PaymentViewModel.cs:68-73` change. Rules: the command keeps sending `PaymentMethodId`; tendered/change/shift attach key off `dto.IsCash` (server: `Bill.cs:229-236`, `AddPayment.cs:96`), reference field off `RequiresReference`; **F2 = the cash method, F3… = the rest in sort order**; Split stays a fixed button; if no cash method is active the cash controls are hidden rather than silently inert. |
| D7 | **Pages the prompt's navigation omits** cannot be dropped | **New "System" group = Licence, Backup, Server, This terminal, Import, Audit. Day end (+ history), Period close, Cash shifts and GST Returns go under Finance, not System.** | Full mapping of the 30 modules: Overview → dashboard · Live Operations → tables, floor, my-orders + new Orders page, kitchen-display, kitchen-completed, billing, closed-bills, print-log · Menu Management → menu (Categories / Menu items tabs) · Finance → discounts (as "Payments & discounts"), shifts, day-end, day-end-history, period-close, gst · Analytics & Reports → reports (Sales, Popular items, Tax breakdown, Staff performance are report types inside it; no second nav item points at it) · People → users, roles · Devices → devices, printers · Settings → settings · System → license, backup, server, this-device, import, audit · not in nav → customers (D3); cashier-home is Cashier-only. Expenses and Profit & Loss have no module and are not fabricated. Groups left empty by `ForUser` permission filtering are hidden. |
| D8 | **Ready turns green** (red since Phase 17); Billing orange per the rule although the admin mock-up draws it blue | **Ready → success green; Billing → warning orange; release note.** | Today `Status.Ready = #DC2626`, `Status.Billing = #0F766E` in `Tokens.Light/Dark.xaml:71-77` and `tokens.css:40-41`. Prompt § 4: "green = completed/ready/available; orange = preparing/pending/bill requested". Two collisions to resolve in 0.1/0.2: **Available vs Ready** are both green — Available uses the soft fill with outline, Ready the solid fill with a check icon and the word "Ready"; **Preparing vs Billing** are both orange — Preparing keeps amber `#B45309`, Billing takes warning `#F59E0B`, and both carry icon + label (prompt: never colour alone). Mapping sites to collapse into `StatusPresentation`: two `StatusLabels.cs`, `DesignSystem.xaml:1046-1125`, `TableMapView` legend, `TableDetailsView`, `app.css:152-164`, `KitchenRules.cs`. |
| D9 | **"Edit order" / "Hold order" / "Print KOT" on the phone** | **Edit = guests + notes, cancel a line, add items (all existing endpoints). Hold omitted. Print KOT deferred out of Phase 21.** | Edit uses `PUT api/orders/{id}` (`UpdateOrderRequest` with `GuestCount`, `Notes`, **`RowVersion`** — the client must send the current one), `POST {id}/items/{itemId}/cancel`, and the existing `tables/{t}/order/{o}/add` flow. `OrderStatus` has no Hold/Parked value (Draft is pre-submit), so Hold is out. Print KOT is *not* existing client capability: the web has no print code at all; it would be new browser-only JS interop over `GET api/print/kot/{ticketId}` plus the `POST kot/{ticketId}/copies` print-log reservation, and the Android host is MAUI BlazorWebView without `window.print`. It breaks the "existing endpoints only, presentation only" rule, so it is listed in § 3 Out as a gap and the desktop KOT print remains the supported path. |
| D10 | **"Shifts" under People** are cash shifts; "Recent activity" source | **Nav label "Cash shifts" under Finance. Recent activity reads the audit log, merged live with the bell feed.** | `CashShift` is one cashier's stint at one drawer (float, counts, variance, X/Z; `Domain/Cash/CashShift.cs`); the module is already titled "Cash Shifts" (`ModuleRegistry.cs:87`). `NoticeCenter` is in-memory, 50 items, cleared on sign-out (`AppNavigator.cs:84`), so a bell-only widget is **empty at every launch**. A2/A6 therefore read the last 8 rows of `GET api/admin/audit-logs` (needs `AuditView`; actions mapped to readable sentences), prepend live `NoticeCenter` items as they arrive, "View all" → Audit. Without `AuditView` the widget falls back to the bell feed. The bell itself stays a live-alert surface, not a history. |
| D11 | **Pictures.** No stock photos | **Item pictures render from the existing `ImageUrl` with an initials placeholder; kitchen ticket pictures deferred; the owner uploads pictures through the desktop Menu editor; galleries are not blocked on them.** | `MenuItem.ImagePath` → `MenuItemDto.ImageUrl` (`/images/menu/…`), upload `POST api/menu-items/{id}/image` (JPEG/PNG, ≤ 2 MB, re-encoded to 512 px JPEG, `MenuManage`; only after the item is first saved — `CanEditPicture => !IsNew`). Desktop editor and order builder already show them; the web shows them only in `OrderBuilderPage.razor:66`. `KitchenTicketItemDto` carries no image, so ticket pictures would need a DTO change — out of scope, recorded in § 3. There is no bulk upload: the demo set is uploaded one item at a time (or via a dev-only seed helper if the owner wants one, as a separate small task). |

## 5. Deliverables by step (files in the audits; sizes S/M/L; 1 agent-day ≈ one Sonnet/Opus agent run)

### 21.0 Design system (shared, first) — `design-system-plan.md` § 6

| # | Step | Size |
|---|---|---|
| 0.1 | Tokens on both sides: palette, navy sidebar set, `*Soft/*Text` tones, neutral, font-size keys; `Status.*` re-pointed (Ready → solid success, Available → soft success, Billing → warning `#F59E0B`, Preparing keeps `#B45309`; D8); stray literals (`App.xaml` MaterialDesign blue, `index.html` theme-color, Android `App.xaml`) | M |
| 0.2 | `Contracts/Presentation/StatusPresentation.cs`: every status enum → label + tone + icon (+ fill: soft/solid); both `StatusLabels.cs`, `DesignSystem.xaml` tile setters, `TableMapView` legend and `app.css .st-*` delegate; desktop KDS thresholds read the same `kitchen.warn_minutes` / `kitchen.late_minutes` settings as `KitchenRules` (D4) | S |
| 0.3 | Inter: desktop `Font.Body` order; web self-hosted woff2 (offline LAN, Android) | S |
| 0.4 | Web primitives: `StatusBadge`, `KpiTile`, `Tabs`, `Segmented`, `SearchBox`, `FilterChip`, `OrderTimeline`, `DetailPane`, `OrderLine`, `Icon` (inline SVG replacing Unicode glyphs), `BottomSheet` | M |
| 0.5 | Desktop primitives: `StatusBadge`, `Pager`, `DetailPane`, `KpiTile` restyle, `Button.Warning/PrimaryOutline/Large`, soft `TableTile`, `Tabs`, `Segmented`, `SearchBox`, `OrderLine`, `PaymentPanel` styles, `StatusToneToBrush` | L |
| 0.6 | Desktop navy shell: grouped sidebar with badges and `DeviceStatusCard` footer, white top bar, auto-collapse below 1440 px | M |
| 0.7 | Web shells: waiter header + five-tab nav (D5); kitchen compact header, no bottom nav (K1) | M |
| 0.8 | Contract tests: palette pinned, WCAG ≥ 4.5 on both themes, web/desktop token parity (`WebTokenContractTests`), every enum member mapped (`StatusPresentationTests`) | S |

### 21.1 Waiter (web/Android) — `audit-web-waiter-kitchen.md` § 4

| # | Step | Size |
|---|---|---|
| W1 | Shell: header, five-tab nav (D5), pick-a-table sheet for New order, "More" sheet (NotificationPanel, Settings, Profile/sign-out/theme) | M |
| W2 | Tables: pastel `TableCard` (code, status, guests, timer, order no.), section chips | M |
| W3 | Home: KPI tiles + quick actions + recent activity (bell feed) | S |
| W4 | Order builder: picture `MenuItemCard`, star, sticky `CartBar`, `CartLine` with edit/delete, merged `ItemOptionsSheet` (D2), order-level notes wired to the existing field | L |
| W5 | My orders: Active / Completed tabs, `OrderCard` with four-step `KitchenProgress` derived from `OrderStatus` | M |
| W6 | Order details: Details / Items / Timeline tabs (ticket times via `GET api/kitchen/orders/{ticketId}`), `ActionList` (Add items, Edit = guests/notes via `PUT api/orders/{id}` with `RowVersion` + cancel line, Request bill, Served, Release, Cancel; no Hold, no Print KOT — D9) | L |
| W7 | Notifications page, Settings/profile, Sign-in restyle, offline / error / back-online states (no "syncing" claim — there is no send queue) | M |
| W8 | bUnit tests, `web-flow.mjs`, gallery | M |

### 21.2 Kitchen (web/Android; desktop kitchen display follows with the same tokens)

| # | Step | Size |
|---|---|---|
| K1 | `KitchenHeader`: brand, station chips with counts (All stations only; per-station counts go stale by design), search, clock, connection, bell, theme, menu (Completed, Station, Settings), user/device | M |
| K2 | KPI strip (New, Preparing, Ready, Delayed, Completed today — all existing counts restyled), column subtitles, "Oldest first" sort, per-column timers (Ready shows time since ready; late still counts from created), Delayed chip becomes a filter toggle (D4) | M |
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
| C4 | Payment column permanent: due hero, quick amounts, one button per active configured method in `SortOrder` (D6: F2 = cash, F3… in order, Split fixed, cash controls keyed off `IsCash`, reference off `RequiresReference`, `PaymentMode` enum retired), received, change, Complete, inline Paid state; keep `IdempotencyKey`/`PendingRequest`; shift bar stays (cash needs an open shift) | M |
| C5 | Quick actions, 1366×768 pass with the collapsed sidebar, `BillingViewModelTests` still guard duplicate submits, rendering + live gallery | S |

### 21.4 Admin / Manager (WPF, option A)

| # | Step | Size |
|---|---|---|
| A1 | Regroup `ModuleRegistry` to the prompt's groups + "System" per the D7 mapping; `ShowInNav` flag, false for `customers`, "Manage customers" link in the bill picker (D3); hide permission-empty groups; new key Orders (all, over `GetActiveAsync`); Menu gets Categories / Menu items tabs; relabels "Cash shifts", "Payments & discounts" (D10, D7) | M |
| A2 | Dashboard header (business day, Online pill) and KPI cards: Today's sales (+ delta vs yesterday from `LastDays`), Orders, Average order value (derived), Occupied tables with occupancy bar; no sparklines, no low stock | M |
| A3 | Live Order Status donut from `/api/orders/active` (`ChartGeometry.RingSegment`) | M |
| A4 | Active orders table: chips, sort, 10 per page (`Pager`), View → table | M |
| A5 | Table status cards via `TableTileViewModel`, "View floor plan" → Tables | M |
| A6 | Recent activity = last 8 rows of `GET api/admin/audit-logs` mapped to sentences, live `NoticeCenter` items prepended, "View all" → Audit, bell-feed fallback without `AuditView` (D10); Staff today + Terminals side by side; keep 7-day bars, payments donut, top items with thumbnails from `ImageUrl` + initials placeholder (D11) | M |
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

- [x] Every screen in the six mock-ups has a counterpart, or an entry in § 3 "Out" with the missing data named (gallery READMEs hold the per-screen comparison tables).
- [x] One token set and one status map; contract tests green on both themes.
- [x] No change under `src/HotelPOS.Application`, `Domain`, `Infrastructure`, `Api` except presentation contracts.
- [x] Full suite green; galleries regenerated.
- [ ] Device demo run on phone, tablet and the cashier PC (owner; not done by the agents).
- [x] D1–D11 decided (§ 4, 2026-10-17) before any step starts.

## 11. Changes during implementation

- 2026-10-17 — D1–D11 decided after three read-only code audits; § 4 rewritten with evidence and amendments; § 3 Out gains kitchen ticket pictures and phone/browser Print KOT; steps 0.1, 0.2, W1, W6, K2, C4, A1, A6 sharpened accordingly. No code changed.
- 2026-10-17 — Implementation on branch `phase-21-role-ui-ux` (from `main` d59a5ae), merged per track:
  - 21.0 design system: tokens (f5f00d4), `StatusPresentation` (803085e), Inter + contract tests (2ecdd8e); web
    primitives/shells (merge 3010d8a); desktop primitives, navy shell, `ShowInNav`, D7 regrouping (merge 97a0729).
    Deviations: `TextSecondary #64748B` on the page is 4.42:1 — owner colours kept, floor pinned at 4.4;
    `NeutralText` is `#5B6B80`; `Brush.OnWarning #1F2937` added (white on `#F59E0B` fails AA); Inter woff2 are the
    full files (~112 KB each), not a Latin subset.
  - 21.2 kitchen K1–K6 (merge 027d9ba): `KitchenHeader`, KPI strip, Delayed filter, `TicketCard`, `TicketDetailPane`
    (no prices), Completed/Station. Completed tickets now read "Served" (status map). `GET api/orders/{id}` is open
    to the Kitchen role, so the detail pane shows guests and notes.
  - 21.1 waiter W1–W8 (merge 807f255): `TableCard`, home KPIs + attention list, `MenuItemCard`/`CartBar`/
    `ItemOptionsSheet` (picker + note + qty in one sheet, D2), order-level notes now sent (`CreateOrderRequest.Notes`,
    `WebDraft.Notes`), My orders tabs, order details with Details/Items/Timeline (ticket times via
    `api/kitchen/orders/{ticketId}` — the Waiter role is allowed), `ActionList` (Edit sends `RowVersion`), route
    `/orders/{id}` for completed orders, "Back online" banner. No Hold, no Print KOT (D9).
  - 21.3 cashier C1–C5 (merge 1bdba79): `CashierWorkspaceViewModel/View` composes the existing `BillDetailViewModel`;
    `PaymentMode` enum retired — `PaymentMethodButton.Build` from the configured list (F2 = cash, F3… sort order,
    Split F9 fixed); Discount, Customer and Invoice preview stay overlays; split tender is inline; numpad dropped.
    `billing` module now points at the workspace; legacy queue/detail views kept (App.xaml references).
  - 21.4 admin A1–A8 (merge 253fc0b): `orders` module (Live Operations), `stations` deep link (Devices, admin-only,
    via new `ModuleDefinition.DefaultParameter`), Menu Categories/Items tabs, `KpiCard`s with real deltas only,
    order-status donut, paged active orders, soft table tiles, Recent activity from `api/admin/audit-logs`
    (`AuditSentences`) with bell-feed fallback, deep links for Reports/Settings/Menu/Templates, 2-column layout
    below 1280. D3 "Manage customers" link in the bill picker: **not yet added** (picker is in `BillDetailView`).
  - Rendering count 78 → 81. Full suite on the merged branch: Domain 365, Application 253, Api 197, Client 204,
    Desktop 421 (2 pre-existing skips) — 0 failures. `git diff main -- Application Domain Infrastructure Api` is
    empty; Contracts gains only `Presentation/StatusPresentation.cs`.
  - Housekeeping: `*.SdkResolver.*.proj.Backup.tmp` untracked and ignored (e463ed1).
- 2026-10-17 — 21.5 galleries against the Development API on :5100 (commits 45c23b3 web, 7291c57 desktop).
  Web: `web-flow.mjs` rewritten for the new markup; 28 waiter + 11 kitchen shots; 8/8 compared screens match the
  mock-ups' structure; one high defect fixed (table-card timer overflow made the Tables page 404 px wide on a
  390 px phone). Desktop: `LiveFlowScreenshots` now drives the cashier workspace and the new admin surfaces;
  19 cashier + 16 manager shots; 8/8 compared screens match; clipped grid columns fixed. The admin password is not
  the seed default, so admin-only screens were captured as `manager1`; the `functions*` live passes still target
  the legacy bill pages and were not re-run. The gallery run registered three new browser terminals (WEB-*, PROBE);
  the owner may deactivate them. Remaining per-screen issues are listed in `docs/ui-ux/web/README.md` and
  `docs/ui-ux/desktop/README.md`.
