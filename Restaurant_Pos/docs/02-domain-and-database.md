# 02 — Domain Model and Database

Last updated: 2026-10-07

## 1. Conventions

- Database: PostgreSQL via Npgsql. Table and column names are PascalCase (quote them in raw SQL).
- Primary keys: `int` identity (`Devices.Id` and `IdempotencyRecords.Key` are `uuid`).
- Every entity derives from `BaseEntity`: `Id`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`
  (set by an EF `SaveChanges` interceptor from `ICurrentUser` and `IClock`).
- `RowVersion` (the `xmin` system column, a `uint` in .NET; no schema column) on: `Tables`, `Orders`, `Bills`, `MenuItems`, `Users`, `KitchenOrders`.
- Master data (users, tables, menu items, categories, taxes, discounts, payment methods) is never
  deleted; it is deactivated (`IsActive = false`). Transactional data is never deleted.
- Monetary columns: `decimal(18,2)`. Rates: `decimal(5,2)`. Quantities: `int`.
- Timestamps: `timestamp(3) with time zone`, always written in UTC.
- Strings: `character varying(n)` with explicit max lengths (names 100, codes 20, notes 500), JSON as `text`.
  Comparisons are case-sensitive: case-insensitive rules upper-case both sides (or use a normalized column).
- Snapshot on transactional rows what must not change afterwards (item name, unit price, tax rate,
  modifier prices, station).
- Enums stored as `int` with a check constraint listing valid values.

## 2. Entity catalogue

### 2.1 Identity and system (Phase 1, extended in Phase 9)

| Entity | Key columns |
|---|---|
| `Users` | `Username` (50), `NormalizedUsername` (upper-case, unique, so logins are case-insensitive), `DisplayName`, `PasswordHash`, `IsActive`, `MustChangePassword`, `FailedLoginCount`, `LockedUntil`, `LastLoginAt`, `RowVersion` |
| `Roles` | `Name` (unique: Admin, Manager, Waiter, Kitchen, Cashier), `Description`, `IsSystem` |
| `UserRoles` | `UserId`, `RoleId` (composite PK) |
| `RolePermissions` (Phase 9) | `RoleId`, `Permission` (string constant) |
| `RefreshTokens` | `UserId`, `TokenHash`, `DeviceId`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`, `CreatedByIp` |
| `Devices` | `Id` (guid), `Name` (unique, e.g. WAITER-01), `Type` (enum), `MachineName`, `AppVersion`, `StationId?`, `LastSeenAt`, `IsActive`, `RegisteredAt` |
| `Settings` | `Key` (PK, 100), `Value` (max), `DataType` (String/Int/Decimal/Bool/Json), `Description`, `IsPublic` (visible to all clients) |
| `AuditLogs` | `UserId?`, `UserName`, `DeviceId?`, `DeviceName`, `MachineName`, `IpAddress`, `Action` (e.g. `Order.Cancelled`), `EntityType`, `EntityId`, `OldValues` (JSON), `NewValues` (JSON), `CorrelationId`, `Timestamp` |
| `IdempotencyRecords` | `Key` (guid PK), `UserId`, `Route`, `RequestHash`, `StatusCode`, `ResponseBody`, `CreatedAt`, `ExpiresAt`, `InProgress` |

### 2.2 Floor (Phase 2)

| Entity | Key columns |
|---|---|
| `Sections` | `Name` (unique), `SortOrder`, `IsActive` |
| `Tables` | `Code` (unique, e.g. T01), `Name?`, `SectionId`, `Capacity`, `Status` (enum, stored), `CurrentOrderId?`, `OccupiedAt?`, `GuestCount?`, `IsActive`, `RowVersion` |

### 2.3 Menu (Phase 3)

| Entity | Key columns |
|---|---|
| `Categories` | `Name` (unique), `SortOrder`, `ImagePath?`, `IsActive` |
| `PreparationStations` | `Name`, `Code` (unique), `SortOrder`, `IsActive` |
| `Taxes` | `Name`, `Code` (unique), `RatePercent`, `IsActive` |
| `MenuItems` | `CategoryId`, `Name`, `Code?` (unique when set), `Description?`, `Price`, `TaxId?`, `PreparationStationId`, `IsAvailable` (sold-out toggle), `IsActive`, `ImagePath?`, `SortOrder`, `RowVersion` |
| `ModifierGroups` | `Name`, `MinSelections`, `MaxSelections`, `IsActive` |
| `ModifierOptions` | `ModifierGroupId`, `Name`, `PriceDelta`, `SortOrder`, `IsActive` |
| `MenuItemModifierGroups` | `MenuItemId`, `ModifierGroupId` (composite PK), `SortOrder` |

