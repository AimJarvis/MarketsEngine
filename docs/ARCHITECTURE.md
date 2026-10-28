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

## API authentication

Every endpoint requires an `X-Api-Key` header (checked against `Auth:ApiKey`, fixed-time compared
in `MarketsEngine.Api/Auth/ApiKeyAuthenticationHandler.cs`) except `GET /health`
(`.AllowAnonymous()`). Wired as ASP.NET Core's standard `AddAuthentication` +
`AddAuthorization(options => options.FallbackPolicy = RequireAuthenticatedUser())` pattern in
`Program.cs` — a global default rather than per-controller `[Authorize]` attributes, so a new
controller is protected automatically without remembering to annotate it. `Auth:ApiKey` is
required and validated at startup (same fail-fast pattern as `ConnectionStrings:Postgres`); the
app will not boot without it set.

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
Status updates after that reach the system two ways, both applying the exact same logic via a
shared `LiveOrderReconciler.ApplyUpdateAsync` (`Infrastructure/Brokers/LiveOrderReconciler.cs`):
`LiveOrderReconciliationService` (background service, off unless `LiveTrading:Enabled`) polls
`kite.GetOrders()` as a guaranteed fallback, and `KiteTickerService` additionally wires Kite's
`Ticker.OnOrderUpdate` push event (same WebSocket connection already used for market data) for
near-instant updates whenever the ticker happens to be running (it's started/stopped manually via
`POST /api/broker/kite/ticker/start`, not automatically). Either path records a `LiveFill` and
updates `LivePosition` via the **same, reused** `PositionUpdater.ApplyFill` the paper engine uses,
and both are idempotent against each other (a status that hasn't changed is a no-op), so having
both active is safe, not a race. The poller also auto-rejects any `PendingApproval` order older
than `LiveTrading:PendingApprovalExpiryMinutes`.

`POST /api/live-orders/{id}/cancel` pulls an already-placed order back from Kite
(`kite.CancelOrder`) — valid only for `Submitted`/`Open`/`PartiallyFilled` orders; a
`PendingApproval` order never reached Kite in the first place, so `/reject` is the right call for
that instead. `POST /api/live-orders/{id}/modify` (`kite.ModifyOrder`) changes an order's
**quantity only** — same state gate as cancel, and re-runs `LiveOrderRiskEvaluator` against the
new quantity before touching Kite. Scoped to quantity deliberately: a market order has no price to
modify, and widening to other order types is outside this project's "market orders only" v1 scope.

**Safety guards specific to live orders, with no paper equivalent**:
- `Instrument.DataSource` must be `"Kite"` — a live order against a mock/fictitious instrument is
  refused outright (`NonKiteInstrument`), since the paper engine's mock trading symbols are never
  meant to reach a real exchange.
- `LiveOrderRiskEvaluator` (pure, Domain) enforces `BlockedTradingSymbols`, `MaxOrdersPerDay`,
  `MaxQuantityPerOrder`, `MaxNotionalPerOrder`, and `MaxNotionalPerDay` (the last two summed from
  today's already-submitted `LiveOrder`s) — checked at submission, at approval, and again at modify
  time, since config or the day's totals may have changed in between.
- Two further checks run once, inside `PlaceWithKiteAsync`, right before the real `kite.PlaceOrder`
  call, because they need the real Kite cash balance (not available at the earlier checkpoints):
  `CashBufferPercent` (a buy may not leave less than this % of pre-trade available cash untouched)
  and `MaxPositionPercentOfEquity` (a single instrument's position, after this fill, may not exceed
  this % of total equity — Kite cash plus the market value of every open `LivePosition`).

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
   silently stop just because the entry filter changed. The thresholds used are the position's own
   `StopLossPercent`/`TakeProfitPercent` (set from the `StrategySignal` that opened it — see
   below), falling back to the global `Strategy:StopLossPercent`/`TakeProfitPercent` when the
   signal had no opinion. See above for why exits close in the same domain the position was opened
   in, not through the gateway.
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
  up by more than `Strategy:MomentumThresholdPercent`. Has no basis to size its own stop-loss/
  take-profit (the threshold is a direction signal, not a magnitude forecast), so its
  `StrategySignal` leaves `StopLossPercent`/`TakeProfitPercent` null — those positions use the
  global `Strategy:StopLossPercent`/`TakeProfitPercent` default.
- **`KronosStrategy`** (Infrastructure) — calls an external Kronos forecasting HTTP service
  (`Strategy:KronosServiceUrl`) with the recent price window, signals buy if the forecast direction
  exceeds `Strategy:KronosDirectionThresholdPercent`. Kronos itself is a general OHLCV forecasting
  model, not a trading strategy — turning a forecast into a buy/no-buy decision is this class's job.
  Unlike `SimpleMomentum`, it **does** have a magnitude to work with — its own predicted % move —
  so it sizes the position's take-profit/stop-loss off that forecast
  (`predictedChangePercent × Strategy:KronosTakeProfitFactor` /
  `× Strategy:KronosStopLossFactor`, defaults 1.0/0.5) instead of the global default. Deliberately
  simple (a fixed scale of the forecast, not a real risk model) — the point is only that Kronos has
  a basis to decide, where `SimpleMomentum` does not.

Both strategies only ever signal "buy" (never short), matching the defined-risk preference stated
up front. Exits are still evaluated uniformly by `PositionExitEvaluator` for every position — only
the thresholds fed into it can now vary per-position (see "Strategy runner" above), not the exit
mechanism itself.

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
- `KiteHistoricalDataService` — `ImportHistoricalDataAsync(instrumentId, from, to)` calls
  `kite.GetHistoricalData(..., Interval: "day")`, upserts into `HistoricalBar` (own table, keyed by
  instrument+timestamp+interval) for the backtest engine below to replay. Requires the instrument
  already synced (has a `KiteInstrumentToken`) and a completed Kite login — same requirement as
  everything else in this folder.

**No order-placement endpoint or adapter exists in `BrokerController`/`Brokers/`, by design** — real
order placement lives entirely in the separate live-trading stack above (`LiveOrderExecutionService`,
`LiveOrdersController`), which happens to depend on this folder's `KiteClientFactory` for an
authenticated client, same as `KiteInstrumentSyncService` does.

## Backtesting (`Domain/Backtesting/`, `BacktestRunnerService`)

Answers the question nothing else in this app answers: would `SimpleMomentum`/`Kronos` + the exit
logic actually have made money? Entirely separate from paper/live trading — never places an order
anywhere, reads only `HistoricalBar` rows imported via `KiteHistoricalDataService` above.

`BacktestEngine.RunAsync` (pure, Domain, no DB/HTTP dependency of its own — bars and an `IStrategy`
are passed in as plain arguments, so it's directly unit-testable) replays bars in ascending time
order through **the exact same pure calculators** the paper/live engines use:
`PositionExitEvaluator` for exits, `PositionUpdater.ApplyFill` for position/average-price math,
`PnLCalculator` for unrealized P&L and the equity curve. Per bar: exit check first (if a position
is open), then entry check (`await strategy.EvaluateAsync(...)` — real async, since a `Kronos`
backtest makes one real HTTP call per bar and needs the Kronos service running; `SimpleMomentum`
has no such dependency). Fills happen exactly at `bar.Close` — historical OHLC data has no bid/ask
spread to simulate slippage against, a deliberate, documented simplification that makes backtest
results somewhat optimistic versus live trading. Re-entry on the exact bar an exit just fired is
skipped — the simplest possible bar-level translation of the same `CooldownMinutes` fix that
closed the live re-entry race found earlier in this project.

A trade record spans one full flat→open→flat cycle (clean because strategies only ever signal buy
and exits always close the full quantity) — `EntryPrice` is the average entry price right before
closing, `RealizedPnL` is net of every fee paid across the cycle (entry + exit), not gross. Each
trade (and any still-open position at the end) also reports the `TakeProfitPercent`/
`StopLossPercent` actually resolved for that cycle (the entry signal's own override, or
`BacktestConfig`'s flat default) — not necessarily the same value across trades in one backtest when
`Kronos` is the strategy, since its override varies with each entry's forecast magnitude.

`BacktestRunnerService` (Infrastructure) loads bars for one instrument/date range, resolves the
strategy — `SimpleMomentum` is constructed fresh per request (so different thresholds can be
tried without touching live `Strategy:*` config); `Kronos` is resolved via the existing keyed DI
registration, so its forecast parameters follow whatever `Strategy:Kronos*` is currently
configured, not per-request — and calls `BacktestEngine.RunAsync`. `POST /api/backtests`
(`BacktestController`) runs this synchronously and returns the full result: trade-by-trade list
plus summary (total realized P&L, fees, win rate, max drawdown, final equity, any still-open
position, reported honestly rather than force-closed).

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
| `GET /api/broker/kite/login-url`, `GET /api/broker/kite/callback`, `GET /api/broker/kite/status`, `POST /api/broker/kite/sync-instruments`, `POST /api/broker/kite/sync-equities`, `POST /api/broker/kite/ticker/start`, `POST /api/broker/kite/ticker/stop`, `POST /api/broker/kite/import-historical` | `BrokerController` | Kite Connect auth, instrument/equity sync, ticker control, historical-data import — **no order endpoint** |
| `POST /api/backtests` | `BacktestController` | Replay a strategy against imported historical bars — never places an order |
| `GET /api/strategy/status`, `POST /api/strategy/run-once` | `StrategyController` | Inspect/manually trigger the strategy runner |
| `POST /api/notifications/test`, `POST /api/notifications/pnl-check-once` | `NotificationsController` | Manually test WhatsApp alerts |
| `POST /api/live-orders`, `GET /api/live-orders`, `GET /api/live-orders/{id}`, `POST /api/live-orders/{id}/approve`, `POST /api/live-orders/{id}/reject`, `POST /api/live-orders/{id}/cancel`, `POST /api/live-orders/{id}/modify`, `GET /api/live-orders/positions` | `LiveOrdersController` | Real-money order submission/approval/cancel/modify workflow — the only place a real Kite order can originate |

All of the above require the `X-Api-Key` header except `GET /health`.

## Background services

All registered as `AddSingleton<T>() + AddHostedService(sp => sp.GetRequiredService<T>())` so each
is both hosted by the runtime and directly injectable (e.g. `StrategyController` injects
`StrategyRunnerService` to expose `run-once`):

- `MockQuoteRefresherService` — Development only; re-ticks mock quotes every 15s.
- `KiteTickerService` — live tick ingestion when Kite is configured and started.
- `StrategyRunnerService` — entry/exit automation loop, off by default.
- `PnLAlertService` — WhatsApp P&L alert loop, off by default.
- `LiveOrderReconciliationService` — polls Kite for real order fills, expires stale pending
  approvals, and compares `LivePosition` against Kite's real `GetPositions()` (logs a warning on
  drift, never auto-corrects), off unless `LiveTrading:Enabled`.
- `HealthMonitorService` — **always runs** (not gated by any feature flag, since a watchdog you can
  turn off isn't much of a watchdog). Tracks a heartbeat (`HealthStatusTracker`, a simple
  in-memory singleton) from the three services above, recorded once per loop iteration regardless
  of whether that service's own feature flag is enabled — a heartbeat means "the loop is alive,"
  not "it did work." If a service's heartbeat goes stale beyond ~3× its own configured interval, it
  logs an error and best-effort sends a WhatsApp alert via the existing `IWhatsAppNotifier` (silent
  failure if Twilio isn't configured — the log is the guaranteed record). Distinguishes "crashed/
  hung" from "feature intentionally disabled," which a naive "last ran at X" check wouldn't.

## Logging

Serilog (`Program.cs`) replaces the default logging provider, writing every `ILogger<T>` call —
every existing call site across controllers/services, no changes needed — to both the console and
a daily-rolling file at `src/MarketsEngine.Api/logs/marketsengine-<date>.log` (30-day retention,
gitignored). This is what makes `HealthMonitorService`'s alerts (and everything else) durable
across restarts instead of only visible in whatever terminal happened to be running the app.
