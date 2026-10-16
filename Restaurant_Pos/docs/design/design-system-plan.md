# HotelPOS design system plan (Phase 21)

One token set, status map and component vocabulary for WPF (Cashier, Admin, desktop Kitchen) and Blazor web/Android
(Waiter, Kitchen), matching `docs/design/reference/`.

## 1. Token diff

Desktop `Themes/Tokens.Light.xaml` and web `wwwroot/css/tokens.css` both carry the Phase 17 palette; same change on both.

| Prompt token | Desktop key (light) | Web var | Change |
|---|---|---|---|
| Primary `#078747` | `Brush.Primary` `#078B45`, `Link` `#078B45` | `--primary` `#078B45` | → `#078747`; `PrimaryHover`/`--primary-hover` → `#066B39` |
| Focus ring | `Brush.Focus` `#078B45` (green) | `--focus` `#2563EB` (blue) | **mismatch** → both `#2563EB` (blue = selected/info) |
| Navy `#142235` | `Brush.Sidebar` `#FFFFFF` (light sidebar) | none | → `#142235`; see sidebar rows below; add `--sidebar*` vars |
| Page bg `#F4F7FB` | `Brush.Background` `#F4F7FA` | `--bg` `#F4F7FA` | → `#F4F7FB` |
| Card `#FFFFFF` | `Brush.Surface` | `--surface` | none |
| Text `#172B4D` | `Brush.TextPrimary` `#172033` | `--text` `#172033` | → `#172B4D` |
| Secondary `#64748B` | `Brush.TextSecondary` `#667085` | `--text-2` `#667085` | → `#64748B` |
| Border `#E2E8F0` | `Brush.Border` `#E3E8EF` | `--border` `#E3E8EF` | → `#E2E8F0` |
| Success `#16A34A` | `Brush.Success` | `--success` | none; add `Brush.SuccessText` `#15803D` (16A34A is 3.3:1 on white, fails text AA); web `--success-text` → `#15803D` |
| Warning `#F59E0B` | `Brush.Warning` | `--warning` | none; add `Brush.WarningText` `#B45309` (web already has `--warning-text`) |
| Error `#DC2626` | `Brush.Danger`, `DangerText` `#B91C1C` | `--danger`, `--danger-text` | none |
| Info `#2563EB` | `Brush.Info`, `InfoSoft` | `--info`, `--info-soft` | none |
| Grey (inactive) | none (`Brush.Neutral.*` in DesignSystem.xaml) | none | add `Brush.Neutral`/`NeutralSoft`/`NeutralText` = `#94A3B8`/`#F1F5F9`/`#64748B`; web `--neutral*` |
| Status hues | `Brush.Status.*`: Ready **red** `#DC2626`, Preparing `#B45309`, Billing teal, Ordering purple | `--st-*` same | re-point to tones (§4): Ready→success, Preparing/Billing→warning, Occupied/Ordering→info, OutOfService→neutral. Ready-red contradicts the new rule |

Sidebar sub-tokens (light): `Sidebar #142235`, `SidebarText #CBD5E1`, `SidebarTextActive #FFFFFF`, `SidebarTextMuted #8A9BB2`
(group headers), `SidebarHover #1C2E45`, `SidebarSelected #078747` (filled pill, as in admin/cashier mocks),
`SidebarAccent #FFFFFF`, `SidebarDivider #23354D`, new `SidebarBadge` = `Brush.Info`. White on navy is 16:1, muted 6:1.

Dark theme (`Tokens.Dark.xaml`, both dark blocks in tokens.css): page `#0F1623` is almost the navy, so the sidebar
would vanish. Use `Sidebar #0A111C` + `SidebarDivider #273246`. Status **solid** tones stay identical in both themes
(existing contract test); `*Soft`/`*Text` get dark variants (`SuccessText #4ADE80`, `WarningText #FBBF24`, `DangerText #FB7185`,
`InfoText #93C5FD`). Also fix strays: `App.xaml` MaterialDesign `CustomColorTheme PrimaryColor="#2563EB"` → `#078747`;
`HotelPOS.Web/wwwroot/index.html` `theme-color`, `HotelPOS.Android/App.xaml` `Primary`, `MainPage.xaml` background.

## 2. Typography

- Desktop: Inter Regular/Medium/SemiBold/Bold are bundled (`Resources/Fonts/*.ttf`, `<Resource>` in the csproj), but
  `DesignSystem.xaml` `Font.Body` = `Segoe UI Variable Text, Segoe UI, …#Inter`, so Windows 11 never shows Inter. Flip to
  `…/Resources/Fonts/#Inter, Segoe UI`; update `ScreenRenderingTests` (line ~476 accepts seguivar/segoeui) to require `inter-`.
