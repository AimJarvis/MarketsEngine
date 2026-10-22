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

Everything defaults to **off** — nothing trades or alerts automatically out of the box.
