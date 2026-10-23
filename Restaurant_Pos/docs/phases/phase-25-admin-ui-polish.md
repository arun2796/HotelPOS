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

Dark mode re-checked on every changed screen (renders with the `-dark` suffix). Phase closed 2026-10-23.

## Left out, by decision

| Proposed | Why it stays out |
|---|---|
| Row actions on hover instead of the button row under a grid | The select-then-act row is the same on every list screen, the demo and screenshot harnesses click those buttons by name, and hover actions do not work on the touch billing counters. A consistent row beats a hidden one. |
| Shadowed (elevated) cards on the dashboard | A `DropShadowEffect` per panel costs frames while the dashboard scrolls and refreshes every few seconds; the elevated card is used only for panes that sit still beside a list. |
| Floating labels on forms | The label-above-field pattern with a caption is already consistent and reads better at the Compact density on billing PCs; floating labels would change every editor for no gain in clarity. |
| Animated 150 ms transitions on buttons and nav items | Buttons already have hover, pressed (scale) and focus states, and pages cross-fade; further animation adds nothing a cashier needs. |

What the page header already provides: the shell's top bar carries each module's title and one-line description from
`ModuleRegistry`, so an in-page header would repeat it; the primary action sits in the content's header row instead.
