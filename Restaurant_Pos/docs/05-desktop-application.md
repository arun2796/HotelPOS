# 05 — Desktop Application (WPF)

Last updated: 2026-10-14

## 1. Project layout (`HotelPOS.Desktop`)

```
HotelPOS.Desktop/
  App.xaml / App.xaml.cs        generic host bootstrap, global exception handlers
  Startup/                      HostBuilder, DI registrations, startup flow (config -> login -> shell)
  Shell/                        ShellWindow, ShellViewModel, navigation sidebar, status bar
  Modules/
    Config/                     FirstRunConfigView/VM, ServerSettingsView/VM
    Auth/                       LoginView/VM, ChangePasswordView/VM
    Waiter/                     TableMapView/VM, TableDetailsView/VM, OrderBuilderView/VM, MyOrdersView/VM
    Kitchen/                    KitchenDisplayView/VM, TicketCardView, CompletedOrdersView/VM
    Billing/                    BillingQueueView/VM, BillDetailView/VM, PaymentView/VM, ClosedBillsView/VM
    Admin/                      Users, Roles, Sections/Tables, Categories, MenuItems, Modifiers, Taxes,
                                Stations, Discounts, PaymentMethods, Devices, Settings, AuditLog, Backup
    Reports/                    DashboardView/VM, ReportsView/VM
  Services/
    Configuration/              IClientSettingsService (settings.json), ISecureStore (DPAPI)
    Api/                        ApiClient (HttpClient), AuthDelegatingHandler, typed clients per area
    Auth/                       IAuthSession (tokens, user, roles, permissions)
    Realtime/                   IRealtimeClient (HubConnection wrapper), IConnectionState
    Navigation/                 INavigationService, module registry (role -> modules)
    Notifications/              INotificationService (toasts, banners, sound)
    Dialogs/                    IDialogService (confirm, input, manager approval)
    Printing/                   IPrintService, renderers, print queue
    Drafts/                     ILocalDraftStore (per-table draft JSON)
    Logging/                    Serilog setup
  Controls/                     touch-friendly reusable controls (NumPad, QuantityStepper, StatusChip,
                                TicketCard, TableTile, ConnectionIndicator)
  Themes/                       Tokens.Light/Dark.xaml (colours), Density.Comfort/Compact.xaml (sizes),
                                DesignSystem.xaml (fonts, converters, every style) — Phase 11
  Resources/Fonts/              Inter (SIL OFL), the design system typeface
  Converters/, Behaviors/
  Assets/                       icons (Segoe Fluent Icons / Material icon font), sounds
```

## 2. MVVM rules

- `CommunityToolkit.Mvvm`: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`,
  `AsyncRelayCommand`, `IMessenger` for loosely coupled notifications.
- Views contain XAML and minimal code-behind (focus handling, animations only).
- View-models receive services through constructor injection; no static service locator.
- No business rules in view-models beyond display logic (provisional cart total, filtering, formatting).
- Long-running work is `async`; UI thread never blocks. All API calls support `CancellationToken`.
- Every view-model that shows server data has `LoadAsync()` (initial) and `RefreshAsync()` (resync).
- View-models are unit-testable (`HotelPOS.Desktop.Tests`) with fake services.
- Screen view-models derive from `ViewModelBase` (Phase 12): one `IsBusy`, one `ErrorMessage`, `ExecuteAsync` for
  work (an unexpected fault is logged and shown on that screen) and `FetchAsync` for loads.

## 3. Startup and configuration

```
App start
  -> load %ProgramData%\HotelPOS\Desktop\settings.json
     (missing or ApiBaseUrl empty)       -> First-run configuration window
  -> GET /api/system/info (Test connection) fails -> show error with "Change server" button
  -> Login window (username/password; remembered username)
  -> register device if no DeviceId stored (POST /api/devices/register)
  -> Shell window with modules for the user's roles
