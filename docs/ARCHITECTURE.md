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

## Order execution pipeline (`OrderExecutionService`)

Every order — manual (via `POST /api/orders`), strategy-submitted, or an automated exit — goes
through the **same** `IOrderExecutionService.SubmitOrderAsync` path. No special-cased order path
exists anywhere in the codebase; this is deliberate so strategy/exit orders get every validation,
margin check, and ledger update a manual order would.

Per order: validates the instrument isn't expired (`Instrument.ExpiryDate` check, applies to mock
and Kite-sourced instruments alike), checks quote staleness (`Trading:QuoteStalenessSeconds`),
simulates a full fill against the latest quote with configurable slippage, blocks margin for
short positions (flat `Trading:ShortOptionMarginPerLot`, not real SPAN margin), deducts/credits
the cash ledger, updates `PaperPosition` (weighted-average entry price), and de-duplicates via a
required idempotency key — wrapped in a DB transaction so order+fill+ledger+position update is
all-or-nothing.

## Strategy runner (`StrategyRunnerService`)

A `BackgroundService`, off by default (`Strategy:Enabled=false`). Each pass does two independent
things, in order, both through `IOrderExecutionService`:

1. **Exit check** (`CheckExitsAsync`) — every open position on the configured account is evaluated
   against `PositionExitEvaluator` (take-profit/stop-loss on % return of cost basis, works for
   long and short) **regardless of `InstrumentKindFilter` or `ActiveStrategy`** — risk management
   must never silently stop just because the entry filter changed.
2. **Entry check** — the active `IStrategy` (resolved via keyed DI:
   `GetRequiredKeyedService<IStrategy>(settings.ActiveStrategy)`) is evaluated against every
   eligible instrument (filtered by `Strategy:InstrumentKindFilter`); a buy is submitted on signal.

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

**No order-placement endpoint or adapter exists anywhere in this integration, by design.**

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

## Background services

All registered as `AddSingleton<T>() + AddHostedService(sp => sp.GetRequiredService<T>())` so each
is both hosted by the runtime and directly injectable (e.g. `StrategyController` injects
`StrategyRunnerService` to expose `run-once`):

- `MockQuoteRefresherService` — Development only; re-ticks mock quotes every 15s.
- `KiteTickerService` — live tick ingestion when Kite is configured and started.
- `StrategyRunnerService` — entry/exit automation loop, off by default.
- `PnLAlertService` — WhatsApp P&L alert loop, off by default.
