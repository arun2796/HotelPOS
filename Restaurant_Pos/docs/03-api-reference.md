# 03 — API Reference (planned surface)

Last updated: 2026-10-07

This is the target REST surface. Each endpoint lists the roles allowed and the phase that delivers it.
`M` = Manager, `A` = Admin, `W` = Waiter, `K` = Kitchen, `C` = Cashier, `*` = any authenticated user.
Admin always has access; Manager has access wherever `M` is listed.

## 1. General

| Item | Value |
|---|---|
| Base URL | `http://<server-ip>:5000` (from client config) |
| Content type | `application/json` |
| Authentication | `Authorization: Bearer <access token>` |
| Device | `X-Device-Id: <guid>` on every request after registration |
| Correlation | `X-Correlation-Id` (client may send; server always returns) |
| Idempotency | `Idempotency-Key: <guid>` required on endpoints marked **[I]** |
| Pagination | `?page=1&pageSize=50` -> `PagedResult<T> { items, page, pageSize, totalCount }` |
| Concurrency | DTOs include `rowVersion` (base64); updates must send it back |
| Versioning | None in v1 (`/api/...`). `GET /api/system/info` reports `apiVersion` and `minClientVersion`. |

## 2. Response envelope and error codes

```json
{ "success": false, "data": null, "message": "...", "errors": [ { "code": "...", "field": null, "message": "..." } ], "correlationId": "..." }
```

| HTTP | Code | When |
|---|---|---|
| 400 | `VALIDATION_ERROR` | FluentValidation failure (one error per field) |
| 401 | `UNAUTHENTICATED` | Missing/expired token |
| 401 | `INVALID_CREDENTIALS` | Login failed |
| 403 | `FORBIDDEN` | Role/permission not allowed |
| 403 | `ACCOUNT_LOCKED` / `ACCOUNT_DISABLED` / `DEVICE_DISABLED` | Account or device blocked |
| 404 | `NOT_FOUND` | Entity missing or inactive; unknown route for authenticated callers (anonymous callers get 401) |
| 405 | `METHOD_NOT_ALLOWED` | Route exists, HTTP method does not |
| 409 | `DUPLICATE` | Unique value already used (e.g. username) |
| 409 | `INVALID_STATE_TRANSITION` | State machine rejected the action |
| 409 | `CONCURRENCY_CONFLICT` | RowVersion mismatch; `data` contains current entity |
| 409 | `TABLE_NOT_AVAILABLE` | Occupy/open on a non-available table |
| 409 | `ORDER_LOCKED` | Order has a bill; items cannot change |
| 409 | `BILL_CLAIMED` | Another cashier/device holds the claim |
| 409 | `IDEMPOTENCY_KEY_REUSED` | Same key, different payload |
| 409 | `REQUEST_IN_PROGRESS` | Same key still processing |
| 409 | `APPROVAL_REQUIRED` / `APPROVAL_INVALID` | Manager approval missing or wrong |
| 409 | `PAYMENT_EXCEEDS_BALANCE` | Non-cash amount above balance due |
| 422 | `BUSINESS_RULE` | Other domain rule (message explains) |
| 429 | `RATE_LIMITED` | Too many login attempts |
| 500 | `SERVER_ERROR` | Unhandled exception (details only in logs) |
| none | `CONNECTION_UNAVAILABLE` | Client-side only: no response from the server (never sent by the API) |

## 3. Endpoints

### 3.1 Auth and system (Phase 1)

| Method | Route | Roles | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anon | `{username, password, deviceName?, deviceType?, machineName?, appVersion?}` -> access + refresh tokens, user, roles, permissions, `deviceId` (the device is registered or updated during login) |
| POST | `/api/auth/refresh` | anon | Rotates refresh token |
| POST | `/api/auth/logout` | anon | Revokes the given refresh token (possession is the authorization, so it works with an expired access token) |
| GET | `/api/auth/me` | * | Current user, roles, permissions |
| POST | `/api/auth/change-password` | * | |
| GET | `/health` | anon | Liveness (DB check) |
| GET | `/api/system/info` | anon | `{restaurantName, apiVersion, minClientVersion, serverTimeUtc}` — used by first-run "Test connection" |
| GET | `/api/settings` | * | Public settings (currency, names, thresholds) |
| GET/PUT | `/api/admin/settings` | A | All settings; PUT audited |
| POST | `/api/devices/register` | * | `{name, type, machineName, appVersion, stationId?}` -> `deviceId` |

### 3.2 Users and roles (Phase 1 basic, Phase 9 full)

