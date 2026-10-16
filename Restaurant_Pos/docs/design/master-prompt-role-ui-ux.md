# HotelPOS — Master prompt for all role-based UI/UX design (owner, 2026-10-17)

Verbatim requirements supplied by the owner together with the six mock-ups in `reference/`. This is the specification
of record for Phase 21; the plan in `docs/phases/phase-21-role-ui-ux-redesign.md` maps every section below to the
existing code and lists the gaps and decisions.

## 1. Project overview

Act as a Senior Product Designer, Restaurant POS UX Specialist, Enterprise SaaS UI Architect, and Senior Frontend
Engineer. Design and implement a complete, production-quality UI/UX experience for HotelPOS. Four roles: Admin/Manager
(operations, configuration, reporting, monitoring), Cashier (billing, payment, receipts, settlement), Waiter/Server
(table service, orders, kitchen submission, status tracking), Kitchen Staff (order display, preparation, readiness).
One connected system, consistent design language, interfaces optimised per role and device.

Primary objective: elegant, modern, highly usable POS experience comparable to professional enterprise software —
professional, clean, premium; fast during busy hours; touch-friendly; responsive; accessible; consistent; built around
operational efficiency, not decoration. Use the existing project, components, API contracts and screenshots as the
source of truth.

## 2. Non-negotiable implementation rules

Inspect the existing project first: frontend architecture and routing, authentication and role-based authorization,
API endpoints and models, order lifecycle and status transitions, table/billing/payment/kitchen workflows, real-time
communication and notifications, UI components and conventions, loading/error/offline/reconnection handling.

Preserve existing functionality — do NOT change to achieve the design: backend business logic; API endpoints, methods,
request formats; DTOs and response models; database schema; authentication/authorization/JWT; order creation and status
transitions; billing and payment calculations; tax, discounts, totals, change; kitchen submission and readiness;
routing and navigation behaviour; SignalR; validation and error handling. No hardcoded mock data. No fake payment
success or fake status updates. Do not remove working features unless listed in the exclusions. If a screen needs
backend functionality that does not exist, document the gap. Primary rule: redesign the presentation layer while
preserving behaviour.

## 3. Required exclusions

No dedicated modules, pages, navigation or management workflows for: Modifiers & Add-ons; Recipes; full Inventory
Management; Customers & Loyalty Programs; Branch Management. No ingredient management, suppliers, purchase orders, stock
adjustments, recipe costing, loyalty points or campaigns. A small low-stock widget may stay on the Admin Dashboard only
if the application already supports that data. Keep genuinely required configuration (restaurant profile, receipt
settings, payment configuration, staff permissions). Do not add features just to look comprehensive.

## 4. Global design system

One reusable design system shared across roles. Visual direction: modern enterprise POS inspired by Fluent UI, Linear,
Stripe Dashboard and professional POS products. Preferred Admin Dashboard: light background, dark navy sidebar, green
accents, KPI cards, charts, order tables, table status cards, operational widgets. Avoid decorative dashboards,
gradients, heavy shadows, crowded layouts, unnecessary animation.

Tokens: primary green `#078747`; dark navy `#142235`; page background `#F4F7FB`; card `#FFFFFF`; primary text
`#172B4D`; secondary text `#64748B`; borders `#E2E8F0`; success `#16A34A`; warning `#F59E0B`; error `#DC2626`;
information `#2563EB`. Semantics: green = completed/ready/successful/available; orange = preparing/pending/bill
requested/attention; red = delayed/failed/cancelled/critical; blue = informational/active selection; grey =
inactive/disabled/neutral. Never colour alone — label and icon as well.

Typography: Inter or the project's existing professional font. Page title 24–30 px; section heading 16–20; body
14–16; supporting labels 12–14; important amounts 22–32. Readable on cashier monitors, kitchen displays, phones.

Components: buttons and icon buttons; inputs, search, filters; dropdowns and date pickers; status badges; cards and KPI
widgets; data tables; dialogs and confirmations; toasts; tabs and segmented controls; pagination; empty/loading/error
states; connection indicators; order summary panels; payment summaries; table status indicators. Consistent spacing,
radius, typography, iconography, interaction patterns.

## 5. Role 1 — Admin / Manager dashboard

Target: desktop or laptop browser; design 1920×1080; minimum 1366×768; tablet responsive for secondary use. Must
resemble the admin mock-up: dark navy sidebar, white cards, light grey background, green accents.

Sidebar groups: Overview (Dashboard); Live Operations (Tables & Floor Plan, Orders, Kitchen Display, Cashier & Bills);
Menu Management (Categories, Menu Items); Finance (Payments, Expenses, Cash Reconciliation); Analytics & Reports (Sales
Report, Profit & Loss, Popular Items, Tax Reports, Staff Performance); People (Staff & Roles, Permissions, Shifts);
Devices (Terminals, Printers, Kitchen Stations); Settings (Restaurant Profile, Tax & Currency, Payment Methods, Receipt
Templates, Notifications). Only show pages and actions the application supports; do not fabricate.