Unique: `MenuItems (CategoryId, Name)`.

### 2.4 Orders (Phase 4)

| Entity | Key columns |
|---|---|
| `Orders` | `OrderNumber` (int from SEQUENCE, unique), `TableId`, `WaiterId`, `GuestCount`, `Status` (enum), `Notes?`, `SubmittedAt?`, `ServedAt?`, `BillRequestedAt?`, `ClosedAt?`, `CancelledAt?`, `CancelledBy?`, `CancelReason?`, `RowVersion` |
| `OrderItems` | `OrderId`, `BatchNumber` (1..n), `MenuItemId`, `ItemName` (snapshot), `UnitPrice` (snapshot), `Quantity`, `Notes?`, `TaxId?`, `TaxRatePercent` (snapshot), `PreparationStationId` (snapshot), `Status` (Draft/Sent/Cancelled), `KitchenOrderId?`, `CancelledBy?`, `CancelReason?` |
| `OrderItemModifiers` | `OrderItemId`, `ModifierOptionId`, `Name` (snapshot), `PriceDelta` (snapshot) |

Constraints: at most one **active** order per table — partial unique index (EF `HasFilter`) on
`Orders(TableId) WHERE Status NOT IN (Paid, Completed, Cancelled)`.

### 2.5 Kitchen (Phase 5)

| Entity | Key columns |
|---|---|
| `KitchenOrders` | `OrderId`, `TicketNumber` (string `1025-1`, unique), `BatchNumber`, `PreparationStationId`, `Status` (enum), `CreatedAt`, `AcceptedAt?`, `AcceptedBy?`, `StartedAt?`, `ReadyAt?`, `CompletedAt?`, `CompletedBy?`, `CancelledAt?`, `RowVersion` |
| `KitchenOrderItems` | `KitchenOrderId`, `OrderItemId`, `Quantity`, `IsCancelled` |

### 2.6 Billing (Phase 6)

| Entity | Key columns |
|---|---|
| `Discounts` | `Name`, `Type` (Percentage/FixedAmount), `Value`, `RequiresApproval`, `IsActive` |
| `PaymentMethods` | `Name`, `Code` (unique: CASH, CARD, UPI...), `RequiresReference`, `IsActive`, `SortOrder` |
| `Bills` | `BillNumber` (int, unique, assigned at creation), `InvoiceNumber?` (string, unique, assigned at finalisation), `OrderId` (unique among non-voided bills), `TableCode` (snapshot), `WaiterId`, `Status` (Open/Finalized/Settled/Voided), `PaymentStatus` (enum), `Subtotal`, `DiscountId?`, `DiscountType?`, `DiscountValue?`, `DiscountAmount`, `DiscountReason?`, `DiscountApprovedBy?`, `TaxableAmount`, `TaxAmount`, `RoundOff`, `GrandTotal`, `PaidAmount`, `RefundedAmount`, `ServiceChargeAmount?` (reserved), `CustomerName?`, `CustomerPhone?`, `CustomerGstin?`, `ClaimedByUserId?`, `ClaimedByDeviceId?`, `ClaimedAt?`, `ClaimExpiresAt?`, `FinalizedAt?`, `SettledAt?`, `SettledBy?`, `VoidedAt?`, `VoidReason?`, `RowVersion` |
| `BillItems` | `BillId`, `OrderItemId`, `ItemName`, `Quantity`, `UnitPrice`, `ModifiersAmount`, `LineSubtotal`, `DiscountShare`, `TaxRatePercent`, `TaxAmount`, `LineTotal` |
| `Payments` | `BillId`, `PaymentMethodId`, `Amount`, `TenderedAmount?`, `ChangeAmount?`, `Reference?`, `Status` (Completed/Refunded/Voided), `ReceivedBy`, `DeviceId?`, `PaidAt`, `IdempotencyKey`, `RefundOfPaymentId?`, `RefundReason?` (refund rows carry a negative `Amount`) |
| `InvoiceCounters` | `Period` (PK: `2026`, `202610` or `ALL`), `LastNumber` |

## 3. Enums (defined in `HotelPOS.Contracts.Enums`)

