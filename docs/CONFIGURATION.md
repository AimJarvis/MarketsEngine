# Configuration

**Secrets live in `src/MarketsEngine.Api/.env`** (as of 2026-10-31) — a gitignored file, loaded via
`DotNetEnv.Configuration.AddDotNetEnv()` in `Program.cs`, using `Section__Key` to mean
`"Section:Key"` in config (same convention as real environment variables). A missing `.env` is a
safe no-op (the app falls back to `dotnet user-secrets`/environment variables, the older
mechanism, which still works but is no longer the documented default). Copy
`src/MarketsEngine.Api/.env.example` to `.env` in the same folder and fill in real values. Never
in `appsettings.json`. Non-secret tunables live in `appsettings.json` and are validated at startup
via `ValidateDataAnnotations().ValidateOnStart()` — an invalid value fails fast at boot with a
clear message instead of misbehaving at runtime.

Example `.env`:
```
Auth__ApiKey=<64 random hex chars - openssl rand -hex 32>
ConnectionStrings__Postgres=Host=localhost;Port=5433;Database=marketsengine;Username=marketsengine_dev;Password=<see root .env, used by docker-compose>
Kite__ApiKey=<from your Kite Connect app>
Kite__ApiSecret=<from your Kite Connect app>
```
Note: this is a **separate file** from the repo-root `.env` that `docker-compose.yml` reads for
the Postgres container's own `POSTGRES_DB`/`POSTGRES_USER`/`POSTGRES_PASSWORD` — don't merge them.

## API authentication — required on every request except `/health`

Every endpoint requires an `X-Api-Key` header matching `Auth:ApiKey` (required, validated at
startup — **the app will not boot without it configured**, same fail-fast behavior as the Postgres
connection string). There is no default key; a missing/wrong key always returns `401`.

```bash
cd src/MarketsEngine.Api
dotnet user-secrets set "Auth:ApiKey" "$(openssl rand -hex 32)"
```

- All `curl`/HTTP calls need `-H "X-Api-Key: <your key>"`.
- Swagger UI (`/swagger`) has an **Authorize** button (padlock icon) — paste the key once per
  browser session, then every "Try it out" call includes it automatically.
- To rotate: set a new value with the same command above and restart the app — the old key stops
  working immediately.
- `dotnet user-secrets list` shows this key in plaintext (it's self-issued, not a third-party
  credential, so that's expected and fine on a local dev machine).

## Required user-secrets (set from `src/MarketsEngine.Api`)

```bash
cd src/MarketsEngine.Api
dotnet user-secrets list   # shows keys only, not values
```

As of the last check, these keys are set (values are on this machine only, not in git):

| Key | Purpose | Notes |
|---|---|---|
| `Auth:ApiKey` | Required for every API request | See "API authentication" above — the app won't boot without this set |
| `ConnectionStrings:Postgres` | DB connection string | Points at the `docker-compose.yml` Postgres on host port 5433 |
| `Kite:ApiKey` / `Kite:ApiSecret` | Zerodha Kite Connect app credentials | **Currently placeholder values** — no real Kite subscription purchased yet. App runs fine without these (Kite features throw a clear `KiteNotConfiguredException`) |
| `Twilio:AccountSid` / `Twilio:AuthToken` | Twilio account credentials | From the Twilio Console |
| `Twilio:WhatsAppFromNumber` | Twilio WhatsApp sandbox number, `whatsapp:+1XXXXXXXXXX` format | The sandbox number, not your own |
| `Twilio:WhatsAppToNumber` | Your own WhatsApp number, `whatsapp:+91XXXXXXXXXX` format | Must have sent `join <sandbox-keyword>` to the sandbox number at least once |
| `Twilio:ContentSid` | Content Template SID for business-initiated alert messages | Custom template `marketsengine_alert` — **may still be pending Meta/WhatsApp approval**, see `docs/STATUS.md` |

Setting any of these: `dotnet user-secrets set "Section:Key" "value"` from `src/MarketsEngine.Api`.

## `appsettings.json` sections

### `Trading`
```json
"Trading": {
  "PaperTradingEnabled": true,       // global kill switch — false rejects all new order submissions
  "SlippageBasisPoints": 0,
  "FeePerLot": 20,
  "ShortOptionMarginPerLot": 50000,  // flat margin rule, NOT real SPAN/exposure margin
  "QuoteStalenessSeconds": 60
}
```

### `Kite`
```json
"Kite": {
  "EquityWatchlist": [ "RELIANCE", "TCS", "INFY", "HDFCBANK" ]
}
```
Controls which symbols `POST /api/broker/kite/sync-equities` pulls. NIFTY/BANKNIFTY options sync
(`sync-instruments`) is hardcoded to the nearest 2 expiries, not config-driven.

### `Strategy`
```json
"Strategy": {
  "Enabled": false,                        // automated buy/exit submission is opt-in
  "ActiveStrategy": "SimpleMomentum",       // "SimpleMomentum" | "Kronos" — THE strategy flag
  "InstrumentKindFilter": "Option",         // "Option" | "Equity" | "All" — the other flag
  "AccountId": null,                        // REQUIRED (a real paper account GUID) when Enabled=true
  "EvaluationIntervalSeconds": 30,
  "LookbackSamples": 5,
  "QuantityPerSignal": 1,
  "CooldownMinutes": 10,                    // also used as the post-exit re-entry cooldown
  "MomentumThresholdPercent": 1.0,
  "KronosServiceUrl": null,                 // e.g. http://localhost:8000 — required when ActiveStrategy=Kronos
  "KronosForecastHorizon": 12,
  "KronosDirectionThresholdPercent": 0.3,
  "KronosTakeProfitFactor": 1.0,            // Kronos only: scales its own forecast % into a take-profit for the position it opens
  "KronosStopLossFactor": 0.5,              // Kronos only: same, for stop-loss (lower factor = tighter leash relative to the forecast)
  "TakeProfitPercent": 10.0,                // the FALLBACK used when the entry signal didn't specify its own (see below)
  "StopLossPercent": 5.0
}
```

**Per-position stop-loss/take-profit, not just one global number.** `PositionExitEvaluator` now reads
each open position's *own* `StopLossPercent`/`TakeProfitPercent` (set from the `StrategySignal` that
opened it), falling back to the two settings above only when the signal didn't specify one:

- **`SimpleMomentum`** has no basis to size its own exit (its threshold is a direction signal, not a
  magnitude forecast) — it always leaves these null, so its positions always use the global default.
- **`Kronos`** does have a magnitude — its own predicted % move — so it sizes the position's
  take-profit/stop-loss off that forecast (`predictedChangePercent × KronosTakeProfitFactor` /
  `× KronosStopLossFactor`) instead of the global default. A forecast predicting a bigger move earns
  a wider take-profit and (scaled down) a wider stop; a forecast barely over
  `KronosDirectionThresholdPercent` gets a tight leash on both. Deliberately simple — a fixed scale
  of the forecast, not a real risk model.
- A manual order (`POST /api/orders`, `POST /api/live-orders`) always uses the global default — there
  is no API field for a human to set an override; only a strategy signal can.
- The override is fixed at the moment a position opens (or reopens after being fully flat, or flips
  direction) and does not change while averaging into an already-open position. `GET
  /api/accounts/{id}/positions` and `GET /api/live-orders/positions` both expose the resolved
  `stopLossPercent`/`takeProfitPercent` (`null` = using the global default) so you can see which one
  is actually governing a position.
- **There is no broker-side stop order anywhere in this system** (no Kite SL/SL-M order type, no
  trigger/limit price on any order) — every order, including exits, is a plain Kite `MARKET` order.
  "Stop-loss" here means "poll price every `EvaluationIntervalSeconds`, then market-sell if the
  threshold is breached," not a resting order that protects you between polls.

### `PnLAlerts`
```json
"PnLAlerts": {
  "Enabled": false,
  "CheckIntervalMinutes": 15,
  "AlertThresholdAmount": 500,   // 0 = alert on every check
  "AccountIds": []               // which accounts to watch
}
```

### `LiveTrading` — real money, read this before touching it

```json
"LiveTrading": {
  "Enabled": false,                  // THE real-money kill switch AND the paper/live mode switch
  "RequireManualApproval": true,     // false = strategy signals place real orders with no human step
  "DefaultProduct": "MIS",           // "MIS" | "NRML" — passed straight to Kite's PlaceOrder
  "MaxQuantityPerOrder": 1,          // in lots, same unit as Strategy:QuantityPerSignal
  "MaxNotionalPerOrder": 10000,      // ₹, rejected before Kite is ever contacted
  "MaxNotionalPerDay": 25000,        // ₹, summed across today's already-submitted live orders
  "MaxOrdersPerDay": 20,             // order COUNT cap, separate from the notional cap above
  "BlockedTradingSymbols": [],       // e.g. ["NIFTY24NOV24500CE"] — a manual deny-list, checked before every order
  "CashBufferPercent": 10.0,         // a buy may not leave less than this % of pre-trade available cash untouched
  "MaxPositionPercentOfEquity": 50.0,// a single instrument's position may not exceed this % of (cash + all open LivePosition value)
  "ReconciliationIntervalSeconds": 15,
  "PendingApprovalExpiryMinutes": 30 // a stale pending approval auto-rejects rather than firing on an old price
}
```