Dashboard: top navigation (brand, title + welcome, date range, search, notification bell with count, online/offline,
profile menu; no branch management); KPI cards (Today's Sales, Total Orders, Average Order Value, Occupied Tables, Low
Stock only if supported) each with icon, label, value, comparison/supporting info only when real, subtle trend when
history exists — no invented statistics; analytics row (Live Order Status chart, Hourly Sales chart, Top Selling Dishes)
with date filters where supported; Active Orders table (number, table, waiter, items, total, status, elapsed, actions;
search, filters, sort, pagination; badges per existing lifecycle: Sent to Kitchen, Preparing, Ready, Served, Bill
Requested, Paid, Cancelled); Table Status (cards or floor summary: Available, Occupied, Preparing, Bill Requested,
Payment Pending — only supported statuses; link to full screen); operational widgets (Low Stock if supported, Recent
Activity from real events, Staff & Device Status from real connection data).

Interactions: KPI cards open reports; order rows open details; table cards open table details; chart tooltips; filters
affect data; actions use existing permissions and APIs; destructive actions confirm; responsive without unnecessary
horizontal scroll.

## 6. Admin / Manager supporting screens

Tables & Floor Plan (layout, number, capacity, status, active order summary, assigned waiter if supported, elapsed,
search/filters; clear distinction of occupied/available/attention). Orders (searchable list, status filters, table and
waiter, item count and total, created time, elapsed, detail panel, item details and status history if available; no
unsupported transitions). Kitchen Display overview (number, destination, items/quantities, instructions, time since
submission, status, delayed indicators; grouping by station/status only where supported). Cashier & Bills (unpaid and
bill-requested orders, number/table, waiter, amount due, age, payment status, open cashier/bill; do not duplicate or
bypass the cashier flow). Menu Management: Categories (list, name, attributes, create/edit/delete where supported,
active/inactive) and Menu Items (search, image, name, category, price, availability, attributes, CRUD) — no Modifiers &
Add-ons or Recipes. Finance (Payments, Expenses, Cash Reconciliation — existing supported screens; totals from existing
logic). Analytics & Reports (Sales, Profit & Loss, Popular Items, Tax, Staff Performance — existing supported reports;
export only where supported). People and Devices (Staff & Roles, Permissions, Shifts, Terminals, Printers, Kitchen
Stations). Settings (Restaurant Profile, Tax & Currency, Payment Methods, Receipt Templates, Notifications) with clear
forms, validation, save/cancel, unsaved-change warnings.

## 7. Role 2 — Cashier

Target: Windows desktop application on a cashier monitor, 1920×1080, sensible support for smaller windows and touch
monitors. Priorities: billing speed, payment accuracy, low interaction effort. Purpose-built workspace, not the admin
dashboard. Three panels where size allows: left Bills and Orders (search by order/table; filters unpaid, bill requested,
payment pending, paid; number, table, waiter, amount due, elapsed, status); centre Order Details (number/table, items,
quantities, unit prices, line totals, subtotal, discounts if supported, taxes, service charges if supported, grand
total, payment status); right Payment (amount due prominent, supported methods, amount received, change due, reference
fields when required, Complete Payment, Print Receipt, Split Bill only if supported). Methods: only those the system
supports (Cash, Card, UPI, other configured). Rules: amount due most prominent; large labelled controls; obvious
selected method; validate before submit; success only after the actual result; prevent duplicate submissions;
actionable failures; accurate change; print via existing mechanism; preserve calculations; never mark paid by click;
disable actions while submitting; clear way back to the list; redesign split/partial payment UI if it exists without
changing rules.

## 8. Role 3 — Waiter / Server

Target: phone first, tablet second; narrow screens, touch, fast service on the move; not a scaled-down dashboard.
Bottom navigation (or equivalent): Tables, My Orders, New Order, Notifications, Profile — only supported routes.
Tables: cards with number, status, guest count, assigned waiter if relevant, active order number, elapsed, bill-requested
/ ready indicators; easy access to available tables and the waiter's active tables. My Orders: number, table, item
count, total, status, time since creation, kitchen progress when available; differentiated status labels; quick
details. New Order: selected table and guest count, category navigation, search, favourites if supported, item cards
with images, name and price, quantity controls, cart summary, subtotal/total, Save Draft if supported, Send to Kitchen;
easy item selection and cart access on small screens; do not add a Modifiers & Add-ons interface; no recipe/ingredient
workflow. Order details: number, table, items, quantities, kitchen status, total, status history when available; the
waiter must understand pending / sent / preparing / ready / served / awaiting bill per the existing workflow. Rules:
large targets, minimal typing, confirmation before sending where appropriate, feedback only after API confirmation,
safe on slow networks, no duplicate orders from repeated taps, preserve auth and permissions, never report submitted
until confirmed.

