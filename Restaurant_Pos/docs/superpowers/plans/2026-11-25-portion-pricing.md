# Portion-based pricing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Items carry 1-12 priced portions (Quarter/Half/Full...); the chosen portion and its price are frozen on order and bill lines and shown on KOT, bill, invoice and reports; waiters and cashiers pick a portion in one tap.

**Architecture:** New `MenuItemPortions` table owned by `MenuItem` (`MenuItem.Price` mirrors the default portion). `OrderItem`/`BillItem` get frozen `Portion*` fields plus a composed `DisplayName`; the server prices from the portion, never the client. All API changes are additive.

**Tech Stack:** .NET 10, EF Core + Postgres, MediatR-style handlers, WPF (MVVM Toolkit), Blazor (web + MAUI Android), xUnit + FluentAssertions + bUnit + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-11-25-portion-pricing-design.md` (read it first; this plan only adds names and tests).

## Global Constraints
- Paths are under `Restaurant_Pos\`. Run tests per project: `dotnet test tests\<Project> --nologo`. If the running Windows app locks `bin`, test in a scratch copy of the source (see memory: windows-shell-gotchas); never close the user's app.
- Stock: one portion sold takes **1** off the item count (`OrderStock` is not changed).
- Limits: `MenuLimits.MaxPortions = 12`, `PortionNameMaxLength = 40`, `PortionQuantityMax = 100000`, units fixed list `GM, KG, ML, L, PCS, PLATE`.
- New parameters on `OrderItem`, `MenuItem`, `BillLineInput` constructors are **optional / trailing** so the existing test helpers keep compiling.
- Images: keep JPEG/PNG, 2 MB, existing `IMenuImageStore`; no new formats.
- No mock/demo data added to `DbSeeder`. Migration name `Phase32_ItemPortions`.
- UI text says "Portion"; stock text ("portion quota") is not renamed.
- Never commit unless the owner asks; uncommitted seat/dashboard work already sits in the tree.

## Review Focus
- A draft whose portion was switched off or deleted is refused on save with the item and portion named; it is never switched to the default.
- A line with no `PortionId` on an item with 2+ active portions is refused; on a one-active-portion item it uses that portion.
- A price edit after an order is sent leaves that order, its bill and invoice unchanged.
- Portion rules: duplicate names (case-insensitive), zero or two defaults, no active portion, default inactive, 13th portion, negative price are all rejected with a row-level message.
- Parcel + portion reads "Chicken Biryani (Half) (Parcel)"; an item with Half and Full where Full is off still stores "Half" and shows no chips.

---

### Task 1: Domain model and display name
**Files:**
- Create: `src/HotelPOS.Domain/Menu/MenuItemPortion.cs`, `src/HotelPOS.Contracts/Menu/ItemDisplayName.cs`
- Modify: `src/HotelPOS.Domain/Menu/MenuItem.cs`, `src/HotelPOS.Domain/Orders/OrderItem.cs`, `src/HotelPOS.Domain/Billing/Bill.cs` (`BillItem`), `src/HotelPOS.Domain/Billing/BillCalculator.cs` (`BillLineInput`), `src/HotelPOS.Contracts/Menu/MenuContracts.cs` (`MenuLimits`)
- Test: `tests/HotelPOS.Domain.Tests/Menu/MenuItemPortionTests.cs` (new), `Orders/OrderTests.cs`, `Billing/BillTests.cs`

**Interfaces:**
- Produces: `sealed class MenuItemPortion : BaseEntity { int MenuItemId; string Name; decimal? Quantity; string? Unit; decimal Price; bool IsDefault; bool IsActive; int SortOrder }`
- `record PortionSpec(int? Id, string Name, decimal? Quantity, string? Unit, decimal Price, bool IsDefault, bool IsActive, int SortOrder)` in `Domain.Menu`
- `MenuItem.Portions : IReadOnlyCollection<MenuItemPortion>`; `void SetPortions(IReadOnlyList<PortionSpec> specs)` (validates, keeps ids of existing rows, sets `Price` = default's price); `void SetDefaultPrice(decimal price)`; the existing constructor also adds one `"Regular"` default portion at `price`.
- `OrderItem`: `int? PortionId, string? PortionName, decimal? PortionQuantity, string? PortionUnit`, `void SetPortion(int portionId, string name, decimal? quantity, string? unit)`, `string DisplayName`.
- `BillItem`/`BillLineInput`: trailing optional `string? PortionName = null, decimal? PortionQuantity = null, string? PortionUnit = null`; `BillItem.DisplayName`.
- `static string ItemDisplayName.Compose(string itemName, string? portionName)` inserts ` (Half)` before a trailing ` (Parcel)`; null portion returns `itemName`.

- [ ] **Step 1: Write failing tests** (`MenuItemPortionTests`): `SetPortions_RejectsDuplicateNamesIgnoringCase`, `..._RejectsTwoDefaults`, `..._RejectsNoDefault`, `..._RejectsNoActivePortion`, `..._RejectsInactiveDefault`, `..._Rejects13Portions`, `..._RejectsNegativePrice`, `..._SetsItemPriceToTheDefaultPortionPrice`, `NewItem_HasOneRegularDefaultPortionAtItsPrice`, `SetDefaultPrice_ChangesBothPrices`; `OrderTests`: `DisplayName_InsertsThePortionBeforeParcel` (`"Chicken Biryani (Parcel)"` + `"Half"` = `"Chicken Biryani (Half) (Parcel)"`), `DisplayName_IsTheOldNameWithoutAPortion`; `BillTests`: `BillItem_CopiesThePortionFromTheLine`.
- [ ] **Step 2:** `dotnet test tests\HotelPOS.Domain.Tests --nologo` -> new tests FAIL (members missing).
- [ ] **Step 3:** Implement the types above in `MenuGuard`/`DomainException` style; units checked against `PortionUnits.All` (add to `MenuLimits` file); names via `MenuGuard.Name`.
- [ ] **Step 4:** Run the Domain test project; all pass (existing helpers still compile).

### Task 2: Persistence and migration
**Files:**
- Modify: `src/HotelPOS.Infrastructure/Persistence/Configurations/MenuConfigurations.cs` (new `MenuItemPortionConfiguration`: unique `{MenuItemId, Name}` on `lower(Name)`, partial unique `{MenuItemId}` WHERE `"IsDefault"`, checks `Price >= 0` and `Quantity > 0`, cascade), `OrderConfigurations.cs` (4 columns, FK `PortionId` ON DELETE SET NULL), `BillingConfigurations.cs` (3 columns), `AppDbContext.cs`, `src/HotelPOS.Application/Common/Interfaces/IAppDbContext.cs` (`DbSet<MenuItemPortion> MenuItemPortions`)
- Create: `Persistence/Migrations/<ts>_Phase32_ItemPortions.cs` (+ Designer, snapshot update via `dotnet ef migrations add Phase32_ItemPortions --project src/HotelPOS.Infrastructure --startup-project src/HotelPOS.Infrastructure --output-dir Persistence/Migrations`)
- Test: `tests/HotelPOS.Application.Tests/Menu/PortionPersistenceTests.cs` (new)

**Interfaces:** Consumes Task 1 entities. Produces `IAppDbContext.MenuItemPortions`.

- [ ] **Step 1: Write failing tests:** `Saving_AnItemWithPortions_RoundTrips`, `TwoDefaultsForOneItem_AreRejectedByTheDatabase`, `DeletingAPortion_NullsOrderItemPortionId_AndKeepsTheName`.
- [ ] **Step 2:** Run; FAIL.
- [ ] **Step 3:** Add configurations and `DbSet`s; generate the migration; edit `Up` to `INSERT INTO "MenuItemPortions" ... SELECT` one `'Regular'` default row per existing `MenuItems` row at its `Price` (sort 1); `Down` drops the table and columns.
- [ ] **Step 4:** Tests pass (the test fixture applies migrations to a scratch DB).
- [ ] **Step 5:** Rehearse on a copy of `hotelpos_dev` (pg_dump -> scratch DB -> migrate -> compare counts/MD5 of orders, order items, bills; then `dotnet ef database update 20261124184559_Phase31_SeatGroupTickets` and back). Record the numbers in `docs/ops/12-portions-release.md` (created in Task 9).

### Task 3: Menu API (write and read)
**Files:**
- Modify: `src/HotelPOS.Contracts/Menu/MenuContracts.cs` (`MenuItemPortionDto`, `SaveMenuItemPortionRequest`, `Portions` on `MenuItemDto`, `MenuEntryDto`, `CreateMenuItemRequest`, `UpdateMenuItemRequest`), `Application/Features/Menu/Commands/CreateMenuItem.cs`, `UpdateMenuItem.cs`, `MenuItems.cs` (`WithReferences` + `.Include(Portions)`), `MenuMapping.cs`, `Queries/GetMenu.cs`, `Queries/GetMenuItem.cs`, `Queries/ListMenuItems.cs`
- Test: `tests/HotelPOS.Application.Tests/Menu/MenuPortionTests.cs` (new), `tests/HotelPOS.Api.Tests/MenuPortionApiTests.cs` (new)

**Interfaces:** `record MenuItemPortionDto { int Id; string Name; decimal? Quantity; string? Unit; decimal Price; bool IsDefault; bool IsActive; int SortOrder }`; `record SaveMenuItemPortionRequest { int? Id; string Name; decimal? Quantity; string? Unit; decimal Price; bool IsDefault; bool IsActive; int SortOrder }`; `IReadOnlyList<SaveMenuItemPortionRequest>? Portions` on both requests; `IReadOnlyList<MenuItemPortionDto> Portions` on `MenuItemDto` (all) and `MenuEntryDto` (**active only**, default first, then `SortOrder`).

- [ ] **Step 1: Write failing tests:** `Create_WithoutPortions_MakesOneRegularPortion`; `Create_WithFourPortions_SavesThemAndMirrorsTheDefaultPrice`; `Update_WithoutPortions_KeepsThem`; `Update_OldClientPriceChange_ChangesTheDefaultPortionPrice`; `Update_WithPortions_KeepsIdsOfExistingRows`; `Update_RemovingAPortionUsedByAnOrder_KeepsTheOrderLine`; validator tests per Review Focus (duplicate, two defaults, none active, 13, negative); `Menu_ListsOnlyActivePortionsDefaultFirst`; `AnyPortionWrite_BumpsTheMenuVersion`; `Audit_RecordsEachPortionOldAndNewPrice`; API: `PortionsRoundTripThroughTheMenuEndpoints`, `WaiterRoleCannotEditPortions` (same permission as item edit).
- [ ] **Step 2:** Run; FAIL.
- [ ] **Step 3:** Implement. Writes go through `MenuChanges.SaveAsync` (bumps the version). Update: `Portions == null` -> untouched, but a changed `Price` calls `SetDefaultPrice`. Image upload unchanged.
- [ ] **Step 4:** Run `HotelPOS.Application.Tests` and `HotelPOS.Api.Tests`; pass.

### Task 4: Order path and snapshot
**Files:**
- Modify: `Contracts/Orders/OrderContracts.cs` (`OrderItemInput.PortionId`; `OrderItemDto` + `PortionId, PortionName, PortionQuantity, PortionUnit, DisplayName`), `Application/Features/Orders/OrderItemFactory.cs`, `OrderItemInputValidator.cs`, `OrderSteps.cs` (mapper), `Commands/AppendOrderItems.cs`, `Commands/CancelOrderItem.cs` (messages use `DisplayName`)
- Test: `tests/HotelPOS.Application.Tests/Orders/PortionOrderTests.cs` (new), `tests/HotelPOS.Api.Tests/PortionOrderApiTests.cs` (new)

**Interfaces:** Consumes Tasks 1-3. Factory rules: resolve portion (given id, else the only active one, else refuse); price = portion price + packing + modifiers via the existing path; store `Portion*` only when `item.Portions.Count > 1`; error text `"<Item> (<Portion>) is no longer available. Choose another portion."` and `"<Item> has several portions. Update the app to choose one."`

- [ ] **Step 1: Write failing tests:** `Order_OfAPortion_FreezesNameQuantityUnitAndPrice`; `PriceEditAfterSend_DoesNotChangeTheSentLineOrTheBill`; `NoPortionId_OnASinglePortionItem_UsesIt`; `NoPortionId_OnATwoActivePortionItem_IsRefused`; `InactivePortion_IsRefused_NamingItemAndPortion`; `PortionOfAnotherItem_IsRefused`; `DraftResave_Reprices_FromTheCurrentPortion`; `DraftResave_WithADisabledPortion_IsRefused_NotSwitched`; `TwoDefinedOneActive_StillStoresThePortion`; `OnePortionItem_StoresNoPortion`; `PackingTaxDiscount_UseThePortionPrice` (parcel 10, GST 5%, 10% discount on a Half line); `Stock_TakesOnePerQuantity_ForAnyPortion`; `ParcelAndPortion_ShowBothInDisplayName`; API: `TwoPortionsOfOneItem_AreTwoLines`.
- [ ] **Step 2:** Run; FAIL.
- [ ] **Step 3:** Implement per the rules above; `OrderStock` untouched.
- [ ] **Step 4:** Run both test projects; pass.

### Task 5: Kitchen, bill, print, reports
**Files:**
- Modify: `Contracts/Kitchen/KitchenContracts.cs`, `Contracts/Billing/BillingContracts.cs`, `Contracts/Print/PrintContracts.cs` (`KotLine`, `InvoiceLine` gain `DisplayName`-composed `Name` + portion fields), `Contracts/Reports/ReportContracts.cs` (`ItemSalesRowDto.Portion`), `Application/Features/Kitchen/KitchenTickets.cs`, `Billing/Commands/RequestBill.cs`, `Billing/BillSteps.cs`, `Print/PrintDocuments.cs` (incl. template-preview sample lines), `Infrastructure/Persistence/ReportQueries.cs` (`ItemSalesAsync` groups by `bi."ItemName", bi."PortionName"`; dashboard top items sum over portions), `Application/Features/Exports/ReportTables.cs`
- Test: `tests/HotelPOS.Application.Tests/Print/PortionPrintTests.cs` (new), `Reports/ReportTests.cs`, `tests/HotelPOS.Desktop.Tests/Printing/EscPosRendererTests.cs`

**Interfaces:** Produces `KitchenTicketItemDto`/`BillItemDto`/`OrderItemDto` `.DisplayName`; `KotLine.Name` and `InvoiceLine.Name` already hold the display name.

- [ ] **Step 1: Write failing tests:** `Kot_ShowsChickenBiryaniHalfTimesTwo`; `Invoice_ShowsHalfRateAndAmount` (`Chicken Biryani (Half) | 2 | 180.00 | 360.00`); `BillItem_KeepsThePortionAfterAPriceEdit`; `ItemSales_SeparatesHalfAndFull`; `ItemSales_BlankPortionForOldLines`; `DashboardTopItems_AddsAllPortionsOfAnItem`; `EscPos_WrapsALongPortionName`; `ExportColumns_IncludePortion`.
- [ ] **Step 2:** Run; FAIL.
- [ ] **Step 3:** Compose `DisplayName` once per mapper with `ItemDisplayName.Compose`; renderers keep printing `Name`.
- [ ] **Step 4:** Run Application, Api, Desktop test projects; update the ~15 tests that assert `ItemName`/"(Parcel)" only where their text changes.

### Task 6: Windows admin (items, list, preview, photo)
**Files:**
- Modify: `src/HotelPOS.Desktop/Modules/MenuAdmin/MenuItemsPageViewModel.cs` (`MenuItemEditorViewModel`: `ObservableCollection<PortionRowViewModel> Portions`, `AddPortionCommand`, `RemovePortionCommand`, `SetDefaultCommand`, per-row validation, `ToCreateRequest`/`ToUpdateRequest` include portions, photo chosen while `IsNew` uploads after save), `MenuItemsPageView.xaml` (portions table replaces the Price field; list shows chips), `MenuPreviewViewModel.cs` + `MenuPreviewView.xaml`
- Create: `src/HotelPOS.Desktop/Modules/MenuAdmin/PortionRowViewModel.cs`
- Test: `tests/HotelPOS.Desktop.Tests/MenuAdmin/MenuItemPortionEditorTests.cs` (new), `Rendering/ScreenRenderingTests.cs` (menu items screen in the 87-screen run)

**Interfaces:** `PortionRowViewModel { int? Id; string Name; string QuantityText; string? Unit; string PriceText; bool IsDefault; bool IsActive; string? Error }`.

- [ ] **Step 1: Write failing tests:** `NewItem_StartsWithOneRegularRow`; `AddRemoveRow_AndDefaultRadioKeepsExactlyOneDefault`; `Save_IsBlocked_ForDuplicateName_NoActiveRow_BadPrice` (row-level `Error`); `ToCreateRequest_CarriesThePortions`; `PhotoChosenBeforeSave_IsUploadedAfterTheItemIsCreated`; `ListRow_ShowsPortionChipsWithPricesAndDefaultMarked`.
- [ ] **Step 2:** Run; FAIL.
- [ ] **Step 3:** Implement view-models and XAML following the existing `Admin.*` styles and the owner-approved mock-up (default radio, name, quantity+unit, price, on/off, remove, "+ Add portion"; disabled chips struck through; clicking a chip opens the editor on that row).
- [ ] **Step 4:** Run `HotelPOS.Desktop.Tests`; pass (rendering test also checks bindings).

### Task 7: Web / Android ordering
**Files:**
- Modify: `src/HotelPOS.Client.Shared/Services/OrderComposer.cs` (`CartLine.PortionId`, `PortionName`, `PortionQuantity`, `PortionUnit`; `MergeKey` includes `PortionId`; `Add(... , MenuItemPortionDto? portion)`; `ToInputs` sends `PortionId`; `LoadOrder`/`LoadDraft` rebuild the portion by id then name), `DraftStore.cs` (`WebDraftLine` optional portion fields), `Shared/MenuItemCard.razor` (chips, Add button "Add Half · 180", quantity caption), `Shared/ItemOptionsSheet.razor` (Portion group first), `Shared/CartLineRow.razor`/`CartBar.razor` (`DisplayName`), `Pages/Waiter/OrderBuilderPage.razor` (`Pick`, `EditLine`, `ConfirmOptions`), `Pages/Waiter/TableDetailsPage.razor`, `Pages/Kitchen/TicketCard.razor`/`TicketDetailPane.razor`/`CompletedPage.razor` (`DisplayName`), `wwwroot/css/waiter.css`
- Test: `tests/HotelPOS.Client.Tests/OrderComposerTests.cs`, `WaiterScreensTests.cs`

- [ ] **Step 1: Write failing tests:** `Composer_MergesSameItemSamePortion_AndKeepsDifferentPortionsApart`; `Composer_SendsPortionId`; `Composer_LoadDraft_MapsPortionByIdThenName_AndFlagsAnUnmappedLine`; `Card_WithTwoActivePortions_ShowsChipsDefaultSelected_AndAddShowsThePrice`; `Card_TapChip_HighlightsItAndUpdatesTheAddPrice`; `Card_WithOneActivePortion_AddsOnOneTap`; `Card_OutOfStock_DisablesAllChips`; `Sheet_ShowsPortionFirst_WhenEditingALine`; `Cart_ShowsChickenBiryaniHalf`.
- [ ] **Step 2:** Run `dotnet test tests\HotelPOS.Client.Tests`; FAIL.
- [ ] **Step 3:** Implement; chips are buttons with `aria-pressed`, min 44 px touch target, 16 px gutter, wrap on a 360 px phone.
- [ ] **Step 4:** Tests pass.

### Task 8: Windows order builder
**Files:**
- Modify: `src/HotelPOS.Desktop/Modules/Orders/OrderBuilderViewModel.cs` (`AddItem` opens the picker for items with 2+ active portions; `MenuTileViewModel` price text shows the default price), `ModifierPickerViewModel.cs` (portion group first; `TryBuild` prices from the chosen portion), `OrderBuilderView.xaml` (picker overlay), `CartItemViewModel.cs` (`PortionId`, names, `IsSameChoice` includes it, `DisplayName`), `src/HotelPOS.Desktop/Services/Orders/LocalDraftStore.cs` (`DraftLine` optional fields), `Modules/Orders/ActiveOrdersViewModel.cs`, `Modules/Tables/TableDetailsViewModel.cs`, `Modules/Billing/BillDetailViewModel.cs`, `ClosedBillsViewModel.cs`, `Modules/Dashboard/DashboardViewModel.cs`, `Modules/Kitchen/KitchenDisplayViewModel.cs`, `CompletedOrdersViewModel.cs` (use `DisplayName`)
- Test: `tests/HotelPOS.Desktop.Tests/Orders/OrderBuilderTests.cs`, `Rendering/ScreenRenderingTests.cs`

- [ ] **Step 1: Write failing tests:** `TileOfAMultiPortionItem_OpensThePickerWithTheDefaultSelected`; `PickingHalf_PricesTheLineAndCartAtHalf`; `TwoPortions_AreTwoCartLines_SamePortionMerges`; `OneActivePortionItem_AddsStraightAway`; `Send_PostsPortionId`; `LoadedDraft_RestoresThePortion`; `KitchenBoard_ShowsDisplayName`.
- [ ] **Step 2:** Run; FAIL.
- [ ] **Step 3:** Implement; reuse the existing picker overlay styles.
- [ ] **Step 4:** Run `HotelPOS.Desktop.Tests`; pass.

### Task 9: Release, docs, verification
**Files:**
- Modify: `build/version.props` (0.1.2), `docs/ops/09-android-app.md`/`04-upgrade.md` pointers; Create: `docs/ops/12-portions-release.md` (what changes, migration + rollback, rehearsal numbers, manual test list); update `docs/07-menus-screens-and-fields.md`.

- [ ] **Step 1:** Run every suite once: Domain, Application, Api, Client, Desktop (and Infrastructure/other test projects present); all green.
- [ ] **Step 2:** Real-app check on a scratch database with the demo seed: create Chicken Biryani with 4 portions and a photo; order Quarter and Half; confirm KOT, kitchen display, bill, printed/PDF invoice; change a price and re-open the old bill; one-portion item; parcel line; discount.
- [ ] **Step 3:** Build the Android APK (`build\publish-android.ps1`, keystore password from the owner) and the installers; note anything not verified (thermal printer, physical phone, clean-VM test).
- [ ] **Step 4:** Report modified files, the migration, API changes, results and open issues.
