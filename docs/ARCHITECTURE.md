# Architecture

## Layering

```
MarketsEngine.Api            → controllers, DI composition root, Swagger, mock-data seeding
      ↓ references
MarketsEngine.Infrastructure → EF Core DbContext, order execution orchestration,
                                Kite/Kronos/Twilio integrations, background services
      ↓ references
MarketsEngine.Domain          → entities, enums, pure calculators, strategy abstraction
                                 (no EF Core, no HTTP clients, no external packages beyond
                                 System.ComponentModel.DataAnnotations)
```

`Domain` is intentionally kept free of infrastructure concerns — `FillPriceCalculator`,
`PositionUpdater`, `MarginCalculator`, `PnLCalculator`, `PositionExitEvaluator` are all static,
pure, and testable without a database. Controllers never touch EF Core directly; they call into
`Infrastructure` services.

## Core entities

`Instrument`, `MarketQuote`, `PaperAccount`, `PaperOrder`, `PaperFill`, `PaperPosition`,
`CashLedgerEntry`, `BrokerSession`.

`Instrument` supports both options and equities via a single `Kind` (`InstrumentKind.Option` /
`.Equity`) field: `Underlying`, `OptionType`, and `ExpiryDate` are nullable and simply left null for
equities. **The order execution engine needed zero changes to support equities** — it already
treated "lot size" generically (options lot size > 1, equities lot size 1).

## Order execution pipeline (`OrderExecutionService`) — paper

Every **paper** order — manual (via `POST /api/orders`), strategy-submitted, or an automated exit
on a `PaperPosition` — goes through the **same** `IOrderExecutionService.SubmitOrderAsync` path.
No special-cased order path exists for paper orders; this is deliberate so strategy/exit orders
get every validation, margin check, and ledger update a manual order would.

Per order: validates the instrument isn't expired (`Instrument.ExpiryDate` check, applies to mock
and Kite-sourced instruments alike), checks quote staleness (`Trading:QuoteStalenessSeconds`),
simulates a full fill against the latest quote with configurable slippage, blocks margin for
short positions (flat `Trading:ShortOptionMarginPerLot`, not real SPAN margin), deducts/credits
the cash ledger, updates `PaperPosition` (weighted-average entry price), and de-duplicates via a
required idempotency key — wrapped in a DB transaction so order+fill+ledger+position update is
all-or-nothing. **This path is completely untouched by live trading** — see below.

## Live order execution (`LiveOrderExecutionService`) — real money, via Kite Connect

A fully parallel stack to the paper engine, living in `Domain/LiveTrading/` and
`Infrastructure/Services/LiveOrderExecutionService.cs`, added deliberately as a **separate**
surface rather than an extension of `IOrderExecutionService` — `OrderSubmissionResult` is
hard-wired to `PaperOrder`/`PaperFill` and `OrderStatus` only models `Filled`/`Rejected`, neither
fits Kite's real asynchronous order lifecycle. Uses its own tables (`LiveOrder`, `LiveFill`,
`LivePosition`) so real and simulated history can never mix.

**The switch**: `ITradingOrderGateway` is the only thing entry-signal callers
(`StrategyRunnerService`) depend on. Its implementation, `TradingOrderGateway`, reads
`LiveTrading:Enabled` (via `IOptionsMonitor`, so a config edit takes effect on the very next order,
no restart) and routes a fresh entry to `IOrderExecutionService` (paper, unchanged) when false, or
`ILiveOrderExecutionService` (live) when true. `LiveTrading:Enabled` is simultaneously the paper/live
mode switch and the real-money kill switch — distinct from `Trading:PaperTradingEnabled`.

**Manual-approval lifecycle** (`LiveTrading:RequireManualApproval`, default true): a validated
signal is persisted as `LiveOrder.Status = PendingApproval` and **Kite is never contacted** until
a human calls `POST /api/live-orders/{id}/approve` (`LiveOrdersController`). Approval re-runs the
kill switch and risk checks (config/state may have changed), checks real Kite margin via
`kite.GetMargins()`, then calls `kite.PlaceOrder(...)` (market orders only, `LiveTrading:DefaultProduct`).
`LiveOrderReconciliationService` (background service, off unless `LiveTrading:Enabled`) polls
`kite.GetOrders()` to detect fills, records a `LiveFill`, and updates `LivePosition` via the
**same, reused** `PositionUpdater.ApplyFill` the paper engine uses. It also auto-rejects any
`PendingApproval` order older than `LiveTrading:PendingApprovalExpiryMinutes`.

