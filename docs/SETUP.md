# Local Dev Setup

## Prerequisites

- .NET 10 SDK
- Docker/Podman (this machine uses **Podman** as the Docker Desktop backend — run
  `podman machine start` first if `docker-compose up` can't reach a daemon)
- `dotnet-ef` tool pinned to **10.0.4** (`dotnet-tools.json`) — the Npgsql EF Core provider
  depends on 10.0.4, not the newer 10.0.13 patch; mixing versions causes assembly conflicts across
  projects. Restore local tools with `dotnet tool restore`.

## 1. Database

```bash
docker-compose up -d        # Postgres 16, host port 5433 → container 5432, named volume
```

If the container shows `Exited` after a Mac sleep/restart (Podman VM doesn't survive sleep), just
`docker-compose up -d` again — no need to re-run migrations, the volume persists.

```bash
cd src/MarketsEngine.Api
dotnet ef database update   # applies all migrations
```

Connect with a GUI client (DBeaver etc.) using the same connection string as
`ConnectionStrings:Postgres` in user-secrets (host `localhost`, port `5433`).

## 2. Secrets

```bash
cd src/MarketsEngine.Api
dotnet user-secrets init   # one-time, already done in this repo
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5433;Database=...;Username=...;Password=..."
dotnet user-secrets set "Auth:ApiKey" "$(openssl rand -hex 32)"   # required — the app won't boot without this
```

See `docs/CONFIGURATION.md` for the full list of required keys, and for how to use the API key
(every request needs an `X-Api-Key` header; Swagger UI has an Authorize button for it).

## 3. Run the API

```bash
cd src/MarketsEngine.Api
dotnet run
```

In `Development`, `MockDataSeeder` seeds 4 mock NIFTY/BANKNIFTY option instruments + 4 mock
equities (RELIANCE, TCS, INFY, HDFCBANK), and `MockQuoteRefresherService` re-ticks their quotes
every 15s so the quote-staleness check doesn't block testing. Swagger UI is at the printed
`/swagger` URL.

If you see `Address already in use` on the configured port: `lsof -ti :<port> | xargs -r kill -9`
to clear a leftover background process, then re-run.

## 4. Kronos forecasting service (optional — only needed for `Strategy:ActiveStrategy = "Kronos"`)

The forecast service wrapper now lives **in this repo** at `kronos-service/` (version-controlled
with everything else). The Kronos **model** itself is a large third-party ML repo and stays an
external dependency — clone it separately (`../Kronos` from `shiyu-coder/Kronos`); the service finds
it via `KRONOS_REPO_PATH` (defaults to a sibling `../Kronos` checkout). It's a general OHLCV
candlestick-forecasting model, **not** a trading strategy by itself — the buy/no-buy decision logic
is MarketsEngine's own `KronosStrategy`.

```bash
# one-time: clone the model repo + its venv (see kronos-service/README.md for full detail)
git clone https://github.com/shiyu-coder/Kronos.git ../Kronos
cd ../Kronos && python -m venv .venv && .venv/bin/pip install -r requirements.txt fastapi uvicorn

# run the service from this repo, using the Kronos venv:
cd kronos-service
../../Kronos/.venv/bin/python -m uvicorn app:app --host 0.0.0.0 --port 8000
```

First run downloads model weights (`Kronos-small` + `Kronos-Tokenizer-base`, ~tens of MB) from
Hugging Face — needs network access.

Known gotchas:
- On Apple Silicon, the model **must** run on `device="cpu"` — MPS's
  `scaled_dot_product_attention` doesn't support dropout, which Kronos's attention layers use, and
  `KronosPredictor` auto-detecting `device="mps"` crashes.
- The service uses exactly the OHLCV + timestamps it's sent (no fabrication). Backtests feed it real
  `HistoricalBar` OHLCV; the live path currently feeds degenerate single-price candles until
  tick→bar aggregation exists (see `docs/TODO.md`).
- `pip install` inside a sandboxed shell may fail with DNS errors even though `curl`/`git` work —
  that's a sandbox quirk blocking Python's own network stack, not a real connectivity problem.

Once running, set `Strategy:KronosServiceUrl = "http://localhost:8000"` (e.g.
`Strategy__KronosServiceUrl` in `.env`) and `Strategy:ActiveStrategy = "Kronos"`.

## 5. Twilio WhatsApp alerts (optional — only needed for `PnLAlerts:Enabled = true`)

1. Create a free Twilio account → WhatsApp Sandbox.
2. From the number you want alerts on, send `join <sandbox-keyword>` to the sandbox number shown
   in the Twilio Console (one-time, required before the sandbox will deliver to you).
3. Set the 6 `Twilio:*` user-secrets (see `docs/CONFIGURATION.md`).
4. Business-initiated messages (i.e. anything sent outside a live 24h WhatsApp session you started)
   require a **Content Template**. Create one in the Twilio Console (Content Template Builder),
   note its SID, set `Twilio:ContentSid`. **Custom templates need Meta/WhatsApp approval before
   they'll send** — there is no way to check approval status via API on a Twilio trial account;
   check the Console UI directly.
5. Test with `POST /api/notifications/test` and `POST /api/notifications/pnl-check-once`.

## Kite Connect (not yet actually used with real credentials)

Placeholder `Kite:ApiKey`/`Kite:ApiSecret` are set so the app boots cleanly; no real Zerodha Kite
Connect subscription has been purchased yet (deliberately deferred — "build everything first, pay
for the subscription later"). When ready:

1. Create a Kite Connect developer app at https://developers.kite.trade (Connect plan, paid).
2. Set the real `Kite:ApiKey`/`Kite:ApiSecret`.
3. `GET /api/broker/kite/login-url`, complete the browser login flow, hit
   `GET /api/broker/kite/callback` with the resulting `request_token` to generate and persist a
   real access token.
4. `POST /api/broker/kite/sync-instruments` / `sync-equities`, then
   `POST /api/broker/kite/ticker/start` for live quotes.

No order-placement adapter exists in this integration and none should be added without a very
deliberate, separate decision.
