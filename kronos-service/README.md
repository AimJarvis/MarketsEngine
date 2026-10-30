# Kronos forecast service

A small FastAPI wrapper (`app.py`) that `KronosStrategy` (C#) calls over HTTP to get price
forecasts from the [Kronos](https://github.com/shiyu-coder/Kronos) foundation model. Only needed
when `Strategy:ActiveStrategy = "Kronos"` (or running a Kronos backtest).

This wrapper is version-controlled here; the Kronos model itself is a large third-party ML repo and
stays an **external dependency** — it is not vendored into this repo.

## One-time setup

```bash
# 1. Clone the Kronos model repo somewhere (a sibling of MarketsEngine is the default the service
#    looks for; otherwise set KRONOS_REPO_PATH, see below).
git clone https://github.com/shiyu-coder/Kronos.git ../Kronos

# 2. Create its venv and install its deps + the two this service needs.
cd ../Kronos
python -m venv .venv
.venv/bin/pip install -r requirements.txt fastapi uvicorn
```

First model run downloads weights (`Kronos-small` + `Kronos-Tokenizer-base`, ~tens of MB) from
Hugging Face — needs network access, cached afterwards.

## Run

From this `kronos-service/` directory, using the Kronos repo's venv so `torch` etc. resolve:

```bash
# KRONOS_REPO_PATH defaults to ../../Kronos relative to this file; set it if your clone is elsewhere.
../../Kronos/.venv/bin/python -m uvicorn app:app --host 0.0.0.0 --port 8000
```

Then point the API at it: set `Strategy__KronosServiceUrl=http://localhost:8000` in
`src/MarketsEngine.Api/.env` (or `Strategy:KronosServiceUrl` in config).

## Notes

- **CPU-only by design** on Apple Silicon — MPS crashes on Kronos's attention layers. Inference is
  therefore slow; one HTTP call per instrument per strategy pass does not scale to the full option
  chain (see `docs/STATUS.md`).
- The service uses exactly the OHLCV + timestamps it's sent — it never fabricates candles. Backtests
  feed it real `HistoricalBar` OHLCV; the live path currently feeds degenerate single-price candles
  until tick→bar aggregation exists (see `docs/TODO.md`).
