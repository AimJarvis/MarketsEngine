# How the Stock Market Works — and What Our APIs Actually Mean

This file explains the market concepts from first principles, then ties every concept directly to
the real Kite Connect API fields and our own code (`PlaceOrder`, `GetMargins`, `GetPositions`,
`GetHistoricalData`, `Instrument`, `Order`, `Tick`) so the two stay connected — not two separate
things to learn. Read top to bottom the first time; use it as a reference after that.

---

## Part 1 — What a stock exchange actually is

An exchange (NSE — National Stock Exchange, BSE — Bombay Stock Exchange, in India) is a matching
engine. Buyers and sellers each submit orders saying "I want to buy/sell X quantity at Y price."
The exchange matches a buyer and a seller whose prices agree, and a **trade** happens. Nothing
more mystical than that — "the market" is just millions of these individual matches happening
continuously during trading hours (NSE equities/derivatives: 9:15 AM – 3:30 PM IST, Monday–Friday).

**Order book**: at any moment, every unmatched buy order (a "bid") and sell order (an "ask"/"offer")
for an instrument sits in a queue, sorted by price (best price first). The **bid price** is the
highest price someone is currently willing to pay; the **ask price** is the lowest price someone
is currently willing to sell at. The **spread** is the gap between them — a tight spread (₹0.05)
means a liquid, actively-traded instrument; a wide spread (₹5+) means a thin, illiquid one where
your order will move the price against you. The **last traded price (LTP)** is simply the price of
the most recent actual trade — not necessarily the current bid or ask.

**Equities vs. derivatives**: an equity (a "stock," e.g. RELIANCE) represents actual partial
ownership of a company. A **derivative** (an option, a future) is a contract *about* an underlying
asset's price — you never own the company, you own a bet on where its price goes. Index options
(NIFTY, BANKNIFTY) are derivatives on an *index* (a basket of stocks), not any single company — you
can't "own" NIFTY, you can only trade contracts referencing its level.

---

## Part 2 — Options, from scratch

An **option** is a contract giving you the *right* (not obligation) to buy or sell something at a
fixed price, by a fixed date.

- **Call option**: the right to *buy* the underlying at the **strike price**. You buy a call when
  you expect the price to go *up* — if NIFTY is at 24,000 and you buy a 24,500 call, you're betting
  NIFTY rises above 24,500 before expiry.
- **Put option**: the right to *sell* the underlying at the strike price. You buy a put when you
  expect the price to go *down*.
- **Strike price**: the fixed price written into the contract (e.g. 24,500).
- **Premium**: the price you pay to buy the option — this is the only thing at risk if you're a
  *buyer*. If NIFTY never reaches 24,500, your call simply expires worthless and you lose exactly
  the premium you paid — nothing more. This is "defined risk," the preference stated throughout
  this project.
- **Expiry**: the date the contract stops existing. NIFTY/BANKNIFTY options are **European style**
  — they can only be exercised/settled *at* expiry, never early (unlike US stock options, which can
  be exercised any time). They're also **cash-settled**: nobody ever actually buys/sells the
  underlying shares; at expiry, the difference in cash is simply credited/debited.
- **Lot size**: options trade in fixed-size bundles ("lots"), not single units — you can't buy
  "1 NIFTY option," you buy 1 lot, which might represent 75 units of the index (lot sizes change
  periodically per exchange rules). This is exactly `Instrument.LotSize` in our code — every
  quantity field in this app is "number of lots," and the actual exchange quantity is always
  `lots × lotSize`.

**ITM / ATM / OTM** (moneyness — where the strike sits relative to the current price):
- **In the money (ITM)**: exercising right now would be profitable (a call with strike below the
  current price; a put with strike above it).
- **At the money (ATM)**: strike ≈ current price.
- **Out of the money (OTM)**: exercising right now would be worthless (a call with strike above the
  current price; a put with strike below it). OTM options are cheaper (lower premium) because
  they need a bigger price move to pay off — and most OTM options expire worthless.

**Intrinsic value vs. time value**: an option's premium = intrinsic value (how far ITM it is, if at
all — zero for OTM) + time value (what the market will pay for the *chance* it moves ITM before
expiry). Time value erodes every single day as expiry approaches — this is **theta decay**, the
single biggest reason option *buyers* lose money more often than they win, even when they're
directionally right but too early or too slow. It's also why `SimpleMomentumStrategy`'s doc
comment flags that its momentum measurement is on the "option's own premium, which drifts with
IV/theta, not just direction" — a real, structural complication specific to trading options
(as opposed to equities) that a naive price-momentum signal doesn't account for.

---

## Part 3 — How an order actually becomes a trade

**Transaction type**: `BUY` or `SELL` — which side of the trade you're on.

