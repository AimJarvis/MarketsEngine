# MarketsEngine

A local-first **paper-trading** system for Indian index options (NIFTY/BANKNIFTY) and equities,
built on ASP.NET Core / .NET 10 + PostgreSQL. Named generically (not "OptionsPaperTrader")
because the long-term goal is to evolve it into a real-money trading system — nothing here places
real orders today.

**No real money, no real orders, ever — unless explicitly re-enabled later.** All trading is
simulated against a paper account (default ₹50,000 starting capital). Kite Connect is wired up
strictly **read-only** (login, instrument master, quotes) with no order-placement adapter.

See `docs/` for the parts that don't show up just by reading the code:

| Doc | What it covers |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Layers, projects, key flows (order pipeline, strategy runner, exits) |
| [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) | Every `appsettings.json` section + required `dotnet user-secrets` |
| [`docs/SETUP.md`](docs/SETUP.md) | Getting a dev environment running (Postgres, migrations, Kronos, Twilio) |
| [`docs/STATUS.md`](docs/STATUS.md) | What's actually verified vs. built-but-untested vs. known gaps |

## Quick start

```bash
# 1. Postgres (Podman as the Docker backend — `podman machine start` first if needed)
docker-compose up -d

# 2. Apply migrations
cd src/MarketsEngine.Api
dotnet ef database update

# 3. Run the API (Development env seeds mock NIFTY/BANKNIFTY options + 4 equities)
dotnet run
```

Swagger UI comes up at the printed `http://localhost:XXXX/swagger` URL. See
[`docs/SETUP.md`](docs/SETUP.md) for the first-time setup (user-secrets, Kronos service,
Twilio sandbox).

## Solution layout

```
MarketsEngine.Api/            controllers, DI wiring, Swagger, mock data seeding
MarketsEngine.Domain/         entities, pure calculators, strategy abstraction (no external deps)
MarketsEngine.Infrastructure/ EF Core, order execution pipeline, Kite/Kronos/Twilio integrations
MarketsEngine.Tests/          xUnit tests (not actively being expanded right now — see docs/STATUS.md)
```

## Current capability, in one line each

- Paper trading engine: accounts, orders, fills, margin blocking, idempotency, cash ledger — ✅ verified.
- Automated strategy runner (`SimpleMomentum` or `Kronos`, switchable by config flag) that buys on
  signal and exits on take-profit/stop-loss — ✅ verified live against mock data.
- Kite Connect integration (real NIFTY/BANKNIFTY/equity data, read-only) — built, **not yet tested
  against a real Zerodha account** (placeholder credentials only).
- WhatsApp P&L alerts via Twilio — pipeline verified; custom alert template submission is blocked
  on a paid Twilio account, so this is **parked** until the core trading pipeline is fully real.

Full detail in [`docs/STATUS.md`](docs/STATUS.md).