- Web: `--font` = Segoe UI stack, no font files (Roboto on Android). Bundle Inter woff2 (4 weights, Latin subset) in
  `Client.Shared/wwwroot/fonts/`, `@font-face` + `font-display: swap`, preload in both `index.html`. No CDN (offline).

| Role | Spec | Desktop | Web |
|---|---|---|---|
| Page title | 24–30 | `Text.Title` (hard 24) → `Font.Size.PageTitle` 24 | new `--fs-title: 24px` (phone 22) |
| Section | 16–20 | `Text.Subtitle` (hard 16) → `Font.Size.Section` 18 | `--fs-section: 18px` |
| Body | 14–16 | `Font.Size.Body` 15 Comfort / 14 Compact (keep) | `--fs-body: 15px` (body is hard 15 in app.css) |
| Labels | 12–14 | `Font.Size.Label`/`Caption` 12–13; `GridHeader` 11 Compact → 12 | `--fs-label: 13px`, `--fs-caption: 12px` |
| Amounts | 22–32 | `Text.Amount` uses Body size → add `Font.Size.Amount` 24, `Font.Size.AmountLarge` 32, `Text.AmountLarge` | `--fs-amount: 24px`, `--fs-amount-lg: 32px` |
| KPI value | 28 | `KpiTile` value → `Font.Size.Kpi` 28 | `--fs-kpi: 28px` |

Kitchen ticket table code stays 24 (`.ticket-table`, desktop kitchen card), tabular figures for every amount.

## 3. Components

Desktop control name = web component name; tones `Neutral | Info | Success | Warning | Danger`.

| Component | Desktop today | Web today | Add / restyle |
|---|---|---|---|
| Button, IconButton | `Button.Primary/Secondary/Danger/Ghost/Icon/Link` | `.btn` primary/secondary/danger/ghost/icon | add `Warning` (orange "Mark ready"), `PrimaryOutline` (Start/Recall), size `Large` 56 px; desktop `Button.Secondary` is plain base → grey outline |
| SearchBox | palette look-alike in ShellView top bar | none | icon + placeholder + optional `Ctrl+K` hint, both sides |
| FilterChip (+count) | `Chip`, `ChipList` | `.chip-btn` | count badge ("Dine In 4"), selected = primary fill |
| Select / DatePicker | styled `ComboBox`, `DatePicker`, `ReportFilterBar` | native `select`/`input type=date` in `.input` | restyle only; no library |
| **StatusBadge** | `Pill.*`, `Chip.*`, `TableStatusBadge` | `.chip.*`, `.st-chip-*` | one `StatusBadge` (desktop `Controls/StatusBadge.cs`, web `Shared/StatusBadge.razor`): soft fill + tone text + dot/icon + label from §4 |
| Card, **KpiTile** | `Card`, `Card.Section`, `Controls/KpiTile` | `.card` | KpiTile: coloured icon square, value, delta (▲/▼ + %, never colour alone), caption, optional sparkline (desktop `ChartGeometry`, web inline SVG); web `KpiTile.razor` for the kitchen strip |
| DataTable | `DataGrid` styles | none | desktop: StatusBadge column, row action button, 44 px rows; web: none needed (admin is desktop) |
| **Pagination** | none | none | desktop `Controls/Pager.cs` (Prev/Next, "1–20 of 186"); web uses "Load more" |
| Dialog / DetailPane | `Dialog.Frame` | `.overlay` + `.sheet` | add `DetailPane` (right drawer: kitchen-2 order detail, cashier order) both sides |
| Toast | `ToastViewModel` in `MainWindow.xaml`, top-centre | `Toasts.razor`, bottom | same anatomy: tone bar + icon + text + optional action; keep positions |
| Tabs / Segmented | `Hub.Tab(s)` | `.seg` | `Tabs` (underline: Details/Items/Timeline) and `Segmented` (pill: Today/7 Days/30 Days) |
| Empty / Loading / Error | `EmptyState`, `BusyOverlay`, `Banner.Error` | `EmptyState`, `Skeleton`, `ErrorState` | desktop `ErrorState` (banner + Retry); icons on all three |
| **ConnectionPill** | `StatusBarViewModel`, `StatusBannerViewModel` | `ConnectionPill`, `OfflineBanner` | desktop pill in top bar; `DeviceStatusCard` (online, terminal, version) in sidebar footer both shells |
| OrderSummary | Billing/Orders views ad hoc | cart in waiter pages | `OrderLine` (image 48–56, "qty × name", modifiers secondary, note in `WarningText` + icon, price right), `NoteCallout` (warning soft), totals block |
| PaymentSummary | Billing views | read-only approx total | desktop `PaymentPanel` styles: due amount (AmountLarge, primary), quick amounts, 2×2 method toggles, received, change box (success soft) |
| **TableTile** | `TableTile` solid fill, white text | `.tile.st-*` solid | soft fill + tone border + code 22 + status icon/label + guests/timer/order no. |
| Timeline | none | none (`Stepper.razor` is a qty stepper) | `OrderTimeline` (Received → Preparing → Ready → Served, delayed step red) both sides |
| NavItem (+badge) | `NavList`, `NavItem` | `.bottomnav a` | count badge, group header |

