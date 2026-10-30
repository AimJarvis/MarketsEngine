# Roadmap — Where We Are, and What Would Actually Help

This is the "state of the union" file: what's built, an honest read on whether it's working
toward the real objective, and ideas for what would move the needle fastest. `docs/STATUS.md` has
the detailed verified/unverified breakdown; `docs/TODO.md` has the concrete checklist. This file
is for judgment calls about *what to do next and why*.

## The actual objective, restated

Automate buying and selling NIFTY/BANKNIFTY options (and equities) profitably, using strategies
built on top of the Kronos forecasting model and simpler rule-based signals. Everything built so
far is infrastructure in service of that one question: **does any of this make money?** That
question is still open — nothing built changes that fact, it only gets you closer to being able
to answer it once real data flows.

## What's built, grouped by what it actually gets you

**A correct paper-trading engine.** Orders, fills, margin, cash ledger, idempotency, P&L — all
verified via extensive live testing. This is solid ground to build on, not a weak link.

**Two strategies, running correctly, with unproven edge.** `SimpleMomentum` (naive price
threshold) and `Kronos` (a general ML forecasting model, not tuned for Indian markets or
options specifically) both correctly produce buy signals and feed the same exit logic
(take-profit/stop-loss). "Correctly produce signals" and "produce signals worth following" are
different claims — only the first is proven.

**A real-money execution path, gated hard.** Manual approval by default, risk caps, a
mock-instrument guard, cancel/modify, push+poll reconciliation, API-key auth on everything. This
is defensive infrastructure — it protects you from mistakes, it doesn't manufacture an edge.

**A backtesting engine, proven correct by hand-computed test.** This is the most important piece
built recently, because it's the only thing that can actually answer the core question — once it
has real data to run against.

**Everything Kite-dependent is one decision away from being verified for real.** Real market
data, real historical data, real order placement — four separate "built but blocked" items that
all share the exact same blocker: a Kite Connect subscription and completed login.

## Ideas that would help reach the objective faster or more reliably

These are suggestions, not decisions already made — flagging them so they're considered, not
quietly acted on.

1. **Get the Kite subscription sooner rather than later.** It's the one action that unblocks four
   different "unverified" items simultaneously. Everything else on this list is secondary to this.

2. **Backtest equities before options, and expect to iterate many times, not once.** Free/easy
   historical data is far more available for equities, and a single backtest result — especially
   a good-looking one — should be treated with suspicion by default. A result that holds up across
   several different date ranges and market conditions (a bull run, a flat period, a crash) is
   much more trustworthy than one great-looking run. The backtest engine already supports
   per-request parameter overrides for exactly this kind of experimentation without touching live
   config.

3. **Don't assume `SimpleMomentum` or raw `Kronos` output has to be the final strategy.** Both
   were built to prove the signal→order pipeline works end to end, not because either is expected
   to be profitable as-is. Once real backtest results come back, the honest next step is probably
   iterating on the strategy itself — trying different lookback windows, combining momentum with
   a volatility or volume filter, or treating Kronos's raw forecast as one input among several
   rather than a standalone signal — not assuming the current two are the final word.

4. **Watch for overfitting discipline, not just a working backtest.** A simple, cheap habit that
   pays off a lot: always check *in-sample* results (the exact period you tuned parameters on)
   against a held-out *out-of-sample* period you didn't tune on. The engine doesn't enforce this
   for you — the next instinct after seeing a good backtest number should be "would this still
   look good on data it hasn't seen," not "ship it."

5. **A minimal read-only dashboard would meaningfully improve day-to-day usability.** Right now,
   checking pending live-order approvals, position state, or a backtest result means curl or
   Swagger. Even a very simple single-page view (pending approvals, current positions, last
   backtest result) would make this much easier to actually operate day to day — worth considering
   once the core data question is being actively worked, not before.

6. ~~**Add basic system-health alerting before running anything unattended.**~~ **Done
   (2026-10-28)**: `HealthMonitorService` now tracks a heartbeat from the three main background
   services and alerts (log + best-effort WhatsApp) on a stale one. Kronos-service-unreachable
   detection specifically isn't covered yet (that only surfaces as a failed HTTP call inside a
   `Kronos`-strategy pass, not a missed heartbeat) — worth adding if `Kronos` backtesting/live use
   picks up.

7. **Keep the real-money path behind manual approval until backtesting has actually produced
   something worth trusting.** The infrastructure for full automation (flip
   `LiveTrading:RequireManualApproval` off) already exists — the honest recommendation is not to
   use it until there's real evidence (from a properly out-of-sample-tested backtest, on real
   data) that there's something worth automating unattended.

8. **Automate the manual "go live" operational steps** — see `docs/TODO.md`'s "Manual steps
   required to actually go live" for the full list. Three of the five are genuine automation
   candidates, not deliberate safety gates: (a) detect an expired/missing Kite session and alert,
   instead of every sync/ticker/order call silently failing; (b) a scheduled job that re-syncs
   NIFTY/BANKNIFTY instruments automatically instead of relying on a manual
   `sync-instruments` call whenever expiries roll over; (c) auto-start the Kite ticker on boot
   when a valid session already exists, instead of a manual `ticker/start` call every restart.
   The other two (the strategy opt-in flag, and `LiveTrading:RequireManualApproval`) are
   intentional gates, not gaps — don't automate those away.
