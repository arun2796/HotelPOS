# Phase 25 — Admin UI polish (enterprise finish)

The owner asked for the admin and manager screens to read as a premium, enterprise product. The bones from Phases
11, 17 and 21 stay (Inter, tokens, light/dark, command palette, navy shell); this phase is finish, applied through the
design system wherever possible so every screen moves together. Presentation only; behaviour and tests unchanged
except where noted.

## Pass 1 (2026-10-23)

| Change | Where | Effect |
|---|---|---|
| Sidebar regrouped by order of use: Overview, Service, Cash & Day End, Reports & Tax, Set-up, People, Devices, System | `ModuleRegistry` | Day-to-day screens first, set-up and administration last; eight groups instead of nine |
| Sidebar icons restored on every module (the regroup had dropped the private-use glyphs) and guarded by a test | `ModuleRegistry`, `NavigationGroupTests.EverySidebarModule_HasAnIconGlyph` | Floor plan gets an icon for the first time |
| Group headers in small capitals, lighter and with more air above | `NavItemViewModel.GroupLabel`, `DesignSystem.xaml` NavItem | The rail reads like the mock-up's section labels |
| Status as badges instead of words in the Users and Devices tables (Active / Inactive, Locked) | `UsersView.xaml`, `DevicesView.xaml` | Colour plus icon plus word, like every other status in the app |
| Sign-out asks for confirmation (staff clients) | `MainLayout.razor` | One wrong tap no longer takes a device off the air |
| Print ₹ as: caption explaining the empty default | `PrinterSettingsView.xaml` | — |

Reference renders (fake data, 1366 × 800, light): `docs/ui-ux/desktop/admin/60-admin-users-phase25.png`,
`61-admin-devices-phase25.png`. `ScreenRenderingTests` (167 renders, both themes, zero binding errors) is the gate.

## Pass 2a (2026-10-23)

| Change | Where |
|---|---|
| `Card.Elevated` (soft shadow) for panes that sit beside a list; used by the Users editor | `DesignSystem.xaml`, `UsersView.xaml` |
| Users action row: one emphasised Edit, the rest quiet ghost buttons, "Log out everywhere" in the danger colour | `UsersView.xaml` |
| Empty states on the Users and Devices grids (why it is empty, what to do) | `UsersView.xaml`, `DevicesView.xaml` |
| Printers: the primary action moves into the header row | `PrinterSettingsView.xaml` |
| Backup: OK / FAILED / Running as badges | `BackupView.xaml` |
| Audit Log: user, terminal and entity columns no longer truncate at 1366 px (room plus trim-with-tooltip) | `AuditLogsView.xaml` |

Renders: `60-admin-users-phase25.png`, `61-admin-devices-phase25.png`, `62-admin-printers-phase25.png`.

## Pass 2b (2026-10-23, in progress)

| Change | Where |
|---|---|
| Expenses: the filter bar wraps onto a second line at 1366 px instead of cutting the search off; summary and reload pinned right; Active / Voided as badges | `ExpensesView.xaml` (render `63-manager-expenses-phase25.png`) |
| Cash shifts: Open / Closed / Handed over / Force closed as badges; a non-zero variance reads in the danger colour | `ShiftsView.xaml`, `ShiftsViewModel.HasVariance` |
| Tax Returns: GSTR-1 / 3B / HSN grid headers in capitals like every other grid, which also fixes IGST, CGST, SGST, GSTIN, HSN/SAC (Phase 20 carry-over 18) | `GstHubView.xaml.cs` |

## Pass 2b (remaining)

1. Page header pattern on every admin screen: title, one-line description, breadcrumb for hub tabs, primary action top
   right (Users, Devices, Printers, Settings hub, Menu hub, Roles, Backup, Server, Licence, Import, Audit, Expenses).
2. Form system: one field style with floating labels, help captions, inline validation states, consistent widths,
   section dividers inside cards (Printers, Settings, Template Studio, user and item editors).
3. Table system: row actions on hover instead of button rows under the grid (Users, Devices, Audit, Print Log),
   sticky header, right-aligned tabular numbers everywhere, empty and loading states on every grid.
4. Cards in two weights (surface for forms, elevated for live data) and KPI tiles with delta arrows and a trend line.
5. Focus rings, pressed states and 150 ms transitions on buttons, chips and nav items; 8 px rhythm audit.
6. Dark mode re-check of every admin screen after the above; galleries regenerated into `docs/ui-ux/desktop`.

Effort for pass 2: two to three days, verified by the rendering suite and new galleries.