**Safety guards specific to live orders, with no paper equivalent**:
- `Instrument.DataSource` must be `"Kite"` — a live order against a mock/fictitious instrument is
  refused outright (`NonKiteInstrument`), since the paper engine's mock trading symbols are never
  meant to reach a real exchange.
- `LiveOrderRiskEvaluator` (pure, Domain) enforces `MaxQuantityPerOrder`, `MaxNotionalPerOrder`, and
  `MaxNotionalPerDay` (summed from today's already-submitted `LiveOrder`s) — checked both when the
  order is parked and again right before the real Kite call.

**Exits are domain-locked, not mode-switched**: `StrategyRunnerService.CheckExitsAsync` closes a
`PaperPosition` via `IOrderExecutionService` and a `LivePosition` via `ILiveOrderExecutionService`
**directly** — never through `ITradingOrderGateway`. An existing position must always close in the
same domain it was opened in; routing an exit through the gateway would mean flipping
`LiveTrading:Enabled` while an old paper position is still open could fire a real Kite order for
it. Only entries (which have no existing position yet) go through the gateway.

## Strategy runner (`StrategyRunnerService`)

A `BackgroundService`, off by default (`Strategy:Enabled=false`). Each pass does two independent
things, in order:

1. **Exit check** — every open `PaperPosition` and `LivePosition` is evaluated against
   `PositionExitEvaluator` (take-profit/stop-loss on % return of cost basis, works for long and
   short) **regardless of `InstrumentKindFilter` or `ActiveStrategy`** — risk management must never
   silently stop just because the entry filter changed. See above for why exits close in the same
   domain the position was opened in, not through the gateway.
2. **Entry check** — the active `IStrategy` (resolved via keyed DI:
   `GetRequiredKeyedService<IStrategy>(settings.ActiveStrategy)`) is evaluated against every
   eligible instrument (filtered by `Strategy:InstrumentKindFilter`); a buy is submitted via
   `ITradingOrderGateway` on signal — paper or live, decided entirely by `LiveTrading:Enabled`.

An instrument is skipped for entry for `Strategy:CooldownMinutes` after it was last exited — this
exists because of a real bug found via live testing: a take-profit exit and a momentum entry
signal can both react to the exact same price spike in the same pass, causing an immediate,
worse-priced re-buy right after the profitable exit. See `docs/STATUS.md` for the reproduction.

Price history for momentum/forecast strategies is kept in-memory, per instance, as a
`Dictionary<Guid, Queue<PricePoint>>` bounded by `Strategy:LookbackSamples` — it is not persisted
and resets on restart.

## Strategies (`IStrategy`)

Pure, stateless, keyed by name in DI:

- **`SimpleMomentumStrategy`** (Domain) — rule-based: signals buy if the recent price window moved
  up by more than `Strategy:MomentumThresholdPercent`.
- **`KronosStrategy`** (Infrastructure) — calls an external Kronos forecasting HTTP service
  (`Strategy:KronosServiceUrl`) with the recent price window, signals buy if the forecast direction
  exceeds `Strategy:KronosDirectionThresholdPercent`. Kronos itself is a general OHLCV forecasting
  model, not a trading strategy — turning a forecast into a buy/no-buy decision is this class's job.

Both strategies only ever signal "buy" (never short), matching the defined-risk preference stated
up front. All exits are handled uniformly by `PositionExitEvaluator`, not by the strategies.

## Kite Connect integration (`MarketsEngine.Infrastructure/Brokers/`)

Deliberately namespaced `Brokers`, not `Kite`/`KiteConnect`, to avoid identifier collisions with
the official `Tech.Zerodha.KiteConnect` NuGet package's own `Kite`/`Instrument`/`Quote` types.

- `KiteClientFactory` — throws `KiteNotConfiguredException` with an actionable message when
  `Kite:ApiKey`/`Kite:ApiSecret` or a session is missing. Kite is **optional**; the app must keep
  working in pure mock mode without it.
- `KiteSessionStore` — persists the access token in `BrokerSession`.
- `KiteInstrumentSyncService` — `SyncNiftyBankNiftyOptionsAsync()` (NFO options, nearest 2
  expiries) and `SyncEquitiesAsync()` (filtered to `Kite:EquityWatchlist`), both upsert into the
  existing `Instruments` table tagged `DataSource="Kite"`.
- `KiteTickerService` — singleton WebSocket ticker, replaces (not appends) the latest
  `MarketQuote` per tick, same pattern as the mock quote refresher.

**No order-placement endpoint or adapter exists in `BrokerController`/`Brokers/`, by design** — real
order placement lives entirely in the separate live-trading stack above (`LiveOrderExecutionService`,
`LiveOrdersController`), which happens to depend on this folder's `KiteClientFactory` for an
authenticated client, same as `KiteInstrumentSyncService` does.

## Notifications (Twilio WhatsApp)

`IWhatsAppNotifier` (Domain) / `TwilioWhatsAppNotifier` (Infrastructure) wraps the official
`Twilio` NuGet package. `PnLAlertService` (background service, off by default via
`PnLAlerts:Enabled`) periodically checks each watched account's P&L via `AccountPnLService`
(shared with `AccountsController` to avoid duplicated P&L logic) and sends a WhatsApp message only
when net P&L has moved by at least `PnLAlerts:AlertThresholdAmount` since the last alert for that
account. Business-initiated messages outside a 24h session window require a Twilio **Content
Template** (`Twilio:ContentSid`) — free-form `body` messages fail with `ContentSid Required`
outside that window.

## HTTP API surface

| Route | Controller | Purpose |
|---|---|---|
| `GET /api/instruments`, `GET /api/instruments/{id}/quote` | `InstrumentsController` | List instruments / latest quote |
| `POST /api/accounts`, `GET /api/accounts/{id}`, `GET /api/accounts/{id}/positions`, `GET /api/accounts/{id}/pnl`, `GET /api/accounts/{id}/orders` | `AccountsController` | Paper accounts, positions, P&L, order history |
| `POST /api/orders`, `GET /api/orders/{id}` | `OrdersController` | Submit / fetch paper orders |
| `GET /api/broker/kite/login-url`, `GET /api/broker/kite/callback`, `GET /api/broker/kite/status`, `POST /api/broker/kite/sync-instruments`, `POST /api/broker/kite/sync-equities`, `POST /api/broker/kite/ticker/start`, `POST /api/broker/kite/ticker/stop` | `BrokerController` | Kite Connect auth, instrument/equity sync, ticker control — **no order endpoint** |
| `GET /api/strategy/status`, `POST /api/strategy/run-once` | `StrategyController` | Inspect/manually trigger the strategy runner |
| `POST /api/notifications/test`, `POST /api/notifications/pnl-check-once` | `NotificationsController` | Manually test WhatsApp alerts |
| `POST /api/live-orders`, `GET /api/live-orders`, `GET /api/live-orders/{id}`, `POST /api/live-orders/{id}/approve`, `POST /api/live-orders/{id}/reject`, `GET /api/live-orders/positions` | `LiveOrdersController` | Real-money order submission/approval workflow — the only place a real Kite order can originate |

## Background services

All registered as `AddSingleton<T>() + AddHostedService(sp => sp.GetRequiredService<T>())` so each
is both hosted by the runtime and directly injectable (e.g. `StrategyController` injects
`StrategyRunnerService` to expose `run-once`):

- `MockQuoteRefresherService` — Development only; re-ticks mock quotes every 15s.
- `KiteTickerService` — live tick ingestion when Kite is configured and started.
- `StrategyRunnerService` — entry/exit automation loop, off by default.
- `PnLAlertService` — WhatsApp P&L alert loop, off by default.
- `LiveOrderReconciliationService` — polls Kite for real order fills and expires stale pending approvals, off unless `LiveTrading:Enabled`.
