# Request Flows

A line-by-line trace of the core flows in this codebase, each following a request from the
HTTP boundary (or background loop) through every layer to the database and back. Read this
alongside `docs/ARCHITECTURE.md` (the map) — this is the guided tour through the territory.

File paths are relative to `src/`.

Every HTTP request below also passes through the `X-Api-Key` authentication gate first (see
`docs/ARCHITECTURE.md` → "API authentication") — omitted from each trace below since it's the same
check in front of everything, not specific to any one flow.

---

## Flow 1: Manual paper order — `POST /api/orders`

The foundational flow. Every other flow in this document eventually reduces to "construct a
request record, call an execution service" — understand this one deeply and the rest is mostly
"who calls it, and why."

```
POST /api/orders
{ "accountId": "...", "instrumentId": "...", "side": "Buy", "tradingStyle": "Intraday",
  "quantity": 1, "idempotencyKey": "..." }
```

### Step 1 — `MarketsEngine.Api/Controllers/OrdersController.cs`

`Submit(...)` does three cheap, HTTP-layer checks before anything touches the database:
- Parses `"Buy"`/`"Sell"` into the `OrderSide` enum — 400 if invalid.
- Same for `TradingStyle`.
- Checks `IdempotencyKey` isn't blank.

It then converts the HTTP shape (`SubmitOrderApiRequest`, in `Contracts/Contracts.cs`) into the
domain shape (`SubmitOrderRequest`, in `MarketsEngine.Domain/Trading/IOrderExecutionService.cs`)
and calls:

```csharp
var result = await orderExecutionService.SubmitOrderAsync(domainRequest, cancellationToken);
```

**Pattern to notice**: controllers across this entire codebase do zero business logic — no DB
queries, no price math. They translate HTTP ↔ domain shapes and delegate. All real logic lives in
`Infrastructure`/`Domain`.

### Step 2 — Idempotency check first, before existence checks

`OrderExecutionService.SubmitOrderAsync` (`MarketsEngine.Infrastructure/Services/OrderExecutionService.cs`)
queries `PaperOrders` by `IdempotencyKey` **before** checking whether the account or instrument
even exist. If a row with that key already exists, it short-circuits and returns the cached
result instead of re-processing.

This is why `StrategyRunnerService` can safely re-evaluate every 30 seconds without creating
duplicate orders — it builds idempotency keys like
`strategy:SimpleMomentum:{instrumentId}:{10-minute-bucket}`, so re-signaling within the same
bucket just replays the first result.

### Step 3 — Structural validation (not persisted if these fail)

In order: account exists → instrument exists → `Quantity > 0`. Failures here return immediately
**without writing anything to the database** — these are client mistakes, not tradeable events
worth an audit trail.

### Step 4 — Business-rule validation (these DO get persisted as `Rejected` orders)

From here on, every rejection goes through `RejectAsync`, which writes a `PaperOrder` row with
`Status = Rejected` before returning — so failed attempts still show up in order history. In order:

1. **Kill switch**: `TradingSettings.PaperTradingEnabled` — false blocks everything.
2. **Expiry check**: `instrument.ExpiryDate < today` — applies to mock and Kite instruments alike.
3. **Quote exists**: latest row in `MarketQuotes` for this instrument.
4. **Quote staleness**: `(now - quote.QuotedAtUtc).TotalSeconds > QuoteStalenessSeconds` (default
   60s) — exists so the engine never fills against a quote that's gone stale (relevant because
   `MockQuoteRefresherService` only re-ticks every 15s, and a real Kite feed could drop).

### Step 5 — The trading math: three pure, stateless calculators

This is the part most worth understanding deeply — genuinely reusable logic with zero
infrastructure dependencies, all in `MarketsEngine.Domain/Trading/`.

**`FillPriceCalculator.CalculateFillPrice`** — a buy "lifts the ask", a sell "hits the bid", with
slippage always working *against* you (buys fill higher, sells fill lower):
```csharp
var basePrice = side == OrderSide.Buy ? askPrice : bidPrice;
var slippage = basePrice * slippageBasisPoints / 10_000m;
return side == OrderSide.Buy ? basePrice + slippage : basePrice - slippage;
```
`SlippageBasisPoints = 0` in config today, so this is currently a no-op — the mechanism exists for
later.

