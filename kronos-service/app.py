"""
Minimal FastAPI wrapper around the Kronos foundation model (https://github.com/shiyu-coder/Kronos)
for MarketsEngine's KronosStrategy to call over HTTP.

Kronos itself only forecasts future OHLCV candles - it has no concept of options, direction, or
risk. This service's one job is: take a short series of real OHLCV candles, run Kronos's
predictor, and return the forecasted close prices. Everything about "is this forecast worth
trading on" is the C# KronosStrategy's job, not this service's.

The caller (KronosStrategy) sends real open/high/low/close/volume arrays plus real bar timestamps.
In backtest these are genuine HistoricalBar candles; on the live quote-snapshot path they are
currently degenerate (open=high=low=close=last-traded-price, volume=0) until real-time bar
aggregation exists. Either way this service uses exactly what it's given and never fabricates OHLC
from a single price, so forecast quality tracks input quality honestly.

This wrapper is OUR code (version-controlled in the MarketsEngine repo). The Kronos *model* itself
is a large third-party ML repo that stays an external dependency - clone it separately
(https://github.com/shiyu-coder/Kronos), set up its venv, and point this service at it via the
KRONOS_REPO_PATH env var (defaults to a sibling ../Kronos checkout next to the MarketsEngine repo).

Run (from this kronos-service/ directory, using the Kronos repo's own venv so torch etc. resolve):
    KRONOS_REPO_PATH=/path/to/Kronos \
    /path/to/Kronos/.venv/bin/python -m uvicorn app:app --host 0.0.0.0 --port 8000

See docs/SETUP.md for the full first-time setup.
"""

import sys
import os
from datetime import datetime, timedelta

import pandas as pd
from fastapi import FastAPI
from pydantic import BaseModel

# The Kronos model package lives in the separately-cloned Kronos repo, not here. Default to a
# sibling ../Kronos checkout (this file is at <learning>/MarketsEngine/kronos-service/app.py, so
# two levels up then into Kronos), override with KRONOS_REPO_PATH.
_default_kronos_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Kronos"))
_kronos_repo_path = os.environ.get("KRONOS_REPO_PATH", _default_kronos_path)
if not os.path.isdir(os.path.join(_kronos_repo_path, "model")):
    raise RuntimeError(
        f"Kronos model package not found at '{_kronos_repo_path}'. Clone "
        "https://github.com/shiyu-coder/Kronos and set KRONOS_REPO_PATH to point at it."
    )
sys.path.append(_kronos_repo_path)
from model import Kronos, KronosTokenizer, KronosPredictor  # noqa: E402

app = FastAPI(title="Kronos Forecast Service")

_tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")
_model = Kronos.from_pretrained("NeoQuasar/Kronos-small")
# Forced to CPU: Apple's MPS backend doesn't support scaled_dot_product_attention with
# dropout, which Kronos's attention layers use, so MPS auto-detection crashes on Apple Silicon.
_predictor = KronosPredictor(_model, _tokenizer, device="cpu", max_context=512)


class ForecastRequest(BaseModel):
    open: list[float]
    high: list[float]
    low: list[float]
    close: list[float]
    volume: list[float]
    timestamps: list[int]  # unix seconds, UTC, one per input bar
    horizon: int


class ForecastResponse(BaseModel):
    forecastPrices: list[float]


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/forecast", response_model=ForecastResponse)
def forecast(request: ForecastRequest):
    n = len(request.close)
    if n < 2:
        return ForecastResponse(forecastPrices=[])

    df = pd.DataFrame({
        "open": request.open,
        "high": request.high,
        "low": request.low,
        "close": request.close,
        "volume": request.volume,
        # Kronos expects an 'amount' (turnover) column; approximate as close*volume per bar. Zero
        # when volume is zero (the live degenerate-candle case), same as before.
        "amount": [c * v for c, v in zip(request.close, request.volume)],
    })

    # Use the real bar timestamps the caller sent, so Kronos's time features reflect the true
    # cadence. Infer the forward (future) bar spacing from the median gap between real bars rather
    # than hardcoding minutes, so daily input produces daily-spaced forecast timestamps.
    x_dt = [datetime.utcfromtimestamp(t) for t in request.timestamps]
    x_timestamp = pd.Series(x_dt)

    if n >= 2:
        deltas = [(x_dt[i] - x_dt[i - 1]).total_seconds() for i in range(1, n)]
        deltas.sort()
        step_seconds = deltas[len(deltas) // 2]  # median gap
    else:
        step_seconds = 86400.0
    step = timedelta(seconds=step_seconds if step_seconds > 0 else 86400.0)

    last_dt = x_dt[-1]
    y_timestamp = pd.Series([last_dt + step * (i + 1) for i in range(request.horizon)])

    pred_df = _predictor.predict(
        df=df,
        x_timestamp=x_timestamp,
        y_timestamp=y_timestamp,
        pred_len=request.horizon,
        T=1.0,
        top_p=0.9,
        sample_count=1,
        verbose=False,
    )

    return ForecastResponse(forecastPrices=pred_df["close"].tolist())