```

`settings.json` (machine-wide, writable by the app via an installer-created ACL; if ProgramData is not
writable the file is saved under `%LocalAppData%\HotelPOS\Desktop\` instead and the newest file wins).
Start the app with `--profile NAME` to run several terminals on one PC (each profile has its own
`settings.NAME.json`, session, preferences and log file):

```json
{
  "ApiBaseUrl": "http://192.168.1.100:5000",
  "DeviceId": "guid-after-registration",
  "DeviceName": "WAITER-01",
  "DeviceType": "Waiter",
  "StationId": null,
  "Theme": "Light",
  "Density": null,
  "Printers": {
    "Kot": { "Kind": "Network", "Host": "192.168.1.50", "Port": 9100, "PaperWidthMm": 80, "Copies": 1, "Model": "Generic", "CurrencyFallback": "Rs." },
    "Invoice": { "Kind": "EscPosRaw", "PrinterName": "POS-80", "PaperWidthMm": 80 },
    "Receipt": { "Kind": "Windows", "PrinterName": "Microsoft Print to PDF" }
  },
  "AutoPrintKot": true,
  "AutoPrintInvoice": true,
  "AutoPrintReceipt": true
}
```

The first-run screen captures `ApiBaseUrl`, `DeviceName`, `DeviceType` (and station for kitchen) and
offers **Test connection**, which shows restaurant name and API version on success. The same screen is
reachable later from the login window ("Server settings") and from the shell (Admin/Manager or local
"Device settings").

## 4. Authentication and token storage

- Access token (JWT, 60 min) kept in memory only.
- Refresh token (opaque, 12 h, rotated on each refresh) stored with DPAPI
  (`ProtectedData`, CurrentUser scope) in `%LocalAppData%\HotelPOS\session.dat`.
- `AuthDelegatingHandler` attaches the bearer token, refreshes once on 401, then forces re-login.
- Logout revokes the refresh token server-side and clears the local store.
- Shift-friendly: remembering the username is allowed, passwords never.

## 5. API client

- One `HttpClient` (via `IHttpClientFactory`) with base address from settings, 15 s timeout.
- `ApiClient` unwraps `ApiResponse<T>` into `ApiResult<T>` (success/data/errors/status) — view-models
  never deal with raw HTTP.
- Automatic retry only for **GET** and for POSTs that carry an `Idempotency-Key` (3 attempts).
- Every request sends `X-Device-Id` and `X-Correlation-Id`.
- 409 `CONCURRENCY_CONFLICT` responses include the current entity; the client refreshes and tells the
  user "This table/bill was changed by another terminal".

## 6. Connection manager and status

`IRealtimeClient` wraps the SignalR `HubConnection` (see `docs/04-realtime-events.md` § 5).
`IConnectionState` exposes `Status` (Connected / Reconnecting / Disconnected) bound to the status bar
indicator (🟢 / 🟡 / 🔴 with text). On `Reconnected`, the active module's `RefreshAsync()` runs.
When `Disconnected`, server-dependent buttons remain enabled but the action returns the message
"Connection unavailable — the order was NOT sent" and keeps the draft.

## 7. Navigation and shell

One window (`MainWindow`) hosts the current screen (configuration, login, forced password change or the
shell) plus two overlay layers: modal dialogs (`IDialogService`) and toasts (`INotificationService`).
View-models are matched to views by DataTemplates in `App.xaml` and created with `ActivatorUtilities`.

- Left sidebar with module buttons (icon + label) grouped by area and filtered by permission; collapses to icons
  (remembered per terminal); user card with change password and sign out. Top bar (Phase 11): page title and
  description, search (opens the Ctrl+K command palette), clock, theme toggle, notices bell, help (F1). Bottom
  status bar: connection indicator, server, terminal, failed prints, app version.
- Command palette (Ctrl+K): jump to any permitted screen, run shell actions, or type a table code, order number
  or bill/invoice number to open it. Shortcuts: Ctrl+D dashboard, Ctrl+T tables, Ctrl+B billing, Ctrl+R reports.
- Full-screen (kiosk-like) mode for Kitchen and Waiter tablets (F11 toggle, remembered).
- Navigation is view-model-first: `INavigationService.NavigateTo<TViewModel>(parameter)`.
- The shell survives server outages: navigation still works, data reloads when connectivity returns.

## 8. Modules and screens by role

### Waiter
| Screen | Content |
|---|---|
| Table Map | Sections as tabs or groups; table tiles coloured by status with order number, guests, elapsed time; tap -> Table Details panel |
| Table Details | Table code, status, waiter, guests, current order items with per-ticket status; buttons: **NEW ORDER**, **ADD ITEMS**, **SERVED**, **REQUEST BILL**, **Occupy/Release** |
| Order Builder | Three columns: categories (left), item grid with price (centre), cart (right: qty stepper, note, modifiers, remove). Footer: subtotal, **SAVE DRAFT**, **SEND TO KITCHEN** |
| My Orders | Active orders of the logged-in waiter with status chips; "Ready" orders highlighted; tap -> Table Details |
| Notifications | Banner/toast "TABLE 05 — ORDER READY" (sound optional) |

### Kitchen
| Screen | Content |
|---|---|
| Kitchen Display | Columns **NEW** / **PREPARING** / **READY**; ticket cards with table, ticket number, waiter, elapsed timer (colour escalates at warn/late thresholds), items with qty, notes, modifiers; buttons **ACCEPT**, **START**, **READY**, **DONE**, **RECALL**; station filter from device config |
| Completed Orders | Today's completed tickets with timings |

### Cashier
| Screen | Content |
|---|---|
| Billing Queue | Pending bills ordered by request time: table, order #, waiter, total, claim badge ("BILLING-02") |
| Bill Detail | Lines (item, qty, price, line total), subtotal, discount (button), tax breakup, round-off, grand total; customer details; **FINALIZE & PRINT**, **CASH**, **CARD**, **UPI**, **SPLIT** |
| Payment | Cash: tendered quick buttons (exact, 100, 200, 500, 2000) and change; Card/UPI: reference; Split: lines until balance 0; **CONFIRM** |
| Closed Bills | Today's settled/voided bills, search, reprint, refund/void (manager approval) |

### Manager / Admin
Users, Roles, Sections/Tables, Categories, Menu Items (list + edit form + image), Modifiers, Taxes,
Stations, Discounts, Payment Methods, Devices, Settings, Audit Log, Reports, Dashboard, Backup.
Data grids with search, add/edit side panel, activate/deactivate instead of delete.

| Screen (phase) | Content |
|---|---|
| Dashboard (8) | Manager: six KPI tiles, top items, 7-day net-sales strip; waiter: own orders, sales, tables today. 60 s timer plus 5 s debounce on sale/order/ticket events |
| Reports (8) | Report list, date presets + custom range, grid with totals row, **EXPORT CSV** (same columns as the grid) |
| Roles & Permissions (9) | Roles × permissions matrix; Admin row read-only; save with optional "force re-login" |
| Devices (9) | Name, type, station, machine, version, last seen, online badge; rename, assign kitchen station, activate/deactivate |
| Audit Log (9) | Filters (search, user, terminal, action, today / 7 days / custom local dates / all), paged grid, before/after side by side, export CSV with the same filters |
| Users (9 additions) | Lockout state with **UNLOCK**, active sessions, **LOG OUT EVERYWHERE**, password hint |

Since Phase 9:
- The sidebar follows the user's **permissions** (`ModuleRegistry.ForUser`); with the default sets it matches the
  role table above.
- Manager approval: the dialog's credentials are exchanged once for a 2-minute approval token
  (`POST /api/auth/approval`); only the token travels with the void/refund/reopen/discount request.
- Login shows distinct messages for a locked account, a disabled device and too many attempts (429). A terminal
  disabled while in use signs out with the reason, on the hub's `ServerNotice` or on any 403 `DEVICE_DISABLED`.

## 9. UX guidelines

- Minimum touch target 48 x 48 px; primary action buttons at least 56 px tall.
- Base font 15 px (Comfort) / 14 px (Compact), Inter; kitchen cards 20-24 px; table tiles show code at 28 px.
- Status colours (same on all screens): Available green, Occupied blue, Ordering purple,
  Preparing amber, Ready red-accent (needs attention), Billing teal, OutOfService grey.
- One primary action per screen, placed bottom-right. Destructive actions need confirmation.
- No modal dialogs in the ordering flow except manager approval and cancellation reason.
- Light and dark themes via swapped resource dictionaries; text contrast at least WCAG AA.
- Density per terminal (Phase 11): **Comfort** (waiter, kitchen; controls 48 px) or **Compact** (billing, admin;
  controls 38 px), chosen from the terminal type unless set in This Terminal (`Density` in `settings.json`,
  `null` = automatic).
- Motion is limited to a 160 ms fade-and-slide when the page changes, skipped when Windows animation effects are
  off or the PC renders in software.
- One culture on every terminal: Indian digit grouping (`1,23,456.00`), `dd-MM-yyyy` dates, 24-hour times.
- Keyboard shortcuts for cashier (F2 cash, F3 card, F4 UPI, F9 split, Esc back).
- Every list that may exceed 200 rows is paged or virtualised.

## 10. Errors and notifications

| Situation | UI |
|---|---|
| Validation error (400) | Inline field messages |
| Business rule (409/422) | Non-blocking red banner with the server message |
| Concurrency conflict | Banner + automatic refresh of the entity |
| Connection unavailable | Red banner, action not performed, draft retained |
| Unexpected error (500) | Toast with correlation id, details in log |
| Real-time notice | Toast (bottom-right), sound for Ready/new ticket if enabled |

## 11. Printing service (Phase 7)

```
IPrintService.PrintAsync(PrintJob job)        // job = document DTO + target printer profile
  IPrintDocumentRenderer<TDocument>            // KitchenTicketDocument, InvoiceDocument, ReceiptDocument
     EscPosRenderer       -> byte[]  -> RawPrinterChannel (Windows RAW spooler) | NetworkPrinterChannel (TCP 9100)
     FlowDocumentRenderer -> FixedDocument -> WindowsPrinterChannel (PrintQueue)
PrintQueue: background channel, retries (3), failure toast with "Retry"; never blocks business actions.
```

## 12. Logging

Serilog rolling file `%LocalAppData%\HotelPOS\logs\desktop-.log` (14-day retention): application errors,
connection state changes, API failures (status, code, correlation id), print failures, navigation.
Never log tokens, passwords, or full payment references.

## 13. Offline safety behaviour (v1)

- Draft orders are written to `%LocalAppData%\HotelPOS\drafts\table-{id}.json` on every change and
  removed after a confirmed submit. An app crash never loses typed items.
- Submit while disconnected: "Connection unavailable — the order was NOT sent" and the draft stays.
- Submit timed out: automatic retry with the same `Idempotency-Key`; if still unconfirmed, a dialog
  offers **Retry** / **Keep draft**. The UI never shows "Sent" without a success response.
- Kitchen and billing screens show the last-sync time; when disconnected they show stale data with a
  red banner and refresh automatically on reconnect.
- Extension point: `IOrderSubmitter` can later be replaced by an outbox implementation that queues
  submits and replays them with the same idempotency keys.
