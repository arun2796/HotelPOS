# 04 — Real-time Events (SignalR)

Last updated: 2026-10-14

## 1. Hub

| Item | Value |
|---|---|
| Path | `/hubs/restaurant` |
| Transport | WebSockets (fallback long polling) |
| Auth | JWT bearer; the client passes the access token through `AccessTokenProvider`. The server reads `access_token` from the query string for hub requests only. |
| Authorization | `[Authorize]` on the hub; group membership derived from the user's roles and the device's station |
| Server -> client | `Clients.Group(...).SendAsync(eventName, payload)` |
| Client -> server | Only `Ping()` (presence) and `JoinStation(stationId)` / `LeaveStation(stationId)` for kitchen screens. All business actions go through REST. |

The hub is a notification channel. **It never performs business operations.**

## 2. Groups

| Group | Members | Purpose |
|---|---|---|
| `role:waiter`, `role:kitchen`, `role:cashier`, `role:manager`, `role:admin` | All connections of users with that role | Broadcast by audience |
| `station:{id}` | Kitchen displays configured for that station (`JoinStation`) | Ticket events per station |
| `user:{id}` | All connections of one user | Waiter-specific notifications ("your table is ready") |
| `device:{id}` | One device | Targeted notices (deactivated, config changed) |

Membership is assigned in `OnConnectedAsync` from the token claims and the `X-Device-Id`/`stationId`
query parameter, and removed automatically on disconnect.

## 3. Event catalogue

Every payload extends:

```csharp
record RealtimeEvent(Guid EventId, DateTime OccurredAtUtc, int EntityId, string? EntityVersion);
```

Payloads are intentionally small. Clients call REST for full details (`GET /api/orders/{id}` etc.).

| Event | Audience | Payload (beyond base) | Trigger | Phase |
|---|---|---|---|---|
| `TableStatusChanged` | all roles | `tableId, tableCode, status, orderId?, orderNumber?` | occupy/release/order lifecycle/bill lifecycle | 2 |
| `MenuChanged` | all roles | `menuVersion` | any category/item/modifier/tax/station change or availability toggle | 3 |
| `OrderCreated` | kitchen, cashier, manager, admin | `orderId, orderNumber, tableCode, waiterName, itemCount` | order submitted | 4 |
| `OrderUpdated` | kitchen, cashier, manager, admin, user:{waiter} | `orderId, orderNumber, status, changeType` (ItemsAppended, GuestsChanged, ItemCancelled, StatusDerived) | | 4 |
| `OrderCancelled` | kitchen, cashier, manager, admin, user:{waiter} | `orderId, orderNumber, tableCode, reason` | | 4 |
| `KitchenTicketCreated` | `station:{id}`, role:kitchen | `ticketId, ticketNumber, orderId, tableCode, stationId, itemCount` | submit / append items | 5 |
| `KitchenTicketUpdated` | `station:{id}`, role:kitchen, user:{waiter}, manager | `ticketId, ticketNumber, orderId, tableCode, status` | accept/start/ready/complete/recall/item cancel | 5 |
| `OrderAccepted` | user:{waiter}, role:waiter, cashier, manager | `orderId, orderNumber, tableCode` | derived order status becomes Accepted | 5 |
| `OrderPreparing` | same | same | derived status Preparing | 5 |
| `OrderReady` | same | `+ readyTicketNumbers[]` | any ticket becomes Ready (waiter shows "TABLE 05 — ORDER READY") | 5 |