**Order type** — how you want the price determined:
- `MARKET`: "fill me immediately at whatever the current best price is." Fast, certain to execute
  (if there's a counterparty at all), but you don't control the exact price — this is the only
  order type this project currently supports, a deliberate v1 scope choice.
- `LIMIT`: "only fill me at this exact price or better." Controls price, but might never execute if
  the market never reaches it.
- `SL` (stop-loss limit) / `SL-M` (stop-loss market): a *conditional* order — dormant until the
  price crosses a trigger price, at which point it activates as a limit/market order. Used to
  automate "exit if price moves against me" without watching the screen.

**Product type** — what happens to the position overnight:
- `MIS` (Margin Intraday Square-off): must be closed the same trading day — the broker auto-squares
  it off near market close if you haven't. Lower margin requirement in exchange for this restriction.
- `CNC` (Cash and Carry, equities only) / `NRML` (Normal, derivatives): can be carried forward to
  future days. Higher margin requirement since the broker is exposed to overnight price gaps.

**Variety**: `regular` (normal order), `amo` (after-market order, queued for the next session's
open), `co` (cover order, a bracketed market+stoploss pair), `iceberg` (a large order split into
smaller hidden chunks to avoid moving the market). This app always uses `regular`.

**Validity**: `DAY` (expires unfilled at end of day), `IOC` (immediate-or-cancel — fill whatever's
possible right now, cancel the rest instantly).

### The order lifecycle (why a "placed" order isn't automatically a "filled" order)

Placing an order is just *submitting* it to the exchange — it isn't a trade yet. Real statuses you
can see: `OPEN` (resting in the order book, unfilled), `TRIGGER PENDING` (an SL order waiting for
its trigger price), `COMPLETE` (fully filled — now an actual trade happened), `CANCELLED`
(withdrawn before it filled), `REJECTED` (the exchange/broker refused it — e.g. insufficient
margin, invalid price). An order can also **partially fill** — if you ask to buy 10 lots but only
5 are available at your price, you might get 5 filled and 5 still `OPEN`.

This is exactly why `LiveOrderExecutionService.PlaceWithKiteAsync` only sets `Status = Submitted`
immediately after `kite.PlaceOrder(...)` returns — it has a real `order_id`, but *whether it ever
actually fills* is only known later, which is why `LiveOrderReconciliationService` (polling) and
`KiteTickerService.OnOrderUpdate` (push) both exist to find out what really happened. Our paper
engine, by contrast, always fills instantly and fully (`OrderStatus` only has `Filled`/`Rejected`)
— a deliberate simplification, since there's no real exchange to partially match against.

---

## Part 4 — Margin: why a seller needs cash set aside but a buyer doesn't

If you **buy** an option, your maximum possible loss is the premium you already paid upfront —
there's nothing more the broker needs to guarantee, since you can't lose more than you've already
handed over.

If you **sell (write)** an option, your loss is theoretically unlimited (for a call) or very large
(for a put) if the market moves against you — you've promised to buy/sell at the strike price no
matter how far away the market moves. The broker requires you to **deposit margin** — cash set
aside, not available to spend — as collateral against that risk, calculated (in the real system)
via **SPAN margin** (a standardized exchange risk model) plus an **exposure margin** add-on.

This project's strategies **only ever buy, never sell/write options** — matching the "defined
risk" preference stated from the very beginning of this project. The margin logic in this codebase
exists for a narrower, different reason: `MarginCalculator` blocks margin for *short equity/future
positions* specifically (a flat, simplified per-lot amount, explicitly **not** real SPAN margin —
see its own doc comment), and `GetMargins()`/`GetOrderMargins()` on the real Kite API give the
actual numbers a real account would need, used in `LiveOrderExecutionService` to check real
available cash before placing a real buy order.

---

## Part 5 — P&L: how "did I make money" is actually computed

**Realized P&L**: profit/loss that's *locked in* — you closed the position, the gain/loss is final
and reflected in your cash balance.

**Unrealized P&L** ("mark-to-market" / "M2M"): the gain/loss *if you closed right now*, based on
the current market price — it goes up and down every tick while the position stays open, and isn't
real money yet.

**Average entry price**: if you buy in multiple pieces at different prices (e.g. 1 lot at ₹100,
then 1 more lot at ₹110), your position's average cost becomes the weighted average (₹105 for 2
lots) — not either individual price. This is exactly what `PositionUpdater.ApplyFill` computes,
and it's the single piece of math reused across the paper engine, the live engine, *and* the
backtest engine in this codebase (three different execution contexts, one correct formula).

**Closing out** ("squaring off"): selling what you bought (or buying back what you sold) to exit.
The difference between your average entry price and the exit price, times quantity, is your
realized P&L on that trade — computed here by `PnLCalculator`.

A concrete worked example, matching the actual numbers from the project's exit-logic test:
buy 1 lot at ₹2049 → price rises to ~₹2254 (10% up) → `PositionExitEvaluator`'s take-profit rule
fires (percent return on cost basis ≥ configured `TakeProfitPercent`) → sell 1 lot at ₹2254 →
realized P&L = (2254 − 2049) × lotSize, minus brokerage fees.

---

## Part 6 — Kite Connect's actual API, field by field

### Instrument master (`kite.GetInstruments`)

