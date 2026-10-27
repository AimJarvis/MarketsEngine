# Status

Last updated: 2026-10-28 (backtesting added). This file exists so a future session (or a lost-context recovery) can
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

## ⚠️ Built, but not fully verified

- **Kite Connect real market data** — `Kite:ApiKey`/`Kite:ApiSecret` are still placeholder values.
  No real Zerodha login, no real instrument sync, no live tick has ever actually flowed through
  this app. Everything in `Brokers/` has only been verified to **fail cleanly** without real
  credentials and to **not disturb** the mock-data flow. The real `request_token → access_token`
  exchange, a live instrument sync, and live ticker ticks remain untested. Deliberately deferred by
  the user: "build everything first and then pay for subscription."
- **Historical data import (`KiteHistoricalDataService`)** — verified to fail cleanly (e.g. a clear
  "sync the instrument first" error when `KiteInstrumentToken` is missing). The actual
  `kite.GetHistoricalData()` call has never run — same real-credentials blocker as above. Until
  real data is imported, the backtest engine (verified separately, see above) has nothing real to
  replay beyond hand-seeded synthetic test data.
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
