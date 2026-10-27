# MarketsEngine

A local-first **paper-trading** system for Indian index options (NIFTY/BANKNIFTY) and equities,
built on ASP.NET Core / .NET 10 + PostgreSQL, with an optional, explicitly-gated **real-money
execution path** via Kite Connect. Named generically (not "OptionsPaperTrader") because the
long-term goal was always to evolve it into a real-money trading system.

**Paper by default. Real money is a single config flag, off by default, with multiple safety
layers.** `LiveTrading:Enabled = false` (the default) means every order — manual, strategy, or
exit — is simulated against a paper account (default ₹50,000 starting capital), completely
untouched by the live-trading code added alongside it. Setting it to `true` routes fresh orders to
a real Kite order, but only after: the instrument is confirmed Kite-sourced (never a mock
symbol), hard quantity/notional risk caps, a real Kite margin check, and — by default — a human
explicitly approving each order before it ever reaches Kite. See `docs/CONFIGURATION.md` before
touching this flag.

See `docs/` for the parts that don't show up just by reading the code:

| Doc | What it covers |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Layers, projects, key components |
| [`docs/REQUEST_FLOWS.md`](docs/REQUEST_FLOWS.md) | Line-by-line trace of the core flows: manual order, strategy auto-buy, exits, live order approval, backtesting |
| [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) | Every `appsettings.json` section + required `dotnet user-secrets` |
| [`docs/SETUP.md`](docs/SETUP.md) | Getting a dev environment running (Postgres, migrations, Kronos, Twilio) |
| [`docs/STATUS.md`](docs/STATUS.md) | What's actually verified vs. built-but-untested vs. known gaps |

## Quick start

```bash
# 1. Postgres (Podman as the Docker backend — `podman machine start` first if needed)
docker-compose up -d

# 2. Required secrets (DB connection string + an API key for every request — see docs/CONFIGURATION.md)
cd src/MarketsEngine.Api
dotnet user-secrets set "Auth:ApiKey" "$(openssl rand -hex 32)"

# 3. Apply migrations
dotnet ef database update

# 4. Run the API (Development env seeds mock NIFTY/BANKNIFTY options + 4 equities)
dotnet run
```

Swagger UI comes up at the printed `http://localhost:XXXX/swagger` URL — use the **Authorize**
button to paste your API key once per session. See [`docs/SETUP.md`](docs/SETUP.md) for the full
first-time setup (user-secrets, Kronos service, Twilio sandbox).

## Solution layout

```
MarketsEngine.Api/            controllers, DI wiring, Swagger, mock data seeding
MarketsEngine.Domain/         entities, pure calculators, strategy abstraction (no external deps)
MarketsEngine.Infrastructure/ EF Core, order execution pipeline, Kite/Kronos/Twilio integrations
MarketsEngine.Tests/          xUnit tests (not actively being expanded right now — see docs/STATUS.md)
```

## Current capability, in one line each

- API authentication: every endpoint except `/health` requires an `X-Api-Key` header — ✅ verified.
- Paper trading engine: accounts, orders, fills, margin blocking, idempotency, cash ledger — ✅ verified.
- Automated strategy runner (`SimpleMomentum` or `Kronos`, switchable by config flag) that buys on
  signal and exits on take-profit/stop-loss — ✅ verified live against mock data.
- Kite Connect integration (real NIFTY/BANKNIFTY/equity data, read-only) — built, **not yet tested
  against a real Zerodha account** (placeholder credentials only).
- Real-money order execution via Kite (`LiveTrading:Enabled`, off by default, manual approval
  required) — full place/cancel/modify-quantity lifecycle, plus push+poll order-status
  reconciliation. The config switch, safety guards, strategy-runner routing, and cancel/modify/auth
  plumbing are **✅ verified live** (see `docs/STATUS.md`); the actual Kite `PlaceOrder`/
  `GetMargins`/`CancelOrder`/`ModifyOrder`/`OnOrderUpdate` calls are **not yet tested**, same
  real-credentials blocker as above.
- WhatsApp P&L alerts via Twilio — pipeline verified; custom alert template submission is blocked
  on a paid Twilio account, so this is **parked** until the core trading pipeline is fully real.
- **Backtesting** (`POST /api/backtests`): replays imported historical daily bars through
  `SimpleMomentum`/`Kronos` and the real exit logic — the question this project needed answered
  most. The replay engine is **✅ verified by hand-computed proof** (not just a smoke test — see
  `docs/STATUS.md`); importing real historical data from Kite (`POST
  /api/broker/kite/import-historical`) is **not yet tested**, same real-credentials blocker as
  everything else live.

Full detail in [`docs/STATUS.md`](docs/STATUS.md).