The four order-progress events share one payload (`OrderProgressEvent`: `orderId, orderNumber, tableId, tableCode,
waiterId, status, readyTicketNumbers`). A kitchen screen that calls `JoinStation` leaves the `role:kitchen` group, so
it receives only its station's ticket events; `LeaveStation` puts it back. The client re-joins after every reconnect.
| `OrderServed` | cashier, manager, role:waiter | `orderId, orderNumber, tableCode` | all tickets completed | 5 |
| `BillRequested` | role:cashier, manager | `billId, billNumber, orderId, orderNumber, tableCode, waiterName, grandTotal` | request-bill | 6 |
| `BillUpdated` | role:cashier, manager, user:{waiter} | `billId, status, paymentStatus, claimedByDevice?, grandTotal, paidAmount, changeType` | claim/release/discount/finalize/partial payment/reopen/void | 6 |
| `PaymentCompleted` | role:cashier, role:waiter, manager, admin | `billId, orderId, tableCode, invoiceNumber, grandTotal` | bill settled | 6 |
| `DeviceStatusChanged` | role:admin, role:manager | `deviceId, deviceName, isOnline, isActive, timestampUtc` | device update/activate/deactivate; first hub connection of a device opens / its last one closes | 9 |
| `ServerNotice` | `device:{id}` (Phase 9); all (Phase 10) | `message, code?` | device deactivated (`code = DEVICE_DISABLED`); admin broadcast and shutdown warning in Phase 10 | 9, 10 |
| `ShiftChanged` | role:manager, role:admin, `device:{id}` of the shift | `shiftId, deviceId, deviceName, cashierName, businessDay, status, changeType` (Opened/MovementAdded/Closed/HandedOver/ForceClosed/Reopened) | shift opened, cash movement, close, handover, force close, reopen | 13 |
| `BusinessDayClosed` | all | `businessDay, status, changedBy` (`entityId` = day number) | Day End closed the day | 13 |
| `BusinessDayReopened` | all | same | a manager reopened a closed day | 13 |
| `PeriodStatusChanged` | all | `PeriodStatusChangedEvent` (kind, period, label, from, to, closed, changedBy, reason) | an administrator closed or reopened a GST month or financial year | 15 |
| `PrintFailed` | the reporting device + Admin, Manager roles | `PrintFailedEvent` (documentType, documentId, copyNumber, output, error, deviceName) | a terminal reported a reserved print copy as failed (queued after commit) | 20 |
| `PaymentFailed` | the paying device only | `PaymentFailedEvent` (billId, code, message) | a payment was rejected for a validation or concurrency reason; nothing was saved | 20 |
| `DayEndDue` | Admin, Manager roles | `DayEndDueEvent` (businessDay, reminderTime) | the `DayEndReminderTime` setting (default 23:00) passed and the business day is not closed; once per business day | 20 |
| `FloorLayoutChanged` | all connected clients | `FloorLayoutChangedEvent` (sectionId) | an Admin saved a section's floor layout (queued after commit); clients reload `GET /api/sections/{id}/layout` | 22 |

Event names are constants in `HotelPOS.Contracts.Realtime.HubEvents`. Phase 13 resync: the shift bar reloads
`GET /api/shifts/current` on reconnect; screens re-read on navigation.

### 3.1 Client handling: notification bell (Phase 20.1)

Both clients turn these events into bell entries and toasts. The mapping and the role filter live in one place,
`HotelPOS.Contracts.Realtime.NotificationMapper`; the desktop `NotificationRouter` and the browser/Android
`NotificationRouter` only subscribe, store and navigate. A hub event that is not in this table is not surfaced.

| Event | Bell kind | Shown to | Level | Tap opens (desktop / web) |
|---|---|---|---|---|
| `OrderAccepted` | `OrderAccepted` | waiter, own orders only (`WaiterId` = signed-in user) | Info | Tables, that table / `/tables/{id}` |
| `OrderPreparing` | `OrderPreparing` | same | Info | same |
| `OrderReady` | `OrderReady` | same | Warning | same |
| `KitchenTicketCreated` | `TicketCreated` | kitchen | Info | Kitchen Display / `/kitchen` |
| `BillRequested` | `BillRequested` | cashier, manager, admin | Warning | Billing / none (desktop only) |
| `BillUpdated` with `changeType = Claimed` | `BillClaimed` | cashier, manager, admin | Info | Billing / none |
| `PaymentCompleted` | `PaymentCompleted` | cashier, manager, admin | Info | Billing / none |
| `DeviceStatusChanged` | `DeviceOnline` / `DeviceOffline` | manager, admin | Info / Warning | Devices (admin) / none |
| `ServerNotice` (except `DEVICE_DISABLED`) | `ServerNotice` | every role | the notice's own level | none |
| `PeriodStatusChanged` | `PeriodChanged` | every role | Warning on close, Info on reopen | none |