## 4. Status map (single source)

Put it in `src/HotelPOS.Contracts/Presentation/StatusPresentation.cs`: `enum StatusTone`, plus
`StatusDisplay For(OrderStatus)` etc. returning `(Label, Tone, Icon)`. Both clients already reference Contracts, and
Contracts already holds UI words (`Realtime/NotificationPreference.cs`). `Client.Shared/Services/StatusLabels.cs` and
`Desktop/Modules/Common/StatusLabels.cs` delegate to it. Desktop: `StatusToneToBrush` converter; web: `tone-{x}` classes.

| Enum | Value → label · tone |
|---|---|
| OrderStatus | Draft "Draft" · Neutral; Submitted "Sent to kitchen" · Info; Accepted "Accepted" · Info; Preparing "Preparing" · Warning; Ready "Ready" · Success; Served "Served" · Info; BillRequested "Bill requested" · Warning; Billed "Awaiting payment" · Warning; Paid "Paid" · Success; Completed "Completed" · Success; Cancelled "Cancelled" · Danger |
| KitchenOrderStatus | New "New" · Info (waiter side keeps "Sent to kitchen"); Accepted "Accepted" · Info; Preparing "Preparing" · Warning; Ready "Ready" · Success; Completed "Served" · Success; Cancelled "Cancelled" · Danger |
| TicketTiming (`KitchenRules`) | Normal "n min" · Neutral; Warn "n min" · Warning; Late "Delayed" · Danger |
| OrderItemStatus | Draft "Not sent" · Neutral; Sent "Sent to kitchen" · Info; Cancelled "Cancelled" · Danger |
| TableStatus | Available · Success; Occupied · Info; Ordering "Ordering" · Info; Preparing · Warning; Ready · Success; Billing "Bill requested" · Warning; OutOfService "Out of service" · Neutral; a table whose ticket is Late shows Danger "Delayed" |
| BillStatus | Open "Open" · Info; Finalized "Awaiting payment" · Warning; Settled "Paid" · Success; Voided "Voided" · Danger |
| PaymentStatus | Pending "Unpaid" · Warning; PartiallyPaid "Partially paid" · Warning; Paid · Success; Refunded "Refunded" · Info; Cancelled · Danger |
| PaymentRecordStatus | Completed "Received" · Success; Refunded · Info; Voided · Danger |
| CashShiftStatus | Open · Success; HandedOver "Handed over" · Info; Closed · Neutral; ForceClosed "Force-closed" · Warning |
| BusinessDayStatus | Closed · Neutral; Reopened · Warning |
| BackupStatus | Running "Running" · Info; Succeeded · Success; Failed · Danger |
| Connection (`WebConnectionStatus`, desktop reconnect) | Connected · Success; Connecting/Reconnecting · Warning; Offline · Danger; Syncing · Info |

Changes from today: desktop/web table Ready red → green; Billing teal → warning; Ordering purple → info; web
`OrderChip` Served/Draft gain explicit tones. Admin mock draws Billing blue, the rule says orange: owner to confirm.
"Reserved" (waiter mock) has no enum value; leave it out.

## 5. Layout shells

