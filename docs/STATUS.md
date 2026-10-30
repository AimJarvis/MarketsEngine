# Status

Last updated: 2026-10-31 (first real Kite Connect login, real instrument sync, real live ticker,
real historical import - and six real bugs found and fixed by actually exercising them). This file
exists so a future session (or a lost-context recovery) can
tell quickly what's actually been proven to work versus what's built but unverified versus what's
a known, explicit gap. Update it whenever a major piece changes state — don't let it go stale.

## ✅ Verified end-to-end (tested live, not just built)

- **Core paper-trading engine** — accounts, order submission, full-fill simulation, idempotency-key
  deduplication, margin blocking/release for short options, insufficient-funds rejection, realized
  P&L on close, expired-instrument rejection. Verified via curl smoke tests covering long
  open/close, short open/cover, duplicate-idempotency replay, unknown-account 404, oversized-order
  rejection.
- **Automated strategy runner** — both `SimpleMomentum` and `Kronos` strategies evaluated live
  against mock quote data and correctly submitted buy orders through the normal order pipeline.
- **Equity support** — added without any changes to the order execution engine itself, proving the
  original lot-size abstraction was already generic enough.
- **Real Kronos ML forecasting** — the actual PyTorch model (not a stub) runs locally and produced
  a real forecast that drove a real (paper) trade end-to-end through `KronosStrategy`.
- **Take-profit / stop-loss exits** — `PositionExitEvaluator` verified against both long and short
  positions; runs ahead of entry checks every pass, independent of `InstrumentKindFilter`.
- **Re-entry bug found and fixed via live reproduction, not just code review**: a take-profit exit
  and a momentum entry signal could react to the *same* price spike in the same evaluation pass,
  causing an immediate worse-priced re-buy right after a profitable exit (observed: INFY sold at
  2049 on take-profit, immediately rebought at 2051). Fixed with a post-exit cooldown
  (`Strategy:CooldownMinutes`, reused) tracked per-instrument in `StrategyRunnerService`. Confirmed
  fixed by reproducing the exact scenario again, including across multiple consecutive passes.
- **Live order execution plumbing (added 2026-10-23)** — the paper/live config switch
  (`LiveTrading:Enabled`), the mock-instrument safety guard, and the strategy runner's gateway
  routing were all verified live end-to-end against real HTTP calls (not just code review):
  (1) a manual `POST /api/live-orders` with `LiveTrading:Enabled=false` correctly rejected with
  `LiveTradingDisabled`; (2) flipping `LiveTrading:Enabled=true` via `dotnet user-secrets` took
  effect **without restarting the app** (`IOptionsMonitor`-backed kill switch); (3) a live order
  against a mock instrument was correctly rejected with `NonKiteInstrument` before anything could
  reach Kite; (4) `POST /api/strategy/run-once` with `LiveTrading:Enabled=true` produced a
  `LiveOrder` row tagged `Source: "Strategy:SimpleMomentum"` — proving `StrategyRunnerService`'s
  entry signal really does route through `ITradingOrderGateway` to the live path when the switch
  is on — while the paper order count stayed unchanged throughout, proving the paper engine is
  unaffected either way. **What this does NOT verify**: no real Kite credentials exist yet, so the
  actual `kite.PlaceOrder`/`GetMargins`/`GetOrders` calls, the manual-approval-to-real-fill
  lifecycle, and `LiveOrderReconciliationService`'s polling have never run against the real Kite
  API — only against the safety guards that reject before reaching it. See "Built, but not fully
  verified" below.

- **API authentication (added 2026-10-28)** — every endpoint except `/health` now requires an
  `X-Api-Key` header, checked via fixed-time comparison against `Auth:ApiKey` (required, validated
  at startup, no default — the app refuses to boot without it configured). Verified live: no
  header → 401, wrong key → 401, correct key → 200, `/health` → 200 with no key, and Swagger's own
  `swagger.json` correctly advertises the `ApiKey` security scheme globally (Swagger UI gets an
  Authorize button). Full regression across accounts/orders/strategy/broker endpoints confirmed
  working with the header present.