| Method | Route | Roles | Phase |
|---|---|---|---|
| GET | `/api/users` (paged, search) | A | 1 |
| GET | `/api/users/{id}` | A | 1 |
| POST | `/api/users` | A | 1 |
| PUT | `/api/users/{id}` | A | 1 |
| POST | `/api/users/{id}/activate` / `deactivate` | A | 1 |
| POST | `/api/users/{id}/reset-password` | A | 1 |
| POST | `/api/users/{id}/unlock` | A | 9 |
| GET | `/api/roles` | A | 1 |
| GET/PUT | `/api/roles/{id}/permissions` | A | 9 |

### 3.3 Sections and tables (Phase 2)

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/sections` | * | Active sections |
| POST / PUT / DELETE | `/api/sections`, `/api/sections/{id}` | A, M | DELETE = deactivate |
| GET | `/api/tables` | * | Table map: sections with tables, status, current order summary (`?includeInactive` for admin) |
| GET | `/api/tables/{id}` | * | Table details + active order summary |
| POST / PUT / DELETE | `/api/tables`, `/api/tables/{id}` | A, M | |
| POST | `/api/tables/{id}/occupy` | A, W, C, M | `{guestCount, rowVersion}` Available -> Occupied |
| POST | `/api/tables/{id}/release` | A, W, C, M | Occupied (no order) -> Available; with order: Manager only |
| POST | `/api/tables/{id}/out-of-service` / `in-service` | A, M | |

### 3.4 Menu (Phase 3)

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/menu` | * | Compact active menu for ordering. `?version=N` returns `notModified: true` when unchanged |
| GET / POST / PUT / DELETE | `/api/categories[/{id}]` | read *, write A/M | `DELETE ?deactivateItems=true` (or `PUT` with `deactivateItems`) also deactivates the items |
| GET / POST / PUT / DELETE | `/api/menu-items[/{id}]` | read *, write A/M | Price change audited; `GET ?categoryId=&search=&includeInactive=` (inactive: A/M) |
| POST | `/api/menu-items/{id}/image` | A, M | multipart field `file`, JPEG/PNG up to 2 MB; stored as JPEG (max 512 px) and served anonymously from `/images/menu/{id}-{random}.jpg` |
| DELETE | `/api/menu-items/{id}/image` | A, M | Removes the picture |
| PATCH | `/api/menu-items/{id}/availability` | K, M, A | `{isAvailable}` sold-out toggle |
| GET / POST / PUT / DELETE | `/api/modifier-groups[/{id}]` | read *, write A/M | Options nested: `/api/modifier-groups/{id}/options[/{optionId}]` |
| GET / POST / PUT / DELETE | `/api/taxes[/{id}]` | read *, write A | |
| GET / POST / PUT / DELETE | `/api/stations[/{id}]` | read *, write A | |

### 3.5 Orders (Phase 4, 5, 6)

| Method | Route | Roles | Notes |
|---|---|---|---|
| POST **[I]** | `/api/orders` | A, W, M | `{tableId, guestCount, notes, items[], submit:boolean}` creates Draft (or Draft+Submit). Table busy -> 409 `TABLE_NOT_AVAILABLE` with the existing order summary in `data` |
| GET | `/api/orders` | *, filtered | `?status=&tableId=&waiterId=me&from=&to=` paged |
| GET | `/api/orders/active` | * | All non-terminal orders (for resync) |
| GET | `/api/orders/{id}` | * | Full order with items, modifiers, tickets, bill summary |
| PUT | `/api/orders/{id}` | W (own), M, A | `{guestCount, notes, rowVersion}` |
| PUT | `/api/orders/{id}/items` | W (own), M | Replace items while Draft |
| POST | `/api/orders/{id}/submit` | W (own), M | Draft -> Submitted, creates tickets (Phase 5) |
| POST **[I]** | `/api/orders/{id}/items` | W (own), M, A | Append items as a new batch (band B only); new tickets |
| POST | `/api/orders/{id}/items/{itemId}/cancel` | W, M | Rules by ticket status (Phase 5) |
| POST | `/api/orders/{id}/cancel` | W (own), M | `{reason}`; role rules by status |
| POST | `/api/orders/{id}/serve` | W, M, A | Completes all Ready tickets (any waiter may serve) |
| POST **[I]** | `/api/orders/{id}/request-bill` | W, C, M, A | Creates the bill (lines snapshotted, totals computed); order -> BillRequested, table -> Billing. Waiters: own orders unless `AllowAnyWaiterToEditOrders` |