Rules shared by both clients: the list keeps the last 50 entries; an entry has a dedup key (for example
`ready:{orderId}`) and a repeat with the same key within 30 seconds updates the existing entry (moves it to the top,
unread again) instead of adding another or toasting again; each entry has read and acknowledged state. Desktop keeps the
list for the session; the browser persists it in `IBrowserStorage` (`notifications.v1`) and clears it on sign-out.
`TicketDelayed`, `DayEndWarning`, `PrintFailed` and `PaymentFailed` kinds exist in `NotificationKind` for step 20.3 and
have no mapping yet.

## 4. Publishing rules (server)

1. Services call `IRealtimeNotifier` **after** the database transaction commits, never inside it.
2. Publishing failures are logged and swallowed; they must not fail the HTTP request.
3. One business action may emit several events (e.g. payment -> `PaymentCompleted` +
   `TableStatusChanged` + `OrderUpdated`).
4. Events carry `EntityVersion` (RowVersion base64) so a client can ignore stale events it already
   holds.
5. No event contains prices for a different role than the one that may see them (kitchen events never
   include amounts).

## 5. Client connection lifecycle

```
Startup / login
   -> build HubConnection (WithAutomaticReconnect(InfiniteRetryPolicy: 0s,2s,5s,10s, then every 15s))
   -> Start()  (retry loop while app is running)
   -> on Reconnecting : status = Reconnecting (yellow)
   -> on Reconnected  : status = Connected (green); ResyncCurrentModule()
   -> on Closed       : status = Disconnected (red); schedule Start() again in 5s
Heartbeat: REST GET /health every 30s when the hub is down (so REST-only outages are also visible)
```

Status indicator rules:

| Indicator | Meaning |
|---|---|
| 🟢 Connected | Hub connected and last REST call succeeded |
| 🟡 Reconnecting… | Hub reconnecting; REST may still work |
| 🔴 Disconnected | Hub closed and/or last health check failed; actions that need the server show "Connection unavailable" |

### Resync policy (source of truth = API)

| Module | On `Reconnected` |
|---|---|
| Waiter table map | `GET /api/tables` |
| Waiter order screen | `GET /api/orders/{currentId}` (draft kept locally if not yet submitted) |
| Kitchen display | `GET /api/kitchen/orders?stationId=` (replace lists) |
| Billing queue | `GET /api/billing/pending` + `GET /api/billing/{openId}` |
| Admin dashboard | `GET /api/reports/dashboard` |
| Menu (all) | `GET /api/menu?version=` |

Events received while a resync is running are queued and applied afterwards (or simply trigger another
fetch). Handlers are idempotent: applying the same event twice has no effect.

Desktop (Phase 20.4): `ReconnectCoordinator` refreshes the page on screen (the billing queue when it is open), reloads the restaurant name and adds one Info bell entry "Reconnected — refreshed at HH:mm" (a repeat replaces it). `MenuCache` and the shift bar resync on their own. Server notices are push-only and cannot be replayed after an outage. Web: a write refused with 409 `CONCURRENCY_CONFLICT` shows "Changed on another device — reloaded", re-fetches the table or order, and keeps the draft and its pending idempotency key.

## 6. Presence

`ConnectionTracker` (in-memory, singleton) maps connection id -> (userId, deviceId, roles, stationId).
`OnConnected/OnDisconnected` update `Devices.LastSeenAt` and emit `DeviceStatusChanged` to admins and
managers when a device's first connection opens or its last one closes (Phase 9). Admin "Connected devices"
screen reads `GET /api/admin/devices`, which merges the DB list with the tracker's online set.

Deactivating a device (Phase 9) sends `ServerNotice` (`code = DEVICE_DISABLED`) to `device:{id}`, waits
500 ms, then aborts every open connection of that device. A connection is put in the tracker before its device
is checked, so a deactivation that commits during the check still finds and closes it.