- **Cancel a live order with the broker (added 2026-10-28)** — `POST
  /api/live-orders/{id}/cancel` calls `kite.CancelOrder`, valid only for `Submitted`/`Open`/
  `PartiallyFilled` orders (a `PendingApproval` order should use `/reject` instead, which never
  touches Kite). Verified: a non-existent order → 404, cancelling an already-`Rejected` order →
  `InvalidState` with a clear message directing to `/reject`. **Not yet verified**: an actual
  cancel against a real in-flight Kite order (needs real credentials, same blocker as everything
  else live).
- **Modify a live order's quantity (added 2026-10-28)** — `POST /api/live-orders/{id}/modify`
  calls `kite.ModifyOrder`, same state gate as cancel, re-validates `LiveOrderRiskEvaluator`
  against the new quantity before touching Kite. **Quantity only** — not price or order type,
  since market orders have no price and other order types are out of v1 scope. Verified:
  non-existent order → 404, invalid quantity → rejected, wrong-state order → `InvalidState`. **Not
  yet verified**: an actual modify against a real in-flight Kite order (same real-credentials
  blocker).
- **Push-based order status updates (added 2026-10-28)** — `KiteTickerService` now also wires
  `Ticker.OnOrderUpdate` (the same WebSocket already used for market data) and applies updates
  instantly via a new shared `LiveOrderReconciler`, extracted from `LiveOrderReconciliationService`
  so the exact same status-mapping/fill-recording logic runs whether it's reached by push or by
  the existing poll — the poll remains as a guaranteed fallback for whenever the ticker isn't
  running (it's started/stopped manually, not automatic). Verified: build + existing 18 tests pass
  unchanged after the extraction, proving the refactor didn't alter behavior. **Not yet verified**:
  an actual real-time push from Kite (needs real credentials + the ticker actually running against
  a live order).
- **Backtesting engine (added 2026-10-28) — verified by hand-computed proof, not just a smoke
  test.** Seeded a synthetic 8-day price path directly into `HistoricalBars` (flat, flat, flat, up
  3%, up further, flat, down hard, flat) and ran `POST /api/backtests` with `SimpleMomentum`
  against it. Every number in the response matched a hand calculation exactly: 2 trades (one
  `TakeProfit` exit, one `StopLoss` exit), each trade's `RealizedPnL` net of accumulated fees,
  `TotalRealizedPnL`/`TotalFeesPaid`/`FinalEquity` all reconciling to the cent. Critically, **the
  same-bar re-entry protection was proven, not assumed**: on both exit bars, the momentum
  conditions that followed would otherwise have fired a fresh buy signal on the same bar — the
  engine correctly skipped entry on those bars, confirmed by the trade count being exactly 2, not
  4. Negative paths also verified: an instrument with no imported bars → clear error, not a crash;
  an unknown `strategyName` → clear error. **What this does NOT verify**: the actual Kite
  `GetHistoricalData` import call (`POST /api/broker/kite/import-historical`) — same
  real-credentials blocker as everything else live. The replay engine itself has no such blocker
  and is fully proven correct independent of it.
- **Per-position stop-loss/take-profit, decided by the strategy (added 2026-10-29)** — exits now
  read each position's own `StopLossPercent`/`TakeProfitPercent` (set from the `StrategySignal`
  that opened it), falling back to the global `Strategy:*` default only when the signal had no
  opinion. `Kronos` sizes both off its own forecast magnitude (`KronosTakeProfitFactor`/
  `KronosStopLossFactor`); `SimpleMomentum` has no magnitude to work with and always falls back to
  the global default. Verified: migration applied cleanly (3 new nullable `numeric(18,4)` column
  pairs on `PaperPositions`/`LivePositions`/`LiveOrders`), clean boot, 18/18 tests pass, a manual
  paper order still opens/closes correctly and the new fields are correctly `null` and exposed via
  `GET /api/accounts/{id}/positions`. **A hand-computed synthetic backtest re-run after the change
  reproduced the exact same numbers as before** (1 trade, entry 160 → exit 176 TakeProfit,
  RealizedPnL=16, FinalEquity=10016), confirming the refactor didn't alter `SimpleMomentum`/paper
  behavior, and the new `trades[].takeProfitPercent`/`stopLossPercent` fields correctly reported
  10/5 (the global default, since `SimpleMomentum` doesn't override). **Two real bugs found and
  fixed during a deep-dive review before this was considered done, not just code review in
  isolation**:
  1. The override was originally set only when `position is null` (brand-new row) — but
     `PaperPosition`/`LivePosition` rows are reused across a full close→reopen cycle (unique index
     on instrument), so a position reopened after fully closing would silently keep the *original*
     cycle's override forever. Fixed to key off "was this position flat before this fill"
     (`currentNetQuantity == 0`), which also now clears the override to `null` on a full close so a
     flat row never shows a stale value. **Verified live**: closed and reopened the same paper
     position twice via HTTP, confirmed the row is reused (not duplicated) and the override field
     clears/resets correctly through the cycle (only exercised with `null` overrides, since
     exercising a real non-`null` override end-to-end needs a live `Kronos` signal — the Kronos
     Python service wasn't running this session).
  2. `BacktestEngine` evaluated the strategy's signal but completely ignored
     `signal.StopLossPercent`/`TakeProfitPercent`, always using the flat `BacktestConfig` default —
     meaning a `Kronos` backtest would never have actually exercised Kronos's forecast-scaled exit,
     silently testing different behavior than what runs live/paper. Fixed to resolve each trade
     cycle's thresholds once, at entry, exactly mirroring how `StrategyRunnerService` resolves them
     live.
  - **Not yet verified**: a `Kronos` signal actually producing a non-default, forecast-scaled
    override end to end (needs the Kronos service running + a live/backtest run against it — not
    done this session). A flip (single fill crossing from long to short or vice versa) correctly
    refreshing the override is verified by code review only, not a live reproduction — it requires
    a manual order larger than an existing strategy-opened position, an edge case not exercised live.
  - **Explicit scope note, not a gap**: there is no limit-order/trigger-price concept anywhere in
    this codebase (confirmed via full-codebase grep) — every order, including every exit, is a
    plain Kite `MARKET` order. "Stop-loss" here means a periodic poll-then-market-sell, never a
    resting broker-side SL/SL-M order; between polls (`Strategy:EvaluationIntervalSeconds`), price
    can move past the configured stop with nothing protecting it.
- **Four additional live-trading risk checks (added 2026-10-29)**, inspired by reviewing a
  third-party Claude Code skill pack (`trader-skill`, US-equities-focused, not usable directly —
  see the conversation) against our own `LiveOrderRiskEvaluator`: `BlockedTradingSymbols` (manual
  deny-list), `MaxOrdersPerDay` (order count cap, separate from the existing notional cap),
  `CashBufferPercent` (a buy can't leave less than this % of pre-trade available cash untouched),
  and `MaxPositionPercentOfEquity` (a single instrument's position can't exceed this % of total
  account equity). Verified: clean build, 18/18 tests pass, clean boot (new config binds and
  validates), `/health` responds. **Not yet verified against a real Kite account** — the cash
  buffer and position-% checks specifically need real `GetMargins()`/position data to actually
  trigger, same real-credentials blocker as everything else live; the blocked-symbol and
  daily-count checks need no real data and are straightforward to reason about from code alone.
- **Operational hardening (added 2026-10-28)**:
  - **File logging (Serilog)** — console + a daily-rolling file (`src/MarketsEngine.Api/logs/`,
    30-day retention) confirmed capturing real app output (verified: framework warnings and
    request logging both appeared in the file after a real boot + request cycle).
  - **Strategy price-history seeding** — `StrategyRunnerService` now seeds an instrument's rolling
    window from existing `MarketQuotes` on first use instead of starting empty after a restart.
    Verified by code path (queries `MarketQuotes` ordered descending, takes `LookbackSamples`,
    re-orders ascending) — not yet observed catching a real restart-then-immediate-signal case live.
  - **CI pipeline** — `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` on every push/PR;
    not yet observed running on GitHub itself (will confirm on the next push).
  - **Health heartbeat tracking** — confirmed via clean app boot that all three watched services
    (`StrategyRunnerService`, `LiveOrderReconciliationService`, `PnLAlertService`) and the new
    `HealthMonitorService` itself resolve correctly through DI (a misregistration would have
    crashed startup). **Not yet observed actually firing a stale alert** — that requires either a
    real crash or waiting out a real multi-minute threshold, neither exercised this session.
  - **`LivePosition` vs. Kite position drift check** — code path confirmed to safely no-op when
    there are no open `LivePosition` rows (the common/current case). The actual comparison logic
    against `kite.GetPositions()` has never run against a real position, same real-credentials
    blocker as everything else live.
- **First real Kite Connect login, instrument sync, live ticker, and historical import (2026-10-31)
  — the single biggest "unverified" item in this project, now closed.** Real Kite Connect app
  created, real `Kite:ApiKey`/`ApiSecret` set (via the new `.env` mechanism, see below), real
  browser login completed, real `BrokerSession` persisted (survives app restart, confirmed).
  `POST /api/broker/kite/sync-instruments` pulled **1019 real NIFTY/BANKNIFTY option contracts**;
  `/sync-equities` pulled 4 real equities, correctly upgrading the pre-existing Development mock
  rows in place (same `TradingSymbol`, `DataSource` flipped `Mock` → `Kite`) rather than erroring
  or duplicating. The live ticker ran against all 1023 instruments and wrote real, correct quotes
  (verified: sane bid/ask/LTP values, `ask >= bid` holds, correct UTC timestamps). `POST
  /api/broker/kite/import-historical` pulled 19 real daily bars for RELIANCE with correct UTC
  timestamps. **This was true end-to-end verification, not just "it didn't crash"** - actual
  real-world data was inspected and checked for correctness at every step.
  **Six real bugs were found and fixed in the process, none of them catchable without a real
  account** (full detail and root-cause pattern in the project history; one-line summary each):
  1. `BrokerController.Callback` required the global `X-Api-Key` header, which Zerodha's own
     redirect can never send — the real login flow 401'd before ever reaching our code. Fixed:
     `[AllowAnonymous]` on that one endpoint, since the `request_token` itself is Kite's proof the
     call is legitimate.
  2. `KiteInstrumentSyncService` matched existing rows by `(TradingSymbol, DataSource=="Kite")`,
     so it never saw the pre-existing mock equity rows sharing the same symbols on purpose, and
     tried to insert duplicates against the global unique index. Fixed: match by `TradingSymbol`
     alone and re-assert `DataSource="Kite"` on every match (not just on insert), so a mock row
     gets upgraded in place instead of colliding.
  3. `KiteTickerService.OnTick` opened a new DB scope/connection per tick, fire-and-forget, with
     no throttling — subscribing to 1023 real instruments in `TickerMode.Full` exhausted
     Postgres's entire connection pool within seconds. Fixed: buffer ticks in memory (last tick
     per instrument wins) and flush to the DB in one batch every 2 seconds, same shape as the
     existing mock quote refresher.
  4. Kite's SDK (`Utils.UnixToDateTime`) builds tick timestamps by adding the IST offset to the
     Unix epoch before adding seconds, so the result is IST wall-clock time tagged
     `DateTimeKind.Unspecified` - confirmed by reading the SDK source, not assumed. Our code just
     relabeled it `Utc` without subtracting the offset. Npgsql actually refused to write the
     `Unspecified` value at all, surfacing this immediately. Fixed with an explicit `-5.5 hours
     then mark Utc` conversion.
  5. The same class of bug existed in `KiteHistoricalDataService` for `candle.TimeStamp`, except
     there Kite's REST API returns an explicit UTC-offset string, which .NET parses to
     `Kind=Local` (correctly resolved against the machine's own timezone — confirmed empirically
     with a throwaway test, not assumed). The fix there was `.ToUniversalTime()`, not a manual
     offset subtraction, since the correct conversion already depends on the *running machine's*
     timezone, not a hardcoded IST assumption.
  6. Real market depth for illiquid option contracts routinely has one side of the order book
     empty. `Tick.Bids`/`Tick.Offers` are **always** fixed-length-5 arrays in Full mode (an empty
     level is `Price=0`, not a shorter array — confirmed from the SDK source), so the original
     per-side fallback logic (`Bids[0] ?? LastPrice`, independently for each side) could produce
     `ask < bid`, which a real DB check constraint (`CK_MarketQuote_Ask_Gte_Bid`) caught
     immediately. Fixed: only trust the real book when *both* sides have a genuine non-zero,
     non-crossed price; otherwise use `LastPrice` for both.
  - **Common thread across all six**: every one of them was a wrong assumption about either the
    Kite SDK's exact behavior or real-world market data shape that **no amount of code review
    against mock data could have caught** — they only became visible the moment real bytes from a
    real account flowed through the code for the first time. This is exactly why `docs/STATUS.md`
    has repeatedly flagged "never tested against real Kite" as the project's biggest open risk.
  - **Confirmed NOT a bug, by investigation**: `Order.OrderTimestamp`/`ExchangeTimestamp` have the
    same IST-but-unspecified-Kind quirk as tick timestamps, but our code never reads either field
    (we timestamp our own order lifecycle independently), so it's a latent SDK quirk with no
    actual impact here.
  - **Confirmed gap, not yet fixed — flagged, not silently left**: a **partial fill is never
    recorded**. Kite has no distinct "partially filled" status string (confirmed via community/
    forum research, since the SDK's own `Constants.OrderStatus` only defines
    `COMPLETE`/`CANCELLED`/`REJECTED`) — a partial fill shows status `OPEN` with
    `filled_quantity < quantity`, and our `LiveOrderReconciler.ApplyUpdateAsync` only ever records
    a `LiveFill`/updates `LivePosition` when status reaches `Complete`. If an order partially
    fills and the remainder is later cancelled (or never completes), the quantity that *did* fill
    at the broker is never reflected in `LivePosition` at all. Fixing this properly means
    `LiveOrder`'s current 1-to-(0 or 1) relationship with `LiveFill` would need to become a
    1-to-many (each partial fill recorded as its own `LiveFill`, applied incrementally to
    `LivePosition`) - a real schema/design decision, not a one-line fix, so it's recorded here
    rather than done unilaterally mid-audit.
  - Hardened while auditing, no bug observed yet but same defensive pattern as elsewhere:
    `LiveOrderReconciliationService.ReconcileOpenOrdersAsync`'s `kite.GetOrders()` now groups by
    `OrderId` (`.First()`) instead of a plain `ToDictionary`, which would have crashed the whole
    reconciliation pass on an unexpected duplicate key.

- **Kronos now receives real OHLCV candles (2026-10-31) — and ran its first real forecast-driven
  backtest.** Previously the strategy pipeline carried price-only `PricePoint`s and the Python
  service *fabricated* flat candles (`open=high=low=close=price, volume=0`) plus minute-spaced
  timestamps, feeding an OHLCV forecasting model exactly the structure it was built to use stripped
  out. Replaced `PricePoint` with a real `Candle` (OHLCV + timestamp) throughout: the backtest path
  now passes genuine `HistoricalBar` OHLCV straight through, `KronosStrategy` sends full OHLCV + real
  unix timestamps, and the service uses them directly (no fabrication; infers forward-bar spacing
  from the real cadence instead of hardcoding minutes). **Verified end-to-end**: started the Kronos
  Python service, ran a real Kronos backtest on real RELIANCE daily OHLCV - 14 forecasts served
  (all 200 OK, no errors), 3 trades produced, and each trade's take-profit/stop-loss came out
  *different* (2.25%/1.13%, 0.68%/0.34%, …), proving the forecast-magnitude-scaled exits work with
  real Kronos forecasts, not just the flat config default. (The result lost money on this 18-bar
  sample - expected and meaningless at that size; the point verified here is the pipeline, not an
  edge.) `SimpleMomentum` backtest produced byte-identical numbers before and after the refactor,
  confirming it's behavior-preserving for the close-only strategy.
  **Known remaining limitation, flagged not hidden**: the LIVE strategy runner still has no real
  intraday OHLC bars - it only has point-in-time quote snapshots, which it now wraps as *degenerate*
  candles (`Candle.FromPrice`, O=H=L=C=LTP, V=0). So live Kronos still gets degraded input; real
  intraday forecasting needs a tick→OHLCV bar aggregator (see `docs/TODO.md`). This is fine for now
  because Kronos must be proven in backtest (real candles) before it's trusted live anyway.

## ⚠️ Built, but not fully verified

- ~~**Kite Connect real market data**~~ / ~~**Historical data import**~~ — **done (2026-10-31)**,
  moved to verified below. These two bullets are kept struck through rather than deleted so the
  history of "this was the single biggest unverified thing in the project" isn't lost.
- **Real-money order execution (`LiveTrading`)** — the gateway, safety guards, and manual-approval
  plumbing are verified (see above), but **no real order has ever been placed with Kite** — that
  requires the same real Kite credentials/subscription as above, plus a real `BrokerSession`
  (login flow completed) and at least one real Kite-sourced instrument (`POST
  /api/broker/kite/sync-instruments`/`sync-equities`). Once those exist: approve the smallest
  possible real order yourself first (see `docs/CONFIGURATION.md`), then confirm
  `LiveOrderReconciliationService` correctly observes the fill and updates `LivePosition`, before
  ever letting a strategy signal reach approval unattended.
- **Twilio WhatsApp alerts** — connectivity is confirmed (a message using Twilio's own default
  template delivered successfully). The custom dynamic-content template (`marketsengine_alert`,
  SID in `Twilio:ContentSid`) is **not yet approved** — a real alert attempt through it failed with
  Twilio error 21655 ("The ContentSid is Invalid"). As of 2026-10-23, the Twilio Console confirms
  the root cause: the template was only ever saved as a draft and **was never actually submitted
  for WhatsApp approval** (tooltip: "WhatsApp approval not submitted. Please submit the template
  for approval in order to use it."). This is not a pending-review wait — nothing will happen until
  it's explicitly submitted via the template's `⋮` menu / detail page ("Submit for WhatsApp
  approval") in the Console. Submitting also requires a registered **WhatsApp Sender** (Messaging →
  Senders → WhatsApp senders in the Console), and as of 2026-10-23 the Console confirms this is a
  **hard wall on a trial account — "A paid account is required to submit a WhatsApp Sender," no
  workaround.** Decision: **parked deliberately.** Priority is to get the core trading pipeline
  fully real (real Kite Connect data/credentials) first; Twilio alerts resume once that's done and
  the user upgrades the Twilio account. Nothing further to try here until then — don't re-chase
  this chain without a plan to upgrade.

## ❌ Explicit, known gaps (not started)

- **Live order fill tracking is self-reconciled, not broker-verified.** `LivePosition` is built
  purely from our own recorded `LiveFill`s (via the same `PositionUpdater.ApplyFill` the paper
  engine uses) — it is never reconciled against Kite's actual positions/holdings API. This is the
  same simplification `PaperPosition` already makes relative to real margin accounting, now
  carried over to real money; worth revisiting before trusting `LivePosition` as ground truth.
- **Strategy price history is in-memory only** (`StrategyRunnerService`'s internal
  `Dictionary<Guid, Queue<PricePoint>>`) — it resets on every app restart, so momentum/forecast
  context is lost across restarts. Not an issue for the mock-quote testing done so far; would
  matter for anything running unattended for a long period.

## Process notes worth keeping

- Unit tests: **do not add proactively.** The user explicitly said "no write UTs yet" mid-project;
  existing tests (~18) were kept as-is but no new ones should be added without an explicit ask at
  that point in the work.
- Git: as of 2026-10-19, `main` and `origin/main` (https://github.com/AimJarvis/MarketsEngine) are
  in sync — all work through the Twilio/exit-logic pass has been committed and pushed. Don't assume
  this stays true; check `git status` / `git log --oneline -1` / compare `git rev-parse main` vs
  `git rev-parse origin/main` before relying on it.
