# Configuration

All secrets live in `dotnet user-secrets` (or environment variables in a real deployment) —
**never** in `appsettings.json`. Non-secret tunables live in `appsettings.json` and are validated
at startup via `ValidateDataAnnotations().ValidateOnStart()` — an invalid value fails fast at boot
with a clear message instead of misbehaving at runtime.

## Required user-secrets (set from `src/MarketsEngine.Api`)

```bash
cd src/MarketsEngine.Api
dotnet user-secrets list   # shows keys only, not values
```

As of the last check, these keys are set (values are on this machine only, not in git):

| Key | Purpose | Notes |
|---|---|---|
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
  "TakeProfitPercent": 10.0,                // applies to EVERY open position regardless of filter/strategy
  "StopLossPercent": 5.0
}
```

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
  "ReconciliationIntervalSeconds": 15,
  "PendingApprovalExpiryMinutes": 30 // a stale pending approval auto-rejects rather than firing on an old price
}
```

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

**Before ever setting this to `true` against a real account: start with the smallest possible
order (e.g. 1 share of a cheap, liquid stock) and approve it yourself manually — do not let a
strategy's first-ever live approval be unattended.** See `docs/STATUS.md` for current status.

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