**`PositionUpdater.ApplyFill`** — the core position math, and the one piece of logic reused
verbatim for live trading:
- Opening/adding in the same direction re-weights the **average entry price**:
  `((oldAvgPrice × oldUnits) + (fillPrice × newUnits)) / totalUnits`.
- Reducing/flipping realizes P&L on the closed portion:
  `(fillPrice - avgEntryPrice) × closingUnits` for a long, inverted for a short.
- A fill bigger than the current position (e.g. short 2, buy 5) closes the 2 and opens 3 new long
  lots at the fill price.

**`MarginCalculator`** — deliberately crude: blocks a flat, configurable amount
(`ShortOptionMarginPerLot`, default ₹50,000) per **net short lot**. It doesn't care which
instrument or its actual risk — explicitly flagged in its own doc comment as "must be replaced
before real SPAN margin is needed."

### Step 6 — Affordability check

```csharp
var cashAfterFees = cashAfterTrade - fees;
if (cashAfterFees < 0 || cashAfterFees < newReservedMargin)
    return Rejected("InsufficientFunds", ...);
```
Checked against `newReservedMargin`, not just `>= 0` — you can't spend money earmarked as margin
for an existing short position.

### Step 7 — The write: one `SaveChangesAsync`, all or nothing

If everything passed: builds in memory a `PaperOrder` (`Status = Filled`), a `PaperFill`
(price/fees/slippage), upserts the `PaperPosition`, updates `PaperAccount.CashBalance`/
`ReservedMargin`, and adds 2–4 `CashLedgerEntry` rows (trade debit/credit, fee, optionally margin
block/release) — **all added to the same `DbContext` and committed in a single
`SaveChangesAsync` call**. EF Core wraps one `SaveChangesAsync` in an implicit transaction, so a
failure partway through rolls back everything — no explicit `BeginTransaction()` needed.

A `DbUpdateException` here (e.g. a race where two requests use the same idempotency key
simultaneously, hitting the unique index) is caught and converted into a clean
`DuplicateIdempotencyKey` rejection rather than a 500 error.

---

## Flow 2: Strategy auto-buy — background loop → `ITradingOrderGateway`

`StrategyRunnerService` (`MarketsEngine.Infrastructure/Strategies/StrategyRunnerService.cs`) is a
`BackgroundService` that loops every `Strategy:EvaluationIntervalSeconds` (default 30s) while
`Strategy:Enabled = true`. Each pass: run exit checks first (Flow 3), then entry checks below.

### Step 1 — Which instruments are even eligible

```csharp
var instrumentsQuery = dbContext.Instruments.Where(i => i.ExpiryDate == null || i.ExpiryDate >= today);
instrumentsQuery = settings.InstrumentKindFilter switch {
    "Option" => instrumentsQuery.Where(i => i.Kind == InstrumentKind.Option),
    "Equity" => instrumentsQuery.Where(i => i.Kind == InstrumentKind.Equity),
    _ => instrumentsQuery
};
```
`InstrumentKindFilter` ("Option"/"Equity"/"All") is the config knob controlling this.

### Step 2 — Cooldown check (skip instruments recently exited)

Before anything else per instrument, it checks an in-memory `Dictionary<Guid, DateTime>`
(`_lastExitAtUtc`) — if this instrument was exited within `Strategy:CooldownMinutes`, skip it
entirely for entry this pass. This exists because of a real bug found via live testing: a
take-profit exit and a momentum signal reacting to the *same* price spike in the same pass caused
an immediate, worse-priced re-buy right after a profitable exit.

### Step 3 — Build the rolling price window

Maintains a `Dictionary<Guid, Queue<PricePoint>>` per instrument, bounded to
`Strategy:LookbackSamples` (default 5) — **in-memory only, resets on app restart**. Each pass
appends the latest quote (if its timestamp is new) and trims the queue.

### Step 4 — Ask the strategy

