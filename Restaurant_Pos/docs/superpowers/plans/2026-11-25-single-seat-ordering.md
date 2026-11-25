# One chair at a time when taking an order: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tapping a seat chip selects only that chair (no `A+B` groups made from the UI); Whole table stays.

**Architecture:** Change the tap rule in the web order builder and the Windows order builder view model. Server, kitchen and billing are untouched; a single chair is a group of one. A chair that already belongs to a legacy multi-seat group still selects that whole group, so no chair ends up in two groups.

**Tech Stack:** Blazor (`HotelPOS.Client.Shared`), WPF/MVVM (`HotelPOS.Desktop`), xUnit + bUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-11-25-single-seat-ordering-design.md`

## Global Constraints
- Whole table option stays (no chair selected = `SeatCode` null).
- No API, kitchen or billing change.
- Orders that already hold groups still open and bill.

## Review Focus
- Tapping the selected chair again clears it (back to Whole table).
- Tapping chair B while A is selected moves the selection to B only.
- A chair of an existing `C+D` group selects both C and D, never mixes with another chair.

### Task 1: Web order builder
**Files:** Modify `src/HotelPOS.Client.Shared/Pages/Waiter/OrderBuilderPage.razor` (`ToggleSeat`, hint, titles); Test `tests/HotelPOS.Client.Tests/WaiterScreensTests.cs`

- [ ] Rewrite the test `Builder_SeatChips_...` as `Builder_SeatChips_SelectOneSeatAtATime_AndATakenSeatSelectsItsGroup`: tap A then B -> pressed `false,true,false,false`; tap B again -> all false; add a dish for A -> A `data-taken` true; tap C then A -> pressed `true,false,false,false`.
- [ ] Run it, expect FAIL.
- [ ] Change `ToggleSeat(string seat)`: if the seat is selected, clear the selection; otherwise clear it and select the seat's existing group seats (or just the seat when free).
- [ ] Change the hint to "Tap a seat to order for it. Tap it again for the whole table. ● Has dishes: tap to add more." and the aria-label/title of a taken single seat to "already has dishes".
- [ ] Run the test, expect PASS.

### Task 2: Windows order builder
**Files:** Modify `src/HotelPOS.Desktop/Modules/Orders/OrderBuilderViewModel.cs` (`OnSeatPickChanged`), `SeatPickViewModel.cs` (tooltip), `OrderBuilderView.xaml` (comment only); Test `tests/HotelPOS.Desktop.Tests/Orders/OrderBuilderTests.cs`

- [ ] Add a test: with a 4-seat table, turning `SeatPicks[0].IsOn` then `SeatPicks[1].IsOn` leaves only B on and `SelectedSeat == "T01B"`; turning B off gives `SelectedSeat == OrderBuilderViewModel.WholeTable`.
- [ ] Run it, expect FAIL.
- [ ] In `OnSeatPickChanged`, when a chip turns on, turn every other chip on only if it shares the changed chip's `OwnerGroup` (free chip: all others off).
- [ ] Tooltip: owner equal to the seat code reads "already has dishes. Tap to add more."
- [ ] Run the test, expect PASS.

### Task 3: Docs and full check
- [ ] Update `docs/ops/11-seat-groups-release.md` § 1 row and § 4 steps 2-3 to the one-chair rule.
- [ ] Run `HotelPOS.Client.Tests` and `HotelPOS.Desktop.Tests` once each.
