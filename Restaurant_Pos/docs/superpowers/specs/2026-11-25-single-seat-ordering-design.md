# One chair at a time when taking an order (short design)

Date: 2026-11-25 · Size: small change · Status: waiting for owner approval

## Intent and why
A waiter taking an order for a table such as T01 (4 seats) wants to pick **one chair** (T01A, T01B, ...) and take that
chair's order. Today, tapping several chairs joins them into a group (`T01A+B`), and tapping a taken chair pulls in its
whole group. The owner does not want groups: each chair is ordered for on its own.

## Goals
- Tapping a chair selects only that chair. Tapping another chair moves the selection to it. Tapping the selected chair
  clears it.
- **Whole table** stays: with no chair picked, dishes go to the table without a seat, as today.
- Same behaviour on web, Android (same web client) and the Windows desktop order screen.
- A chair that already has dishes stays marked ● and tapping it adds more dishes to that chair.

## Non-goals
- No server change. The API, kitchen tickets and billing keep accepting seat text such as `T01A+B`; a single chair is
  just a group of one, so each chair gets its own kitchen ticket and its own bill, as today.
- No change to orders that already hold groups: they still open, show and bill. If such an order has a chair that
  belongs to a group, tapping it selects that existing group, so the waiter can still add to it and the server never sees
  an overlapping group.
- Not making a chair mandatory for dine-in.

## Anti-goal
A change that makes old grouped orders unreadable, or that lets a chair end up in two groups.

## Approaches considered
1. **Screens only (chosen).** Change the tap rule in the web order builder (`ToggleSeat` in `OrderBuilderPage.razor`) and
   the desktop one (`OnSeatPickChanged` in `OrderBuilderViewModel.cs`). Small, reversible, no data risk.
2. **Also make the server refuse groups.** Breaks existing orders, merged-table logic and tests; nobody asked for it.
   Rejected.

## Decisions
- Select rule: new free chair replaces the current selection; selected chair tapped again clears it (Whole table).
- Hint text changes from "Tap one seat, or several that share the order" to "Tap a seat to order for it. Tap again for
  the whole table." Desktop tooltips are changed the same way.
- The desktop still offers as many chairs as the guests entered (existing rule, unchanged).

## Left to the builder
- Update the existing web test `Builder_SeatChips_SelectSeveralSeatsTogether_...` to the new rule and add one for a
  legacy grouped chair; add the desktop equivalent in the rendering/view-model tests.
- Update the manual test steps in `docs/ops/11-seat-groups-release.md` § 4 (steps 2-3 describe multi-select).
