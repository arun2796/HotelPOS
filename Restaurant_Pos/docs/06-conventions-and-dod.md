# 06 — Conventions and Definition of Done

Last updated: 2026-10-07

## 1. Coding conventions

- C# 12, .NET 8, nullable reference types enabled, implicit usings, `TreatWarningsAsErrors` in `src/`.
- `async`/`await` end-to-end; async methods end with `Async` and accept `CancellationToken`.
- Records for DTOs and event payloads; classes for entities; `sealed` by default.
- Guard clauses and early returns; no nested ternaries; no magic strings (use constants in Contracts).
- `Result<T>` for expected failures in Application; exceptions only for programming errors or
  infrastructure faults.
- One class per file; file name = type name. Exception: the request/response records of one feature may
  share a file in Contracts (`UserContracts.cs`).
- No `DateTime.Now` in Domain/Application (inject `IClock`); UTC everywhere except display.
- No `decimal` arithmetic in view-models beyond display; the server computes money.
- Comments explain *why*, not *what*.

## 2. Naming

| Thing | Pattern | Example |
|---|---|---|
| Entity | singular noun | `Order`, `KitchenOrder` |
| DbSet / table | plural | `Orders`, `KitchenOrders` |
| Application service | `<Feature>Service` / `I<Feature>Service` | `IOrderService` |
| Request DTO | `<Verb><Entity>Request` | `CreateOrderRequest`, `ApplyDiscountRequest` |
| Response DTO | `<Entity>Dto`, `<Entity>SummaryDto`, `<Entity>DetailDto` | `OrderDetailDto` |
| Validator | `<Request>Validator` | `CreateOrderRequestValidator` |
| Controller | `<Resource>Controller`, route `api/<resource-kebab>` | `MenuItemsController` -> `/api/menu-items` |
| Hub event | PascalCase past tense | `OrderReady`, `BillRequested` |
| View / VM | `<Screen>View`, `<Screen>ViewModel` | `OrderBuilderView`, `OrderBuilderViewModel` |
| Migration | `Phase0N_<Topic>` | `Phase04_Orders` |
| Audit action | `<Entity>.<Verb>` | `Order.Cancelled`, `MenuItem.PriceChanged` |
| Setting key | PascalCase | `BusinessDayStartTime` |
| Error code | UPPER_SNAKE | `CONCURRENCY_CONFLICT` |

## 3. Testing conventions

| Project | What | Tools |
|---|---|---|
| `Domain.Tests` | State machines, `BillCalculator`, invariants; pure, no DB | xUnit, FluentAssertions |
| `Application.Tests` | Services with a real `AppDbContext` on SQL Server LocalDB (per-test transaction rollback) and fakes for `IRealtimeNotifier`, `IClock`, `ICurrentUser` | xUnit, NSubstitute |
| `Api.Tests` | `WebApplicationFactory` end-to-end: auth, authorization matrix, idempotency, concurrency, hub events (real `HubConnection` to TestServer) | xUnit, Testcontainers (optional in CI) |
| `Desktop.Tests` | View-model behaviour with fake services; connection-loss scenarios | xUnit |

Database tests create `HotelPOS_<Suite>Tests_<guid>` on LocalDB (override with `HOTELPOS_TEST_SQLSERVER`)
and drop it afterwards. `ScreenRenderingTests` renders every screen and fails on XAML or binding errors;
the end-to-end resilience test runs only with `HOTELPOS_E2E=1`.

Rules: test names `Method_Scenario_Expected`; one assertion topic per test; integration tests seed
their own data; no test depends on another; the suite runs with `dotnet test` from a clean clone.

## 4. Git workflow

- `main` is always buildable. Branch per phase: `phase/04-waiter-ordering`; small PRs inside a phase.
- Conventional commits: `feat(orders): submit creates kitchen tickets`, `fix(billing): ...`,
  `docs(phase-06): ...`, `test(api): ...`.
- Never commit secrets, `appsettings.*.json` with real connection strings, or `settings.json`.

## 5. Definition of Done (applies to every phase)

- [ ] All items in the phase's **Deliverables** and **Acceptance criteria** are implemented.
- [ ] Solution builds with zero warnings (`dotnet build -warnaserror` for `src/`).
- [ ] `dotnet test` passes; new tests listed in the phase document exist.
- [ ] EF migration added, applied on a clean database, and seed data updated.
- [ ] Every new endpoint: role authorization attribute, validator, documented in
      `docs/03-api-reference.md` (update the table if it changed).
- [ ] Every new real-time event: listed in `docs/04-realtime-events.md` and resync handled.
- [ ] Audit entries written for the phase's critical operations.
- [ ] No hard-coded URLs, IPs, printer names, restaurant names or tax rates.
- [ ] Manual demo script in the phase document executed on two machines over Wi-Fi (or two instances
      on one machine) including a "pull the network" check for the phase's critical action.
- [ ] Phase status updated in `README.md` and `docs/phases/README.md`; decisions that changed are
      reflected in the reference docs.

## 6. Keeping docs current

The reference docs (`00`–`06`) are the contract. When implementation deviates, update the doc in the
same PR and note the change under a "Changes during implementation" heading in the phase document.
