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

## Pass 2 (proposed, not started)

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