All enums are numbered from 1, so the default value 0 is never a valid state (validators reject it).

```
OrderStatus        Draft, Submitted, Accepted, Preparing, Ready, Served, BillRequested, Billed, Paid,
                   Completed, Cancelled
OrderItemStatus    Draft, Sent, Cancelled
KitchenOrderStatus New, Accepted, Preparing, Ready, Completed, Cancelled
TableStatus        Available, Occupied, Ordering, Preparing, Ready, Billing, OutOfService
BillStatus         Open, Finalized, Settled, Voided
PaymentStatus      Pending, PartiallyPaid, Paid, Refunded, Cancelled
PaymentRecordStatus Completed, Refunded, Voided
DiscountType       Percentage, FixedAmount
DeviceType         Waiter, Kitchen, Billing, Admin
SettingDataType    String, Int, Decimal, Bool, Json, Time (HH:mm)
```

## 4. State machines

### 4.1 Order

Statuses are grouped into bands. Within the kitchen band the status is **derived from tickets** and may
move in both directions (a dessert batch added after mains were served re-enters the kitchen). Across
bands transitions are explicit and forward-only, with one audited exception (reopen).

```
Band A (editable) : Draft
Band B (kitchen)  : Submitted <-> Accepted <-> Preparing <-> Ready <-> Served   (derived)
Band C (billing)  : BillRequested -> Billed -> Paid -> Completed
Terminal          : Completed, Cancelled
```

| From | To | Trigger | Who |
|---|---|---|---|
| Draft | Submitted | `submit` | Waiter, Manager |
| Draft | Cancelled | `cancel` | Waiter (own), Manager |
| Band B (any) | Band B (derived) | ticket events, `append items`, `serve` | Server |
| Submitted, Accepted | Cancelled | `cancel` | Waiter (own order), Manager |
| Preparing, Ready | Cancelled | `cancel` with reason | Manager only |
| Served (or Ready) | BillRequested | `request-bill` | Waiter, Cashier, Manager |
| BillRequested | Billed | `finalize` (explicit or implicit on first payment) | Cashier, Manager |
| BillRequested, Billed | Served | `reopen` (bill voided, items editable again) | Cashier, Manager (audited) |
| Billed | Paid | payment reaches grand total | Server |
| Paid | Completed | auto-close (setting) or `close` | Server / Cashier |
| Billed (unpaid) | Cancelled | `void bill` | Manager |
| Paid, Completed | anything backwards | — | **Never** (use refund) |

Derivation of the band-B status from the order's non-cancelled tickets:

```
all tickets Completed                          -> Served
all open tickets Ready or Completed (>=1 Ready)-> Ready
any ticket Preparing                           -> Preparing
any ticket Accepted and none Preparing         -> Accepted
otherwise                                      -> Submitted
```

### 4.2 Kitchen ticket

```
New -> Accepted -> Preparing -> Ready -> Completed
 |        |           |          |
 +--------+-----------+----------+--> Cancelled (all items cancelled, or order cancelled)
Ready -> Preparing  ("recall", kitchen only)
```

`Completed` means handed over / served. Kitchen (bump) or Waiter (serve) can complete a Ready ticket.

### 4.3 Table status (stored, set by server in the same transaction)

| Situation | Table status |
|---|---|
| No active order, not occupied | Available |
| Manually occupied (guests seated), no order or order Served | Occupied |
| Active order in Draft | Ordering |
| Order Submitted / Accepted / Preparing (no ticket ready) | Preparing |
| Any ticket Ready and not yet Completed | Ready |
| Order BillRequested / Billed | Billing |
| Order Paid / Completed / Cancelled and no other order | Available (released) |
| Admin flag | OutOfService |

### 4.4 Bill and payment status

```
Bill.Status:      Open -> Finalized -> Settled
                  Open/Finalized -> Voided
Bill.PaymentStatus: Pending -> PartiallyPaid -> Paid -> Refunded (full refund)
                    Pending/PartiallyPaid -> Cancelled (bill voided)
```

## 5. Numbering

| Number | Rule |
|---|---|
| `OrderNumber` | `nextval('"OrderNumbers"')` (SEQUENCE starting 1001, EF `HasSequence`). Gaps allowed. |
| `TicketNumber` | `"{OrderNumber}-{BatchNumber}"` plus station suffix when a batch spans stations: `1025-1-BAR`. |
| `BillNumber` | SEQUENCE `"BillNumbers"`. Internal. |
| `InvoiceNumber` | `"{InvoicePrefix}{yyyy}{MM}-{000000}"` from the period's `InvoiceCounters` row, incremented with `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` (row lock until commit) inside the finalisation transaction. Gap-free. Prefix and reset policy (yearly/monthly/never) from settings. |