```csharp
var context = new StrategyContext(instrument.Id, instrument.TradingSymbol, windowSnapshot);
var signal = await strategy.EvaluateAsync(context, cancellationToken);
```
`strategy` is resolved via **keyed DI**: `GetRequiredKeyedService<IStrategy>(settings.ActiveStrategy)`
— `"SimpleMomentum"` or `"Kronos"`, registered in `DependencyInjection.cs`. Both implementations
are pure/stateless and only ever return `ShouldBuy` — never a sell signal; all exits are handled
uniformly by `PositionExitEvaluator` instead (Flow 3).

- **`SimpleMomentumStrategy`** (Domain): buy if the price window moved up more than
  `Strategy:MomentumThresholdPercent`.
- **`KronosStrategy`** (Infrastructure): POSTs the price window to an external Kronos HTTP service
  (`Strategy:KronosServiceUrl`), buys if the forecast direction exceeds
  `Strategy:KronosDirectionThresholdPercent`.

### Step 5 — The gateway decides paper vs. live

```csharp
var request = new GatewayOrderRequest(
    settings.AccountId.Value, instrument.Id, OrderSide.Buy, TradingStyle.Intraday,
    settings.QuantityPerSignal, idempotencyKey, $"Strategy:{settings.ActiveStrategy}");

var result = await tradingOrderGateway.SubmitAsync(request, cancellationToken);
```

This is the one line in this entire codebase that doesn't know whether it just placed a simulated
or a real order. `TradingOrderGateway` (`MarketsEngine.Infrastructure/Services/TradingOrderGateway.cs`)
reads `LiveTrading:Enabled` (via `IOptionsMonitor`, so a config edit takes effect on the very next
pass) and routes to `IOrderExecutionService` (Flow 1, paper, unchanged) if false, or
`ILiveOrderExecutionService` (Flow 4) if true. The idempotency key, side, quantity, and a `Source`
tag (`"Strategy:SimpleMomentum"`, visible later in `LiveOrder.Source` for audit) flow through
either way.

---

## Flow 3: Exit check — take-profit / stop-loss

Runs **before** entry checks every pass, for **every open position regardless of
`InstrumentKindFilter` or `ActiveStrategy`** — risk management must never silently stop just
because the entry filter changed. Split into two methods that are structurally identical except
for which table/service they target — this split is itself an important design decision (next
section explains why).

### `CheckPaperExitsAsync` — closes `PaperPosition` rows

For each `PaperPosition` with `NetQuantity != 0`:
```csharp
var exitReason = PositionExitEvaluator.Evaluate(
    position.NetQuantity, position.AverageEntryPrice, latestQuote.LastTradedPrice,
    position.Instrument!.LotSize, settings.TakeProfitPercent, settings.StopLossPercent);
```
`PositionExitEvaluator` (`MarketsEngine.Domain/Trading/PositionExitEvaluator.cs`) is pure: computes
% return on cost basis (`unrealizedPnL / costBasis × 100`) and compares against
`TakeProfitPercent`/`-StopLossPercent`. Works identically for long and short because it goes
through `PnLCalculator` rather than raw price comparison.

If triggered, it submits the **closing** order (opposite side, full quantity) directly through
`IOrderExecutionService.SubmitOrderAsync` — Flow 1, exactly.

### `CheckLiveExitsAsync` — closes `LivePosition` rows

Identical logic, but queries `LivePositions` and submits through
`ILiveOrderExecutionService.SubmitOrderAsync` directly (Flow 4) — which means a live exit also
gets parked `PendingApproval` and needs manual approval, same as a live entry.

### Why exits never go through `ITradingOrderGateway`