**`MaxOrdersPerDay`/`BlockedTradingSymbols`** are checked by the pure `LiveOrderRiskEvaluator` (same
place as `MaxQuantityPerOrder`/`MaxNotionalPerOrder`/`MaxNotionalPerDay`) — at submission, approval,
and modify time. **`CashBufferPercent`/`MaxPositionPercentOfEquity`** are checked once, right before
the real `kite.PlaceOrder` call in `PlaceWithKiteAsync`, because they need the real Kite cash balance
and current position values — not available at the earlier checkpoints.

**`LiveTrading:Enabled` is the single switch between paper and real money.** When `false` (the
default), `ITradingOrderGateway` routes every order through the existing, unchanged paper engine —
nothing about this feature can affect paper trading while it's off. When `true`, fresh entry/exit
signals route to `LiveOrderExecutionService` and can place real orders with Kite, subject to:

- The instrument must be Kite-sourced (`Instrument.DataSource == "Kite"`) — a mock instrument is
  always refused, so flipping this on against seeded mock data is safe by construction.
- `RequireManualApproval` (default `true`): nothing reaches Kite until you call
  `POST /api/live-orders/{id}/approve`. List pending ones with `GET /api/live-orders?status=PendingApproval`.
- The `MaxQuantityPerOrder`/`MaxNotionalPerOrder`/`MaxNotionalPerDay` caps, checked twice (at
  signal time and again right before the real Kite call).
- A real `kite.GetMargins()` check for sufficient cash before a buy is placed.

Once an order is `Submitted`/`Open` with Kite, `POST /api/live-orders/{id}/cancel` pulls it back
(not valid for a `PendingApproval` order — use `/reject` for that, which never touches Kite).
Status updates arrive two ways: instantly via Kite's `Ticker.OnOrderUpdate` push when
`KiteTickerService` is running (`POST /api/broker/kite/ticker/start`), and via
`LiveOrderReconciliationService`'s periodic poll (`ReconciliationIntervalSeconds`) as a guaranteed
fallback regardless of whether the ticker is running.

**Before ever setting this to `true` against a real account: start with the smallest possible
order (e.g. 1 share of a cheap, liquid stock) and approve it yourself manually — do not let a
strategy's first-ever live approval be unattended.** See `docs/STATUS.md` for current status.

## Backtesting — no `appsettings.json` section, everything is per-request

1. **Import historical data** (requires real Kite credentials + a completed login + the
   instrument already synced): `POST /api/broker/kite/import-historical` with
   `{ "instrumentId": "...", "from": "2023-01-01", "to": "2024-01-01" }` — pulls daily candles and
   stores them in `HistoricalBar`. Only needs to be done once per instrument/date range; re-running
   it just skips bars already imported.
2. **Run a backtest**: `POST /api/backtests` with `instrumentId`, `strategyName`
   (`"SimpleMomentum"` or `"Kronos"`), `from`/`to` (must be within imported data), and the strategy
   parameters (`lookbackSamples`, `quantityPerSignal`, `takeProfitPercent`, `stopLossPercent`,
   `feePerLot`, `startingCapital`, and `momentumThresholdPercent` if using `SimpleMomentum`) — these
   are **independent of live `Strategy:*`/`Trading:*` config**, so you can experiment with
   different values without touching anything that affects paper/live trading. The request's
   `takeProfitPercent`/`stopLossPercent` are the **fallback**, same as the live config — a `Kronos`
   backtest resolves each trade's actual take-profit/stop-loss from that trade's own entry
   forecast, same as it would live, and reports what was actually used on every trade/open-position
   in the response (`trades[].takeProfitPercent`/`stopLossPercent`,
   `openPositionAtEnd.takeProfitPercent`/`stopLossPercent`) — useful for seeing why a `Kronos`
   trade exited where it did when it doesn't match the request's flat default. Returns the full
   trade-by-trade result plus summary stats synchronously.

A `Kronos` backtest calls the real Kronos Python service once per bar (see `docs/SETUP.md`) — start
it first, and expect it to be much slower than a `SimpleMomentum` backtest (no external
dependency, effectively instant).

## Turning automation on (local testing)

1. Create or pick a `PaperAccount` (`POST /api/accounts`, note the returned id).
2. Set `Strategy:AccountId` to that id (via `appsettings.Development.json` or user-secrets —
   it's not sensitive, but it is environment-specific).
3. Set `Strategy:Enabled = true`.
4. Pick `ActiveStrategy` and `InstrumentKindFilter`.
5. If using Kronos, start the Kronos service first (see `docs/SETUP.md`) and set
   `Strategy:KronosServiceUrl`.
6. To also get WhatsApp alerts, set `PnLAlerts:Enabled = true` and add the account id to
   `PnLAlerts:AccountIds`.
7. Real money is a separate, additional opt-in on top of all of the above — see `LiveTrading`
   above. Leaving `LiveTrading:Enabled = false` (the default) means steps 1–6 only ever touch the
   paper engine, regardless of how real your Kite-sourced market data is.

Everything defaults to **off** — nothing trades or alerts automatically out of the box.