## 6. Money and bill calculation (`BillCalculator`, pure domain code)

```
for each non-cancelled order item:
    lineSubtotal   = (unitPrice + sum(modifier priceDelta)) * quantity
subtotal           = sum(lineSubtotal)
discountAmount     = Percentage: round(subtotal * value / 100, 2)
                     FixedAmount: min(value, subtotal)
discountShare(i)   = round(discountAmount * lineSubtotal(i) / subtotal, 2); last line absorbs remainder
taxable(i)         = lineSubtotal(i) - discountShare(i)
tax(i)             = round(taxable(i) * taxRate(i) / 100, 2)
taxableAmount      = sum(taxable)        taxAmount = sum(tax)
grandTotalRaw      = taxableAmount + taxAmount
roundOff           = RoundOffTotals ? round(grandTotalRaw, 0) - grandTotalRaw : 0
grandTotal         = grandTotalRaw + roundOff
```

Rounding: `MidpointRounding.AwayFromZero`. Tax breakup for the invoice is grouped by `TaxRatePercent`
(e.g. GST 5% shown as CGST 2.5% + SGST 2.5% when `TaxSplitDisplay = CGST_SGST`).
Prices are tax-exclusive in v1 (`PricesIncludeTax` setting reserved).

## 7. Indexes and constraints (minimum)

- `Users(Username)` unique; `Roles(Name)` unique; `Devices(Name)` unique
- `Tables(Code)` unique; `Tables(SectionId)`
- `MenuItems(CategoryId, Name)` unique; `MenuItems(PreparationStationId)`; `Taxes(Code)` unique
- `Orders(OrderNumber)` unique; `Orders(TableId) WHERE active` unique; `Orders(Status)`;
  `Orders(WaiterId, CreatedAt)`
- `OrderItems(OrderId)`; `KitchenOrders(OrderId)`; `KitchenOrders(PreparationStationId, Status)`;
  `KitchenOrders(TicketNumber)` unique
- `Bills(OrderId) WHERE Status <> Voided` unique; `Bills(BillNumber)` unique; `Bills(InvoiceNumber)` unique partial (where not null);
  `Bills(Status, CreatedAt)`
- `Payments(BillId)`; `Payments(IdempotencyKey)` unique
- `AuditLogs(Timestamp)`, `AuditLogs(EntityType, EntityId)`, `AuditLogs(UserId)`
- `IdempotencyRecords(ExpiresAt)`
- Check constraints: `Price >= 0`, `Quantity > 0`, `RatePercent BETWEEN 0 AND 100`,
  `Amount > 0` on payments (`< 0` on refund rows), enum ranges

## 8. Seed data (first migration + `DbSeeder`)

- Roles: Admin, Manager, Waiter, Kitchen, Cashier (`IsSystem = true`)
- User `admin` / password from installer (dev default `Admin@123`, `MustChangePassword = true`)
- Payment methods: CASH, CARD, UPI
- Preparation station: `MAIN` (Main Kitchen)
- Settings: `RestaurantName`, `Address`, `Gstin`, `CurrencySymbol` (₹), `InvoicePrefix` (INV-),
  `InvoiceResetPolicy` (Yearly), `RoundOffTotals` (true), `BusinessDayStartTime` (04:00),
  `KitchenWarnMinutes` (10), `KitchenLateMinutes` (20), `AutoCloseOnFullPayment` (true),
  `ReceiptFooter`, `MenuVersion` (1), `TaxSplitDisplay` (CGST_SGST), `AllowAnyWaiterToEditOrders` (false),
  `MaxCashierDiscountPercent` (10), `AllowBillBeforeReady` (false)
- Development-only seed: sample sections, tables, categories, items, a waiter/kitchen/cashier user

## 9. Migration strategy

- One EF Core migration per phase at minimum, named `Phase0N_<Topic>` (e.g. `Phase02_Tables`).
- `Database:AutoMigrate = true` applies pending migrations at API startup (on-prem convenience);
  `dotnet ef database update` remains available for technicians.
- Never edit an applied migration; add a new one.
- Destructive column changes require a data-migration step and a note in the phase document.