This is a subtle but important design decision: **an existing position must always close in the
same domain it was opened in, regardless of the current `LiveTrading:Enabled` value.** If exits
went through the mode-switching gateway instead, flipping `LiveTrading:Enabled` on while an old
paper position was still open would route that paper position's exit into the *live* order path —
attempting a real Kite order for a position that only ever existed in simulation. Only **entries**
(which have no existing position yet, so there's nothing to get confused about) go through the
gateway.

---

## Flow 4: Live order — manual approval → real Kite order → reconciliation

The real-money path, fully parallel to Flow 1, living in `MarketsEngine.Domain/LiveTrading/` and
`MarketsEngine.Infrastructure/Services/LiveOrderExecutionService.cs`. Can be entered three ways:
a strategy entry signal (Flow 2, when `LiveTrading:Enabled = true`), an exit signal (Flow 3), or a
manual `POST /api/live-orders`. All three construct a `SubmitLiveOrderRequest` and call
`LiveOrderExecutionService.SubmitOrderAsync` (directly, or via the gateway for fresh entries).

### Step 1 — `SubmitOrderAsync`: validate and park, never touch Kite yet

Same idempotency-first pattern as Flow 1 (`LiveOrders` table instead of `PaperOrders`), then:

1. `LiveTrading:Enabled` kill switch.
2. `Quantity > 0`.
3. Instrument exists.
4. **`instrument.DataSource == "Kite"`** — the one guard with no paper equivalent. A mock
   instrument's trading symbol is fictitious; this refuses outright
   (`NonKiteInstrument`) rather than ever sending a made-up symbol to a real exchange.
5. Expiry check, quote exists, quote staleness — identical to Flow 1.
6. `LiveOrderRiskEvaluator.Evaluate(...)` (`MarketsEngine.Domain/LiveTrading/LiveOrderRiskEvaluator.cs`,
   pure) checks, in order: `MaxQuantityPerOrder`, `MaxNotionalPerOrder` (estimated price × lot size
   × quantity), and `MaxNotionalPerDay` (summed from today's already-submitted `LiveOrder`s,
   computed in `GetNotionalTradedTodayAsync`).

If everything passes, it writes a `LiveOrder` with:
```csharp
Status = LiveSettings.RequireManualApproval ? LiveOrderStatus.PendingApproval : LiveOrderStatus.Approved
```
**If `PendingApproval`, the method returns immediately — no Kite API call has happened.** This is
the core safety property of the whole feature: a strategy can signal all it wants, but nothing
reaches the real market until a human acts.

### Step 2 — `POST /api/live-orders/{id}/approve` → `ApproveAsync`

(`MarketsEngine.Api/Controllers/LiveOrdersController.cs` → `LiveOrderExecutionService.ApproveAsync`)

Re-runs almost everything from Step 1 — kill switch, fresh quote lookup, risk evaluation again —
because config or the day's traded notional may have changed since the order was parked. This
"check twice" pattern (once at signal time, once at approval time) is deliberate defense in depth
for anything that moves real money.

If it still passes: `Status = Approved`, then straight into `PlaceWithKiteAsync`.

### Step 3 — `PlaceWithKiteAsync`: the actual Kite call

```csharp
var kite = await kiteClientFactory.CreateAuthenticatedClientAsync(cancellationToken);
var margins = kite.GetMargins(Constants.Margin.Equity);
// ... reject with InsufficientMargin if margins.Available.Cash < estimated order value ...

var response = kite.PlaceOrder(
    Exchange: instrument.Kind == InstrumentKind.Equity ? Constants.Exchange.NSE : Constants.Exchange.NFO,
    TradingSymbol: instrument.TradingSymbol,
    TransactionType: order.Side == OrderSide.Buy ? Constants.Transaction.Buy : Constants.Transaction.Sell,
    Quantity: order.Quantity * instrument.LotSize,   // lots → actual share/contract units
    Product: LiveSettings.DefaultProduct,             // "MIS" or "NRML"
    OrderType: Constants.OrderType.Market,             // market orders only, v1
    Variety: Constants.Variety.Regular);
```

Note `Quantity` here is `order.Quantity * instrument.LotSize` — `LiveOrder.Quantity` is in **lots**
(same unit as everywhere else in this codebase), but Kite's API wants actual units.

`kite.PlaceOrder` returns synchronously with a real `order_id` as soon as Kite's system accepts
it — **this does not mean the order has filled**, just that it's been placed. Any `KiteException`
(margin shortfall, invalid order, etc.) is caught and turns into `Status = Failed` with the
exception message stored as `RejectionReason` — never an unhandled throw.

On success: `Status = Submitted`, `BrokerOrderId = response.OrderId`.

### Step 4 — finding out what actually happened: poll + push, both idempotent

Kite's `PlaceOrder` returns an order id immediately but the order itself fills asynchronously —
two independent paths apply the same update, sharing one piece of logic so they can never drift
apart: `LiveOrderReconciler.ApplyUpdateAsync` (`MarketsEngine.Infrastructure/Brokers/LiveOrderReconciler.cs`).

```csharp
public static async Task ApplyUpdateAsync(
    MarketsEngineDbContext dbContext, LiveOrder order, Order brokerOrder, DateTime now, ILogger logger, ...)
{
    var newStatus = MapStatus(brokerOrder.Status);          // COMPLETE→Complete, CANCELLED→Cancelled, REJECTED→Failed, else→Open
    if (newStatus == order.Status) return;                  // no-op if nothing changed — this is what makes poll+push safe together

    order.Status = newStatus;
    if (newStatus == LiveOrderStatus.Complete && order.Fill is null)
        await RecordFillAsync(dbContext, order, brokerOrder, now, cancellationToken);  // writes LiveFill, updates LivePosition via PositionUpdater.ApplyFill — same math as Flow 1
    ...
}
```

**Path A — `LiveOrderReconciliationService` (poll, the guaranteed fallback)**: a
`BackgroundService` polling every `LiveTrading:ReconciliationIntervalSeconds`, only while
`LiveTrading:Enabled`. Each pass: (1) expires any `PendingApproval` order older than
`LiveTrading:PendingApprovalExpiryMinutes`, so an approval can never fire against a long-stale
price; (2) loads every `LiveOrder` in `Submitted`/`Open`/`PartiallyFilled` status, calls
`kite.GetOrders()` **once** (not per-order), matches by `BrokerOrderId`, and calls
`ApplyUpdateAsync` for each. This runs regardless of whether the ticker is connected — it's the
path that guarantees an order status is never permanently stuck.

**Path B — `KiteTickerService.OnOrderUpdate` (push, low latency)**: the same WebSocket connection
already used for market-data ticks (`Flow` not covered here, but see `KiteTickerService.OnTick`)
also delivers order-update messages from Kite automatically — `Ticker.OnOrderUpdate` is wired
alongside `OnTick` in `StartAsync`, before `Connect()`. On each push, it opens its own DB scope
(same `IServiceScopeFactory` pattern `OnTick`'s handler already used), looks up the `LiveOrder` by
`BrokerOrderId`, and calls the identical `ApplyUpdateAsync`. **Only active while the ticker is
running** (`POST /api/broker/kite/ticker/start` — it's manual, not automatic), which is exactly
why Path A must keep running independently as the fallback, not be replaced by this.

Because both paths funnel through the same idempotent `ApplyUpdateAsync`, running them
simultaneously is safe by construction — whichever one observes a status change first "wins" and
the other becomes a no-op on the next check.

### Cancelling an in-flight order

`POST /api/live-orders/{id}/cancel` → `LiveOrderExecutionService.CancelAsync`: valid only for
`Submitted`/`Open`/`PartiallyFilled` orders (there's nothing to cancel *with the broker* for a
`PendingApproval` order — it never reached Kite — use `/reject` for that instead, which never
calls Kite at all). Calls `kite.CancelOrder(OrderId: order.BrokerOrderId, Variety: Regular)`; on
success sets `Status = Cancelled` immediately rather than waiting for reconciliation, since Kite
accepting the cancel request is itself sufficient confirmation — the poll/push paths above will
simply see the same `Cancelled` status later and no-op.

### Modifying an in-flight order's quantity

`POST /api/live-orders/{id}/modify` → `LiveOrderExecutionService.ModifyAsync`: same state gate as
cancel (`Submitted`/`Open`/`PartiallyFilled`). Unlike approve/cancel, this re-runs
`LiveOrderRiskEvaluator` against the **new** quantity (not the original) before calling
`kite.ModifyOrder(OrderId: ..., Quantity: newQuantity * lotSize, Variety: Regular)` — the original
approval only validated the original quantity, so a larger modified quantity must clear the risk
caps fresh. **Quantity only** — no price or order-type field is exposed, since every order here is
a market order (no price to modify) and widening to other order types is outside v1 scope.

**What this flow has never actually done**: every step through `kite.GetMargins()`,
`kite.PlaceOrder()`, `kite.GetOrders()`, `kite.CancelOrder()`, and `kite.ModifyOrder()` requires
real Kite credentials and a completed login, neither of which exist yet (see `docs/STATUS.md`).
Everything *before* those calls — the kill switch, the mock-instrument guard, the risk caps, the
gateway routing, and (as of 2026-10-28) the API-key authentication gate in front of all of it —
has been verified live.

---

## Flow 5: Backtest — `POST /api/broker/kite/import-historical` then `POST /api/backtests`

Entirely separate from every flow above — never places, approves, or touches any order. Answers
"would this strategy have made money?" by replaying historical bars through the real strategy and
exit logic.

### Step 1 — Import historical data (`KiteHistoricalDataService.ImportHistoricalDataAsync`)

Loads the `Instrument`, requires `KiteInstrumentToken` already set (sync the instrument from Kite
first), then calls `kite.GetHistoricalData(InstrumentToken: token.ToString(), FromDate, ToDate,
Interval: Constants.Interval.Day)`. Upserts each returned `Historical` candle into `HistoricalBar`,
keyed by `(InstrumentId, TimestampUtc, Interval)` — safe to re-run, already-imported bars are
skipped.

### Step 2 — `BacktestRunnerService.RunAsync`: load bars, resolve the strategy

Queries `HistoricalBar` for the requested instrument/date range (rejects with a clear error if
none are found — never silently runs on no data). Resolves the strategy by name: `SimpleMomentum`
is **constructed fresh** with the request's own `MomentumThresholdPercent` (so you can experiment
with different values without touching live `Strategy:*` config at all); `Kronos` is resolved via
the **existing keyed DI registration** (`GetRequiredKeyedService<IStrategy>("Kronos")`), meaning
its forecast parameters follow whatever `Strategy:Kronos*` is currently configured in
`appsettings.json` — a noted asymmetry versus `SimpleMomentum`'s full per-request overridability.

### Step 3 — `BacktestEngine.RunAsync`: the pure replay loop

(`MarketsEngine.Domain/Backtesting/BacktestEngine.cs` — takes bars and an `IStrategy` as plain
arguments, no DB/HTTP dependency of its own, directly unit-testable)

Per bar, in order:
1. Maintains the same rolling `PricePoint` window (bounded by `LookbackSamples`) that
   `StrategyRunnerService` keeps live.
2. **Exit check** (only if a position is open) — `PositionExitEvaluator.Evaluate(...)` against
   `bar.Close`. On trigger: closing fill simulated **at `bar.Close`** (no bid/ask spread exists in
   historical OHLC data, so no slippage is modeled — an explicit, documented simplification that
   makes backtest results somewhat optimistic versus live) via the same `PositionUpdater.ApplyFill`
   from Flow 1, minus a configurable fee. A `BacktestTrade` is recorded, closing out the position's
   flat→open→flat cycle.
3. **Entry check** — skipped entirely if an exit just fired on this exact bar (the simplest
   possible bar-level translation of the live `CooldownMinutes` fix for the same-pass re-entry
   race found earlier in this project — verified again here, see below). Otherwise,
   `await strategy.EvaluateAsync(context)`; on a buy signal, a fill is simulated at `bar.Close` via
   the same `PositionUpdater.ApplyFill`, averaging into any existing position exactly like live.
4. Tracks running equity (`StartingCapital + realized − fees + unrealized`) each bar for a
   max-drawdown figure; never force-closes a position still open at the end of the range — it's
   reported honestly as `OpenPositionAtEnd` with its unrealized P&L instead.

**Verified by hand-computed proof** (see `docs/STATUS.md` for the full numbers): a synthetic
8-day price path was seeded directly into `HistoricalBars` and run through `SimpleMomentum`. Every
figure in the response — 2 trades, each exit reason, each trade's fee-net `RealizedPnL`, the
summary totals — matched a hand calculation exactly, including confirming the same-bar re-entry
guard actually fires (the trade count was exactly 2, not 4, even though the momentum conditions
immediately following each exit would otherwise have signaled a fresh buy on that same bar).

**What this flow has never actually done**: the real `kite.GetHistoricalData()` call in Step 1 —
same real-credentials blocker as every other Kite-backed flow in this app. The replay engine
itself (Steps 2–3) has no such dependency and is fully verified independent of it.