| Role | Shell | Notes |
|---|---|---|
| Admin/Manager (WPF) | navy sidebar 240/64 with groups (mock: Live Operations, Menu, Finance, Analytics, People, Devices, Settings), white 64 px top bar (title+subtitle, date/branch filters, search, bell, theme, connection), `DeviceStatusCard` + user at bottom | `ShellView.xaml` already has collapse, groups (`NavItemViewModel.ShowGroupHeader`) and Ctrl+K; restyle, add badges |
| Cashier (WPF) | same navy shell, flat list | 3 panes: bills 400 / order * / payment 340 |
| Kitchen, monitor ≥1440 (web + WPF) | kitchen-2: navy sidebar, station chips in top bar, 4 columns incl. DELAYED, `DetailPane` | |
| Kitchen, tablet 1024–1439 | kitchen-1: compact header with chips, 64 px icon rail, 3 columns, late tickets keep their column with Danger border + "Delayed"; detail as overlay | |
| Kitchen, portrait <1024 / Waiter phone | top bar + bottom nav (waiter: Home, Tables, My orders, Menu, More) | today waiter has 4 tabs incl. Settings |

1366×768: 1366 − 240 leaves 1126 px but the cashier 3-pane needs ~1180, so auto-collapse the sidebar below 1440 px
(re-expandable). Admin: 5 KPI tiles at ~210 px fit; content rows drop to 2 columns below 1280. Phone: 360 px min,
3 table tiles per row, 48 px targets, `--safe-bottom`. `MainLayout.razor` picks the shell from `ShellModes.For(Auth)` + CSS media queries.

## 6. Steps

Order follows the role tracks: Waiter → Kitchen → Cashier → Admin.

| # | Step | Files | Size |
|---|---|---|---|
| 1 | Tokens: palette, sidebar, tone `Soft/Text`, neutral, font-size keys/vars; Status.* re-pointed; stray literals | `Tokens.Light/Dark.xaml`, `Density.*.xaml`, `tokens.css`, `App.xaml`, both `index.html`, Android `App.xaml`/`MainPage.xaml` | M |
| 2 | Status map | `Contracts/Presentation/StatusPresentation.cs`, both `StatusLabels.cs` | S |
| 3 | Inter everywhere | `DesignSystem.xaml` `Font.Body`, `wwwroot/fonts/*.woff2`, tokens.css, index.html, `ScreenRenderingTests` | S |
| 4 | Web primitives | `Shared/StatusBadge, KpiTile, Tabs, Segmented, SearchBox, FilterChip, OrderTimeline, DetailPane, OrderSummary.razor`, `Toasts.razor`, `app.css` | M |
| 5 | Web shells | `MainLayout.razor`, `DeviceStatusCard.razor`, app.css (rail, bottom nav 5 items, breakpoints) | M |
| 6 | Desktop primitives | `Controls/StatusBadge.cs, Pager.cs, DetailPane`, `KpiTile` restyle, `Controls.xaml`/`DesignSystem.xaml` (Button.Warning/PrimaryOutline/Large, TableTile soft, Tabs, Segmented, SearchBox, OrderLine, PaymentPanel), `StatusToneToBrush` | L |
| 7 | Desktop navy shell | `ShellView.xaml`, `ShellViewModel.cs` (badges, auto-collapse), `MainWindow.xaml` toast | M |
| 8 | Docs | `docs/05-desktop-application.md` §9, phase-21 doc, galleries regenerated | S |

Tests:
- `ThemeTokenContractTests`: keep parity; pin the prompt palette values (light); `Status.Ready == Success`; WCAG
  fact (pure C#), both themes ≥ 4.5: text on Surface/Background, each `*Text` on `*Soft`, sidebar text on Sidebar,
  PrimaryText on Primary.
- New `Client.Tests/WebTokenContractTests.cs`: regex-parse tokens.css; mapped vars equal desktop keys (light, dark);
  `@media` dark block equals `[data-theme=dark]`; no hex literals in `Client.Shared` `.razor`.
- `StatusPresentationTests`: every enum member has label + tone (new unmapped values fail); replaces the chip cases
  in `WebUiFixTests` (~line 344).

## 7. Risks

- **Dark theme + navy**: page and sidebar nearly the same colour; needs the darker sidebar plus divider and a check in
  the rendering gallery. Soft fills in dark must be re-derived, not reused.
- **Meaning change**: Ready has been red since Phase 17; announce the switch to green in release notes.
- **Touch density**: web `--touch` is 44 px, below the `docs/05` §9 48 px rule; raise to 48 (kitchen primary 56).
  Cashier runs Compact (38 px controls) but the mock's payment buttons are 48+: use `Button.Large` there.
- **Android WebView fonts**: no Segoe; woff2 must ship as static web assets (`_content/.../fonts`), offline; brief
  FOUT with `swap`; ~100 KB APK growth.
- **Low-end tablets**: keep kitchen cards flat (no shadows), lazy dish images with a fallback icon.
- **Colour alone**: badges, tiles, KPI deltas and pills need text or icon; tests cannot catch it, so review galleries.
