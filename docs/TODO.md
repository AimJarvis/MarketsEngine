# TODO — What's Still Pending

A concrete, honest checklist of what's not done yet. Cross-references `docs/STATUS.md` (the
source of truth for verified-vs-not) but is organized as actionable items, not a status report.
Update this as items get done — don't let it drift from reality.

## Blocking everything else — do this first

- [ ] **Buy the Zerodha Kite Connect subscription and complete the real login flow.** This is the
  single highest-leverage next step: it unblocks real market data, real historical data import,
  real backtesting results, and real order-placement verification — currently four separate
  "built but can't verify" items all behind this one decision. Steps: create a Kite Connect
  developer app at developers.kite.trade (Connect plan, paid), set the real `Kite:ApiKey`/
  `Kite:ApiSecret` via `dotnet user-secrets`, then `GET /api/broker/kite/login-url` → complete the
  browser login → `GET /api/broker/kite/callback` with the resulting `request_token`.

## Once Kite credentials exist

- [ ] Sync real instruments: `POST /api/broker/kite/sync-instruments` (NIFTY/BANKNIFTY options)
  and `/sync-equities` (your `Kite:EquityWatchlist`).
- [ ] Import real historical data: `POST /api/broker/kite/import-historical` for at least one
  equity (easiest to source) over a multi-year range.
- [ ] **Run real backtests** (`POST /api/backtests`) against that real data and actually look at
  the results — this is the whole point of building the engine. Try both strategies, try a range
  of `TakeProfitPercent`/`StopLossPercent`/`MomentumThresholdPercent` values, and be skeptical of
  any result that looks too good (overfitting is the default outcome of a single backtest run, not
  the exception — see "Ideas" in `docs/ROADMAP.md`).
- [ ] Start the Kite ticker (`POST /api/broker/kite/ticker/start`) and watch the strategy runner
  operate against real live data in **paper mode** for a real stretch of trading days before
  trusting it with anything else — real data has gaps/latency/noise mock data doesn't.
- [ ] Only after the above: flip `LiveTrading:Enabled = true` and manually approve the smallest
  possible real order yourself (see `docs/CONFIGURATION.md`) to verify `PlaceOrder`/`GetMargins`/
  `CancelOrder`/`ModifyOrder`/`OnOrderUpdate` for real, before letting any strategy signal reach
  approval unattended.

## Twilio (parked, needs a decision)

- [ ] Decide whether to upgrade the Twilio account to paid (required to register a WhatsApp
  Sender — no workaround on trial, confirmed). If yes: Console → Billing → add payment method →
  Messaging → Senders → WhatsApp senders → create sender (requires Meta Business Manager
  verification) → resubmit the `marketsengine_alert` template for Meta approval.

## Known gaps / simplifications never addressed

- [ ] No unit tests exist for anything built this session (live trading, auth, cancel/modify,
  push reconciliation, backtesting) — deliberately deferred per the project's own "no proactive
  UTs" preference. Worth a dedicated pass once the feature set stabilizes.
- [x] ~~`LivePosition` is self-reconciled from our own recorded fills, never checked against
  Kite's actual positions/holdings API~~ — **done (2026-10-28)**: `LiveOrderReconciliationService`
  now periodically compares against `kite.GetPositions()` and logs a warning on drift. Still
  compare-and-alert, not auto-corrected — a real mismatch always needs the exact Kite credentials
  that don't exist yet to actually produce one, so this remains unverified against real data (see
  `docs/STATUS.md`).
- [x] ~~Strategy price history is in-memory only and resets on every app restart~~ — **done
  (2026-10-28)**: `StrategyRunnerService` now seeds each instrument's rolling window from its
  existing `MarketQuotes` history on first use after a restart, instead of starting empty.
- [ ] No order modification for price or order type — only quantity, since every order is a
  market order (v1 scope decision, not a bug).
- [ ] Options backtesting is inherently per-contract (a few weeks of data per expiry, no
  continuous series) — much weaker as a validation tool than equities backtesting.
- [x] ~~No system-health alerting~~ — **done (2026-10-28)**: a new `HealthMonitorService`
  watchdog tracks a heartbeat from `StrategyRunnerService`/`LiveOrderReconciliationService`/
  `PnLAlertService` and logs an error (plus a best-effort WhatsApp alert if Twilio is configured)
  if any goes stale well beyond its expected interval — distinguishing "crashed" from "just
  disabled."
- [ ] No frontend/dashboard — everything is curl/Swagger. Fine for development, not fine for daily
  operation if this ever runs unattended.
- [x] ~~No CI/CD pipeline~~ — **done (2026-10-28)**: `.github/workflows/ci.yml` runs
  `dotnet build` + `dotnet test` on every push/PR to `main`.

## Also done (2026-10-28)

- [x] File-based logging: Serilog now writes to both console and a daily-rolling file at
  `src/MarketsEngine.Api/logs/marketsengine-<date>.log` (30-day retention), capturing every
  `ILogger<T>` call app-wide with zero changes to existing call sites — including the new health
  alerts above.

See `docs/ROADMAP.md` for ideas on what to prioritize among the remaining gaps and what new
capabilities might help reach the actual objective (profitable automated trading) faster or more
reliably.