### 3.6 Kitchen (Phase 5)

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/kitchen/orders` | K, M, A | `?stationId=&status=` open tickets with items; `serverTimeUtc` for timers |
| GET | `/api/kitchen/orders/{ticketId}` | K, W, M | |
| GET | `/api/kitchen/orders/completed` | K, M | `?date=` business day |
| POST | `/api/kitchen/orders/{ticketId}/accept` | K, M | New -> Accepted |
| POST | `/api/kitchen/orders/{ticketId}/start` | K, M | Accepted -> Preparing (also allowed from New: implicit accept) |
| POST | `/api/kitchen/orders/{ticketId}/ready` | K, M | Preparing -> Ready |
| POST | `/api/kitchen/orders/{ticketId}/complete` | K, W, M | Ready -> Completed |
| POST | `/api/kitchen/orders/{ticketId}/recall` | K, M | Ready -> Preparing |

### 3.7 Billing (Phase 6)

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/billing/pending` | C, M | Open/Finalized bills with claim info |
| GET | `/api/billing/{id}` | C, M, W (own order) | Bill with lines, totals, payments |
| GET | `/api/billing/closed` | C, M | `?date=&search=` settled/voided |
| POST | `/api/billing/{id}/claim` | C, M | Soft lock (5 min, refreshed by every cashier action); 409 `BILL_CLAIMED` naming the holder. `{override:true}` lets a manager take over (audited) |
| POST | `/api/billing/{id}/release` | C, M | |
| PUT | `/api/billing/{id}/discount` | C, M | `{discountId? | type,value, reason, approval?}`; recalculates |
| DELETE | `/api/billing/{id}/discount?rowVersion=` | C, M | |
| PUT | `/api/billing/{id}/customer` | C, M | name/phone/GSTIN for invoice |
| POST | `/api/billing/{id}/finalize` | C, M | Assigns invoice number, locks discount; Order -> Billed |
| POST **[I]** | `/api/billing/{id}/payments` | C, M | `{paymentMethodId, amount, tendered?, reference?}`; implicit finalize; settles when fully paid |
| POST | `/api/billing/{id}/close` | C, M | Only when `AutoCloseOnFullPayment=false` |
| POST | `/api/billing/{id}/reopen` | C, M | `{reason, approval?, rowVersion}` unpaid bills; voids the bill, Order -> Served, items editable. Approval required once an invoice number exists |
| POST | `/api/billing/{id}/void` | C, M | Unpaid finalized bill, `{reason, approval, rowVersion}`; a cashier must include a manager approval. Order cancelled, table released |
| POST **[I]** | `/api/billing/{id}/refunds` | C, M | `{paymentId, amount, reason, approval, rowVersion}` on settled bills; a cashier must include a manager approval |
| GET / POST / PUT / DELETE | `/api/discounts[/{id}]` | read C/M, write A/M | |
| GET / POST / PUT / DELETE | `/api/payment-methods[/{id}]` | read *, write A | |

### 3.8 Printing (Phase 7)

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/print/kot/{ticketId}` | K, W, M | `KitchenTicketDocument` |
| GET | `/api/print/invoice/{billId}` | C, M, W | `InvoiceDocument` (tax breakup, GSTIN, footer) |
| GET | `/api/print/receipt/{paymentId}` | C, M | `ReceiptDocument` |
| POST | `/api/print/reprints` | C, M | `{documentType, entityId, reason}` audit record |

### 3.9 Reports (Phase 8)

All `GET`, roles A/M, params `from`, `to` (business days), optional `format=csv`.

```
/api/reports/dashboard
/api/reports/sales/daily        /api/reports/sales/monthly?year=
/api/reports/sales/items        /api/reports/sales/categories
/api/reports/payments           /api/reports/cancellations
/api/reports/discounts          /api/reports/taxes
/api/reports/staff/waiters      /api/reports/kitchen
```

### 3.10 Admin operations (Phase 9, 10)

| Method | Route | Roles | Phase |
|---|---|---|---|
| GET | `/api/admin/audit-logs` (paged, filters) | A, M | 9 |
| GET | `/api/admin/devices` | A | 9 |
| PUT | `/api/admin/devices/{id}` ; POST `.../deactivate` / `activate` | A | 9 |
| POST | `/api/admin/backup` ; GET `/api/admin/backups` | A | 10 |
| GET | `/api/admin/logs?lines=500` | A | 10 |

## 4. Idempotency protocol (client side)

1. When the user starts a critical action, the client generates a GUID and stores it with the pending
   action (e.g. in the local draft file).
2. Send the request with `Idempotency-Key`. On timeout or connection error, **retry with the same key**
   (3 attempts, backoff 1s/3s/5s).
3. If the server returns the stored response, the client treats it as success.
4. If all retries fail, show: "Could not confirm that the order was saved. Check the connection and
   press Retry." The key is kept until a definitive response arrives.
5. A new key is generated only after a definitive success or when the user discards the action.
6. A replayed response carries the header `Idempotent-Replayed: true`. Server errors (5xx) are not stored, so the
   retry is processed again.

## 5. Manager approval protocol

Actions marked "approval" accept an `approval` object:

```json
{ "approverUsername": "manager1", "approverPassword": "..." }
```

The server validates the credentials, checks the approver's role, records `ApprovedBy` on the entity and
in the audit log. The approver's password is never logged. If the caller already has the Manager role
the object is optional. Phase 9 may replace this with short-lived approval tokens.
