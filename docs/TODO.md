# TODO — What's Still Pending

A concrete, honest checklist of what's not done yet. Cross-references `docs/STATUS.md` (the
source of truth for verified-vs-not) but is organized as actionable items, not a status report.
Update this as items get done — don't let it drift from reality.

## Blocking everything else — do this first

- [x] ~~Buy the Zerodha Kite Connect subscription and complete the real login flow.~~ — **done
  (2026-10-31)**: real Kite Connect app created, real login completed, `BrokerSession` persisted
  and confirmed surviving a restart. See `docs/STATUS.md` for the full verification (and six real
  bugs found and fixed in the process).
- [x] ~~Sync real instruments~~ — **done (2026-10-31)**: 1019 real NIFTY/BANKNIFTY option contracts
  + 4 real equities synced, correctly upgrading the pre-existing Development mock rows in place.
- [x] ~~Start the Kite ticker and confirm real live data flows~~ — **done (2026-10-31)**: real
  ticks confirmed flowing with correct timestamps and sane bid/ask/LTP values, DB connection usage
  bounded (was exhausting Postgres entirely before the fix - see `docs/STATUS.md`).
- [x] ~~Import real historical data~~ — **done (2026-10-31)**: 19 real daily RELIANCE bars
  imported with correct UTC timestamps.

## Now that real Kite data flows

- [ ] **Run real backtests** (`POST /api/backtests`) against the real RELIANCE data already
  imported (or import more first - other equities, a longer range). This is the whole point of
  building the engine, and now there's finally real data to run it against. Try both strategies,
  try a range of `TakeProfitPercent`/`StopLossPercent`/`MomentumThresholdPercent` values, and be
  skeptical of any result that looks too good (overfitting is the default outcome of a single
  backtest run, not the exception — see "Ideas" in `docs/ROADMAP.md`).
- [ ] Watch the strategy runner operate against real live data in **paper mode** for a real stretch
  of trading days before trusting it with anything else — real data has gaps/latency/noise mock
  data doesn't, and the ticker has only been observed outside market hours so far (real intraday
  tick volume/behavior is still unverified).
- [ ] **Fix the partial-fill gap before ever placing a real order** (see `docs/STATUS.md`'s
  "Confirmed gap, not yet fixed" note) — `LiveOrderReconciler` currently never records a fill for
  an order that partially fills and then stalls/gets cancelled; the quantity that did fill at the
  broker would silently never show up in `LivePosition`. Needs `LiveOrder`'s current
  1-to-(0 or 1) relationship with `LiveFill` to become 1-to-many.
- [ ] Only after the above: flip `LiveTrading:Enabled = true` and manually approve the smallest
  possible real order yourself (see `docs/CONFIGURATION.md`) to verify `PlaceOrder`/`GetMargins`/
  `CancelOrder`/`ModifyOrder`/`OnOrderUpdate` for real, before letting any strategy signal reach
  approval unattended.

## Manual steps required to actually "go live" (none of this is automatic today — candidates for automation later)

Authenticating with Kite and starting the app does **not**, by itself, make the system find F&O
contracts and start trading. Every one of these is a separate, currently-manual step:

1. **Kite login is a daily chore, not a one-time setup.** `Kite:ApiKey`/`ApiSecret` only let the
   app *talk* to Kite — the actual login flow (`GET /api/broker/kite/login-url` → browser
   login/2FA → `GET /api/broker/kite/callback`) still has to be completed to get a real access
   token. **Kite sessions expire at the end of each trading day** — this is a Kite/Zerodha
   limitation (the login requires a human clicking through 2FA), not something our code can
   script around directly. Automation idea: at minimum, a startup/health check that detects an
   expired session and alerts (WhatsApp/log) that re-login is needed, rather than silently failing
   the next sync/ticker/order call.
2. **Nothing auto-discovers NIFTY/BANKNIFTY contracts.** `POST /api/broker/kite/sync-instruments`
   must be called manually. Nothing re-runs it on a schedule, so when expiries roll over, contracts
   go stale until someone re-triggers it. Automation idea: a scheduled background job (e.g. daily,
   or on detecting the nearest synced expiry has passed) that calls this automatically.
3. **Nothing auto-starts real-time prices.** `POST /api/broker/kite/ticker/start` must be called
   manually. Without it, `StrategyRunnerService` has no fresh quote to evaluate and silently skips
   every instrument — no error, just no activity. Automation idea: auto-start the ticker on boot
   whenever a valid Kite session already exists, instead of requiring a manual call every restart.
4. **The strategy loop itself is off by default** (`Strategy:Enabled=false`) and needs
   `Strategy:AccountId` pointed at a real paper account, plus `ActiveStrategy`/
   `InstrumentKindFilter` chosen. Not really "automatable" further — this is an intentional,
   deliberate opt-in, not a gap.
5. **Real money needs its own separate switch, and even then there's a gate.**
   `LiveTrading:Enabled=true` only routes signals to Kite; `RequireManualApproval` (default `true`)
   still parks every signal for a human to approve (`POST /api/live-orders/{id}/approve`) before
   anything reaches Kite. This gate is intentional and should stay on until real backtest results
   justify removing it (see `docs/ROADMAP.md` item 7) — **not** a candidate for automation yet.

**Caveat that applies to all of the above**: none of the Kite-dependent code (login exchange,
instrument sync, ticker, `PlaceOrder`, `GetMargins`, cancel/modify) has ever run against a real
account — only verified to fail cleanly without credentials. The first real run through this whole
flow is also the first real test of all of it. Go slowly: small quantities, `RequireManualApproval`
left on, watch logs closely for several sessions before considering any further automation above.

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
- [ ] **Live real-time OHLCV bar aggregation for Kronos.** The backtest path now feeds Kronos real
  `HistoricalBar` OHLCV (done 2026-10-31), but the live strategy runner only has point-in-time quote
  snapshots, which it wraps as degenerate candles (`Candle.FromPrice`, O=H=L=C=LTP, V=0) — so live
  Kronos still gets degraded input. Needs a tick→OHLCV aggregator: accumulate the ticker's stream
  into time-bucketed bars (first=open, max=high, min=low, last=close, sum=volume) per instrument and
  feed those to the runner instead of the latest-quote snapshot. Only worth building once Kronos has
  proven an edge in backtest — don't build it speculatively.
- [ ] No frontend/dashboard — everything is curl/Swagger. Fine for development, not fine for daily
  operation if this ever runs unattended.
- [ ] **No alert when the Kite session goes stale.** Kite access tokens expire once per day with no
  refresh-token API — re-login via the browser (password + 2FA) is unavoidable and shouldn't be
  automated away (scripting it means storing your real Zerodha password + TOTP seed in this app,
  a much bigger blast radius than anything here today, and likely against Kite's ToS). What
  *should* be automated: detecting that the stored `BrokerSession`'s token has gone
  stale/rejected and sending a WhatsApp/log alert ("re-login needed") instead of every downstream
  Kite call (ticker, sync, order placement) just failing silently until someone notices. See
  `docs/ROADMAP.md` item 8(a) and `docs/TODO.md`'s "Manual steps required to actually go live"
  item 1 for context.
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