Each row describes one tradeable contract. Key fields we actually use:
`instrument_token` (a numeric id the exchange/Kite uses internally — stored as
`Instrument.KiteInstrumentToken`, used to subscribe to live ticks and fetch historical data),
`tradingsymbol` (the human-readable name, e.g. `NIFTY24NOV24500CE`), `name` (the underlying, e.g.
`NIFTY`), `expiry`, `strike`, `instrument_type` (`CE`=call, `PE`=put, `EQ`=equity),
`lot_size`, `exchange` (`NFO` for NIFTY/BANKNIFTY options, `NSE` for equities).

### Live tick (`Ticker.OnTick` → a `Tick` struct)

A `Tick` is one real-time snapshot of an instrument. Fields this app uses: `LastPrice` (the LTP —
the last actual trade price), `Bids`/`Offers` (the current top-of-book bid/ask depth — this app
uses the best/first level as `BidPrice`/`AskPrice`), `Timestamp`. This is the real-world analogue
of this project's `MarketQuote` row — the only difference is `MarketQuote` is also populated by
`MockQuoteRefresherService` with made-up numbers when no real feed is connected.

### Placing an order (`kite.PlaceOrder(...)`)

Every parameter maps directly to a concept above: `Exchange` (NFO/NSE), `TradingSymbol`,
`TransactionType` (BUY/SELL), `Quantity` (**actual units, not lots** — this app always multiplies
`lots × lotSize` before calling this), `Product` (MIS/NRML), `OrderType` (this app only ever passes
`MARKET`), `Variety` (always `regular` here). The **response** is just an `order_id` — confirmation
the exchange *accepted* the order, not that it *filled* (see Part 3's lifecycle section).

### Checking what happened (`kite.GetOrders()` / `GetOrderHistory`)

Returns an `Order` per order, with `Status` (the real lifecycle state — `OPEN`/`COMPLETE`/
`CANCELLED`/`REJECTED`/etc.), `FilledQuantity`/`PendingQuantity` (for partial fills),
`AveragePrice` (the actual average fill price — can differ slightly from the market price at
submission time), `OrderId`. `LiveOrderReconciler.ApplyUpdateAsync` reads exactly these fields to
update our own `LiveOrder`/`LiveFill` records.

### Checking available money (`kite.GetMargins(segment)`)

Returns `Available.Cash` (real spendable cash right now — this is what
`LiveOrderExecutionService` checks before a real buy) and a `Utilised` breakdown (`Span`,
`Exposure`, `OptionPremium`, `M2MUnrealised`, etc. — how much of your capital is currently tied up
in open positions/margin, broken down by *why*).

### Checking current holdings (`kite.GetPositions()`)

Returns `Day` (positions opened today) and `Net` (your actual overall position, day + carried
forward) position lists. Each `Position` has `Quantity` (**signed** — positive = net long,
negative = net short, in actual units not lots), `AveragePrice`, `PNL`, `M2M`. This is the real
ground truth our self-reconciled `LivePosition` is periodically compared against (see
`docs/ARCHITECTURE.md`'s "Live order execution" section) — our own records are built purely from
recorded fills, Kite's are the broker's own authoritative bookkeeping.

### Historical candles (`kite.GetHistoricalData(...)`)

Returns a list of `Historical` candles: `TimeStamp`, `Open`, `High`, `Low`, `Close`, `Volume` — one
row per time bucket (`Interval`: `"day"`, `"minute"`, `"5minute"`, etc.). This is literally what a
"candlestick chart" is made of — each candle is one bucket's open/high/low/close. Our
`BacktestEngine` replays these bars in order, exactly simulating what a strategy watching that
price history in real time would have done.

### Cancelling / modifying (`kite.CancelOrder` / `kite.ModifyOrder`)

Only meaningful for an order still `OPEN` at the exchange (not yet `COMPLETE`) — you're asking the
exchange to withdraw or change an order still sitting in the book, not undo a trade that already
happened (a completed trade can only be undone by placing an *opposite* trade).

---

## Part 7 — How it all maps back to this project's own entities

| Real-world concept | This project's entity/field |
|---|---|
| A trade actually happening | `PaperFill` / `LiveFill` |
| A submitted order (may or may not fill) | `PaperOrder` / `LiveOrder` |
| Your current net exposure to an instrument | `PaperPosition` / `LivePosition` |
| Weighted average cost of a position | `PositionUpdater.ApplyFill`'s `NewAverageEntryPrice` |
| Unrealized/mark-to-market P&L | `PnLCalculator.CalculateUnrealizedPnL` |
| An exchange order book snapshot | `MarketQuote` (bid/ask/LTP) |
| A candlestick chart bar | `HistoricalBar` |
| Collateral held against a risky position | `CashLedgerEntry` (`MarginBlock`/`MarginRelease`) |

See `docs/ARCHITECTURE.md` for how these fit together as code, and `docs/REQUEST_FLOWS.md` for a
line-by-line trace of a request moving through all of it.