## 9. Role 4 — Kitchen Staff

Target: kitchen tablet or touchscreen monitor, landscape and larger displays; readable from a short distance; large
text, strong contrast, clear status colours, minimal decoration; no dense navigation, tiny controls, charts or
admin-style layouts. Compact header (brand, station name if available, current time, connection status, alert
indicator, user/sign-out); no large admin sidebar; compact station navigation only if multiple stations are supported.
Columns per existing statuses, typically New/Pending, Preparing, Ready — no new backend statuses; preserve existing
names. Ticket: order number, table/destination, submission time, elapsed, item names, quantities large, existing special
instructions, status, actions; prominent overdue indicator per existing rules/thresholds. Interactions: accept/start,
mark ready, view details — only allowed actions; update from confirmed results or real-time events; notify roles via the
current system; avoid duplicate actions; handle stale tickets and reconnection; clear failure feedback. Display:
ordered per existing workflow/priority; new orders noticeable without disruptive animation; long-running stand out;
ready easy to identify; readable in a bright kitchen; avoid unnecessary scrolling; empty/loading/disconnected/
reconnecting states.

## 10. Role-based access and navigation

Visibility per the existing role and permission model. Admin/Manager: management, reporting, monitoring per
permissions. Cashier: billing, payment, receipt flows. Waiter: table, ordering, service. Kitchen: kitchen orders and
preparation. Do not rely on hiding buttons as the only authorization; do not change backend authorization; no new role
names or permission claims; no admin pages for unauthorized roles; preserve login/logout/session/refresh; redirect per
existing routing. Preserve any additional existing roles.

## 11. Responsive design and device strategy

| Role | Primary device | Priority |
|---|---|---|
| Admin / Manager | Desktop or laptop | Information density, analytics, management |
| Cashier | Windows desktop / touch monitor | Billing speed, payment accuracy |
| Waiter / Server | Mobile phone | Quick table service and order entry |
| Kitchen Staff | Tablet or kitchen monitor | Large readable tickets, fast status changes |

Admin tables horizontal scroll only when unavoidable; cashier payment controls always visible; waiter mobile-first with
bottom nav; kitchen tickets reflow by width; dialogs fit the viewport; no fixed widths that break small devices;
keyboard and touch; consistent language without forcing one layout.

## 12. Real-time updates, network states, notifications

Use the existing real-time infrastructure (SignalR). States: online, offline, reconnecting, loading, request in
progress, success, failed, session expired. On connection loss: visible but unobtrusive banner; never claim success;
prevent unsafe duplicates; use existing retry/recovery; show staleness. No new offline-sync/queueing architecture
without reviewing the existing one. Consistent toasts for confirmed actions and actionable failures.

## 13. Accessibility and usability

Strong contrast; focus states; keyboard navigation on desktop; large touch targets; labels for icon-only buttons; form
labels and validation; status text in addition to colour; consistent primary-action placement; confirmation for
destructive operations; no hidden critical information; no unnecessary animation; readable currency and date formats;
consistent error handling. Use the configured locale and currency.

## 14. Implementation requirements

Existing stack and conventions. Do not replace framework, routing, API client, state management, auth or real-time.
Reusable layouts and components without unnecessary abstraction: shared tokens; shared buttons/inputs/badges/dialogs/
notifications; role-specific layouts; admin dashboard and management screens; cashier workspace; waiter mobile
workspace; kitchen ticket workspace; shared loading/empty/error/connection states. Existing icon and chart libraries;
no new dependencies without clear need; presentation separate from business logic where the architecture permits;
everything connected to real behaviour; no static prototype.

## 15. Required workflow

Phase 1 Audit → Phase 2 Design System → Phase 3 Admin/Manager → Phase 4 Cashier → Phase 5 Waiter → Phase 6 Kitchen →
Phase 7 Integration and regression testing (routes, permissions, API integrations, states, real-time, payments, status
changes; confirm no business-logic change) → Phase 8 Final review (responsiveness, consistency, accessibility, controls,
role navigation, status rendering, data binding, payment totals, no duplicate submissions, no broken routes, no console
errors).

## 16. Required final deliverables

1. Summary of the existing architecture and role workflows discovered. 2. Pages redesigned per role. 3. Shared design
system and components. 4. Files created or modified. 5. Missing backend capabilities discovered. 6. API and workflow
compatibility findings. 7. Responsive verification results. 8. Functional and regression test results. 9. Remaining
issues or limitations. Do not claim tested unless tested; do not claim complete while placeholders remain.

## Final instruction

One cohesive product with four optimised experiences. Use the clean Admin Dashboard mock-up as the visual foundation.
Preserve APIs, business rules, data models, authentication, permissions, payments, order lifecycle and real-time
workflows. Do not add the excluded modules. Do not invent functionality. Inspect first, redesign systematically,
implement against real behaviour, test before declaring completion.
