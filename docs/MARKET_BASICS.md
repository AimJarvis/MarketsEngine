# How the Stock Market Works — and What Our APIs Actually Mean

This file explains market concepts in plain language, with a worked numeric example for almost
every term, then ties each one to the real Kite Connect API fields and our own code (`PlaceOrder`,
`GetMargins`, `GetPositions`, `GetHistoricalData`, `Instrument`, `Order`, `Tick`) — so "the textbook
concept" and "the actual field in our code" are never two separate things to learn.

Read top to bottom the first time. After that, use it as a reference — every heading is searchable.

Every example below uses realistic NIFTY/RELIANCE-style numbers in ₹ so the math is concrete, not
abstract.

---

## Part 1 — What a stock exchange actually is

**In simple words:** an exchange (NSE — National Stock Exchange, BSE — Bombay Stock Exchange) is
just a giant matchmaking system. You say "I'll buy at this price," someone else says "I'll sell at
this price," and the moment two prices agree, a **trade** happens. That's it — "the market" is just
millions of these tiny matches happening every second during trading hours (NSE: 9:15 AM – 3:30 PM
IST, Monday–Friday).

### Order book, bid, ask, spread, LTP

**Order book** — the list of everyone currently waiting to buy or sell, sorted best-price-first.

**Example** — a simplified order book for one option:

| Buyers waiting (bids) | Sellers waiting (asks) |
|---|---|
| ₹120.50 — 3 lots | ₹121.00 — 2 lots |
| ₹120.25 — 5 lots | ₹121.25 — 4 lots |
| ₹120.00 — 10 lots | ₹121.50 — 8 lots |

From this table:
- **Bid price = ₹120.50** — the highest price any buyer is currently offering.
- **Ask price = ₹121.00** — the lowest price any seller is currently asking.
- **Spread = ₹121.00 − ₹120.50 = ₹0.50** — the gap between them. A small spread (like this) means
  the instrument is liquid (lots of buyers/sellers, easy to trade). A big spread (₹5+) means it's
  thin/illiquid — your own order will move the price against you just by showing up.
- **LTP (Last Traded Price)** — not shown in the table above because it's not a standing order, it's
  *history*: the price of the most recent trade that actually happened. It can sit anywhere between
  (or even outside) the current bid/ask if the market just moved.

### Equities vs. derivatives

**In simple words:** a share of RELIANCE means you own a tiny sliver of the actual company. An
**option** (a type of derivative) means you own a *bet* on where RELIANCE's price goes — you never
own any part of the company itself. NIFTY/BANKNIFTY options are bets on an **index** (a basket of
~50 stocks' combined value), so there's no company to own at all — only a number (the index level)
to bet on.

**Example** — if you buy 1 share of RELIANCE at ₹2,900, you now legally co-own a tiny piece of
Reliance Industries. If you instead buy 1 NIFTY option, you own a contract that pays off based on
where the NIFTY *index number* ends up — you don't own any of the 50 companies in it.

---

## Part 2 — Options, from scratch

**In simple words:** an option is a coupon that gives you the *right* — but not the obligation — to
buy or sell something at a price fixed today, by a date fixed today.

### Call option

**In simple words:** the right to **buy** at a fixed price. You buy a call when you think the price
will go **up**.

**Example:** NIFTY is at 24,000 today. You buy a "24,500 Call" expiring in 2 weeks. You're betting
NIFTY will be above 24,500 by expiry. If NIFTY ends at 24,800, your call is worth something
(see "Intrinsic value" below). If NIFTY ends at 24,100, your call expires worthless.

### Put option

**In simple words:** the right to **sell** at a fixed price. You buy a put when you think the price
will go **down**.

**Example:** NIFTY is at 24,000. You buy a "23,500 Put." You're betting NIFTY falls below 23,500 by
expiry. If NIFTY ends at 23,200, your put is worth something. If NIFTY ends at 24,000, it expires
worthless.

### Strike price, premium, expiry

- **Strike price** — the fixed price written into the contract (24,500 and 23,500 in the examples
  above).
- **Premium** — the price you pay *today* to own the option. This is the **only** money at risk if
  you're the buyer (which is all this project's strategies ever do — see "Margin" below).
  **Example:** if the premium for the 24,500 Call is ₹120, and NIFTY's lot size is 75, buying
  1 lot costs you **₹120 × 75 = ₹9,000** upfront. If NIFTY never reaches 24,500, you lose exactly
  that ₹9,000 — never more.
- **Expiry** — the date the contract stops existing. NIFTY/BANKNIFTY options are **European style**
  (can only be settled *at* expiry, not early) and **cash-settled** (nobody actually buys/sells the
  index — at expiry, the cash difference is simply credited or debited to your account).

### Lot size

**In simple words:** you can't buy "1 unit" of a NIFTY option — you buy in fixed bundles called
**lots**. If the lot size is 75, one lot = 75 units of the index, bundled together as one tradeable
item.

**Example:** lot size = 75, you buy "2 lots." You now hold 2 × 75 = **150 units** worth of exposure,
even though you only ever typed "2." In our code, this is `Instrument.LotSize` — every quantity
field in this app means "number of lots," and the app always multiplies by `LotSize` before telling
Kite the real exchange quantity.

### ITM / ATM / OTM (moneyness)

**In simple words:** this just describes whether exercising the option *right now* would make you
money, lose you money, or be a wash — based on where the strike sits relative to the current price.

**Example** — NIFTY is at 24,000. Three different call options:

| Strike | Where it sits | Moneyness | Why |
|---|---|---|---|
| 23,500 | below current price | **ITM** (in the money) | you could "buy" at 23,500 and the index is already at 24,000 — a ₹500 head start |
| 24,000 | equal to current price | **ATM** (at the money) | no head start, no gap |
| 24,500 | above current price | **OTM** (out of the money) | the index would need to *rise* 500 points before this would be worth exercising |

(For a **put**, it's the mirror image — a put is ITM when its strike is *above* the current price.)

OTM options are cheaper (lower premium) exactly because they need a bigger favorable move to pay
off — and most OTM options simply expire worthless.

### Intrinsic value

**Definition:** the real, immediate money an option would be worth if exercised right this second —
not counting any "it might still move in my favor" hope. Only ITM options have intrinsic value;
ATM/OTM options have **zero** intrinsic value.

**Formulas:**
- Call option: `Intrinsic Value = max(Spot Price − Strike Price, 0)`
- Put option: `Intrinsic Value = max(Strike Price − Spot Price, 0)`

**Example:** NIFTY (spot) is at 24,600. You hold a 24,500 Call.
`Intrinsic Value = max(24,600 − 24,500, 0) = ₹100`.
If instead you held a 24,500 **Put** at the same spot price:
`Intrinsic Value = max(24,500 − 24,600, 0) = max(−100, 0) = ₹0` (it's OTM, so zero, never negative).

### Time value and theta decay

**In simple words:** the premium you actually pay is almost never *just* the intrinsic value — it
also includes a bit extra for "there's still time left for this to move further in my favor." That
extra bit is **time value**, and it shrinks every single day as expiry gets closer, even if the
price doesn't move at all. That daily shrinkage is called **theta decay**.

**Formula:** `Time Value = Premium − Intrinsic Value`

**Example:** continuing the 24,500 Call above (intrinsic value ₹100). Say the option is actually
trading at a premium of ₹145.
`Time Value = 145 − 100 = ₹45`.
That ₹45 is pure "hope value" — with 10 days left to expiry it might be ₹45; with 1 day left
(same spot price, nothing else changed) it might be only ₹5, because there's barely any time left
for a bigger move to happen. **This is the single biggest reason option buyers lose money more
often than they win** — even being right about direction isn't enough if it takes too long, because
theta eats the premium every day in the meantime. It's also exactly why `SimpleMomentumStrategy`'s
own doc comment warns that measuring momentum on an *option's* premium (not the underlying index)
is contaminated by this decay — a real complication specific to trading options.

---

## Part 3 — How an order actually becomes a trade

**Transaction type**: `BUY` or `SELL` — which side of the trade you're on. Simple.

### Order type — how the price gets decided

- **`MARKET`** — "fill me right now, at whatever the best available price is." Fast and (almost)
  guaranteed to execute, but you don't control the exact price.
  **Example:** you click "Buy 1 lot, Market" on a call trading at bid ₹120.50 / ask ₹121.00 — you'll
  likely pay close to ₹121.00 (the ask), not exactly ₹120.50. **This is the only order type this
  project currently places — a deliberate v1 scope choice**, not an oversight. There is no limit
  order, no trigger/stop order anywhere in this codebase.
- **`LIMIT`** — "only fill me at this price or better." Controls price, but might never execute.
  **Example:** you place a buy limit at ₹119.00 while the ask is ₹121.00 — nothing happens until/
  unless the price actually drops to ₹119.00.
- **`SL` (stop-loss limit) / `SL-M` (stop-loss market)** — a *dormant* order that only "wakes up"
  and becomes a real limit/market order once the price crosses a trigger you set.
  **Example:** you're long from ₹120, and place an SL-M with trigger ₹114 — if the price falls to
  ₹114, it automatically fires a market sell to get you out, without you watching the screen.
  **This project does not use this order type at all** — see the stop-loss note below.

> **Important distinction for this project specifically:** "stop-loss" in this codebase is **not**
> a resting `SL`/`SL-M` order sitting with the broker. It's our own app checking prices periodically
> (every `Strategy:EvaluationIntervalSeconds`, default 30s) and, if the loss threshold is breached,
> firing an ordinary `MARKET` sell order at that moment. Between checks, nothing is protecting the
> position — a real broker-side stop order would trigger instantly even if our app were offline;
> ours only reacts the next time it checks.

### Product type — what happens to the position overnight

- **`MIS`** (Margin Intraday Square-off) — must close the same day; the broker force-closes it near
  market close if you haven't. Needs less margin in exchange for this restriction.
- **`CNC`** (equities) / **`NRML`** (derivatives) — can be held overnight/for days. Needs more
  margin since the broker is now exposed to price gaps while markets are closed.

**Example:** you buy 1 lot `MIS`. If you forget to sell by ~3:20 PM, Kite auto-sells it for you —
you can't accidentally hold it overnight. With `NRML`, it would simply carry over to tomorrow.

### Variety and validity

- **Variety**: `regular` (normal), `amo` (queued for tomorrow's open), `co` (a bundled order +
  auto-stoploss pair), `iceberg` (a big order secretly split into small chunks). **This app always
  uses `regular`.**
- **Validity**: `DAY` (cancels automatically if unfilled by end of day), `IOC` (fill whatever's
  possible this instant, cancel the rest immediately — no waiting at all).

### The order lifecycle — why "placed" ≠ "filled"

**In simple words:** submitting an order just means "the exchange has received your request." It
does **not** mean a trade happened yet.

**Example walkthrough** — you ask to buy 10 lots at market:
1. Status becomes `OPEN` the instant it's accepted.
2. Say only 6 lots are available to match right now → 6 lots fill, status becomes
   `PARTIALLY FILLED`, the remaining 4 lots stay `OPEN` waiting for more sellers.
3. A moment later the rest matches too → status becomes `COMPLETE` — now (and only now) is it a
   real, fully-settled trade.
4. (Alternative ending: you cancel the remaining 4 before they fill → `CANCELLED`. Or: the broker
   rejects the whole order upfront for insufficient margin → `REJECTED`, nothing ever fills.)

This is exactly why `LiveOrderExecutionService.PlaceWithKiteAsync` only sets `Status = Submitted`
the instant `kite.PlaceOrder(...)` returns — it has a real `order_id`, but whether it *actually*
fills is only known later. That's why `LiveOrderReconciliationService` (checks periodically) and
`KiteTickerService.OnOrderUpdate` (gets told instantly by Kite) both exist — to find out what really
happened. Our **paper** engine skips all of this nuance on purpose: every paper order fills
instantly and completely (`OrderStatus` only ever has `Filled`/`Rejected`), since there's no real
exchange order book to partially match against.

---

## Part 4 — Margin: why a seller needs cash set aside but a buyer doesn't

**In simple words:** if you **buy** an option, the absolute worst case is you lose the premium you
already paid — nothing more can go wrong, so the broker doesn't need to hold anything extra from
you.

If you **sell (write)** an option, you've made a promise ("I'll sell you NIFTY at 24,500 no matter
what") and the market could move hugely against that promise. The broker makes you **deposit
margin** — cash locked up as a safety deposit — in case you can't honor it.

**Example:**
- **Buying** a 24,500 Call for ₹120 premium, lot size 75: you pay **₹9,000 total, upfront, period.**
  Nothing else is ever asked of you for this position — win, lose, or expire worthless.
- **Selling (writing)** that same call: you *receive* ₹9,000 upfront, but the broker might demand
  (for example) ₹60,000+ in margin as collateral, because if NIFTY rockets to 26,000, you owe the
  buyer the difference — potentially far more than the ₹9,000 you collected.

**This project's strategies only ever buy options, never sell/write them** — matching the
"defined-risk" principle stated from day one. The margin code that *does* exist
(`MarginCalculator`) is for something narrower: blocking margin on **short equity/future
positions** specifically, using a flat simplified per-lot number — explicitly **not** real SPAN
margin (its own code comment says so). The real SPAN/exposure margin numbers only ever come from
Kite's own `GetMargins()`/`GetOrderMargins()`, used to check real spendable cash before a real buy.

---

## Part 5 — P&L: how "did I make money" is actually computed

### Realized vs. unrealized P&L

- **Realized P&L** — profit/loss that's *locked in*. You closed the position; it's final, and it's
  already reflected in your actual cash balance.
- **Unrealized P&L** (also called "mark-to-market" / "M2M") — the profit/loss *if you closed right
  now*. It changes every time the price ticks, and isn't real money until you actually close.

**Example:** you bought 1 lot at ₹2,049 (lot size 1, for simplicity). Price is now ₹2,150.
Unrealized P&L = (2,150 − 2,049) × 1 = **+₹101**, but only on paper — you haven't sold yet, so
nothing has actually hit your cash balance. If the price dropped back to ₹2,049 tomorrow, that ₹101
simply disappears again, because you never locked it in.

### Average entry price (what happens when you buy more than once)

**In simple words:** if you buy the same thing twice at two different prices, your "cost" becomes
the blended average of both purchases — weighted by how much you bought each time.

**Formula:** `Average Price = (Price₁ × Qty₁ + Price₂ × Qty₂ + ...) / Total Qty`

**Example:** you buy 1 lot at ₹100, then later buy 1 more lot at ₹110.
`Average Price = (100 × 1 + 110 × 1) / 2 = 210 / 2 = ₹105`.
Your position now shows "2 lots @ ₹105 average" — not ₹100, not ₹110, the blend. This exact formula
is `PositionUpdater.ApplyFill` — the one piece of math reused, unchanged, across the paper engine,
the live engine, and the backtest engine in this codebase.

### Closing out ("squaring off") and realized P&L

**Formula:** `Realized P&L = (Exit Price − Average Entry Price) × Lot Size × Quantity − Fees`
(for a position you originally *bought*; flip the subtraction order if you originally *sold*)

**Worked example** (real numbers from this project's own exit-logic testing): you buy 1 lot at
₹2,049. Price rises to ₹2,254 — a **10% gain** on your cost. The app's take-profit rule
(`PositionExitEvaluator`) checks: *has the percent return on cost basis reached the configured
`TakeProfitPercent`?* Yes (10% ≥ 10%) → it automatically sells 1 lot at ₹2,254.

`Realized P&L = (2,254 − 2,049) × 1 lot × 1 = ₹205, minus brokerage fees.`

### Stop-loss/take-profit percent, in numbers

**Definition:** `PositionExitEvaluator` doesn't look at raw price moves — it looks at **percent
return on your cost basis**, so the same rule works whether you're up or down, long or short.

**Formula:** `Percent Return = Unrealized P&L / (Average Entry Price × Lot Size × Quantity) × 100`

**Example:** you bought 1 lot at ₹2,000 (lot size 1). Price falls to ₹1,900.
`Unrealized P&L = (1,900 − 2,000) × 1 = −₹100`.
`Percent Return = −100 / (2,000 × 1 × 1) × 100 = −5%`.
If `StopLossPercent` is configured at 5%, this exactly triggers the stop — the app immediately
sells to cut the loss there.

**Who decides that 5%/10%?** As of the latest change to this project, it's no longer always one
fixed global number:
- `SimpleMomentum` has no real basis for picking its own number, so its positions always use the
  app-wide default (`Strategy:StopLossPercent`/`TakeProfitPercent` in config).
- `Kronos` actually produces a price *forecast*, so it scales its own stop/target off how big a
  move it's predicting. **Example:** Kronos forecasts NIFTY rising 2% → with the default scaling
  factors, it sets that position's take-profit at 2% and stop-loss at 1% (half the forecast) —
  tighter or wider automatically depending on how confident/large the forecast is, instead of
  always using the same 10%/5%.

---

## Part 6 — Kite Connect's actual API, field by field (with example payloads)

### Instrument master (`kite.GetInstruments`)

Each row describes one tradeable contract.

**Example row** (simplified):
```
instrument_token: 256265
tradingsymbol:    NIFTY24NOV24500CE
name:             NIFTY
expiry:           2024-11-28
strike:           24500
instrument_type:  CE          (CE = call, PE = put, EQ = equity)
lot_size:         75
exchange:         NFO         (NFO for NIFTY/BANKNIFTY options, NSE for equities)
```
`instrument_token` is what we store as `Instrument.KiteInstrumentToken` — it's the numeric ID Kite
actually uses internally to look up ticks/historical data; the human-readable `tradingsymbol` is
just for display.

### Live tick (`Ticker.OnTick` → a `Tick` struct)

**Example tick** (simplified): `{ LastPrice: 24650.50, Bids: [{Price: 24650.00, Quantity: 300}],
Offers: [{Price: 24651.00, Quantity: 150}], Timestamp: "...14:32:05" }`. This is a live, real-time
version of the order-book example from Part 1 — `LastPrice` is the LTP, the first `Bids`/`Offers`
entry is the current best bid/ask. Our `MarketQuote` row is the app's own copy of exactly this,
except `MockQuoteRefresherService` fills it with made-up numbers when no real Kite feed exists.

### Placing an order (`kite.PlaceOrder(...)`)

**Example request:**
```
Exchange: NFO, TradingSymbol: NIFTY24NOV24500CE, TransactionType: BUY,
Quantity: 150,       <- NOT "2 lots" - this app always sends lots × lot_size (2 × 75 = 150)
Product: NRML, OrderType: MARKET, Variety: regular
```
**Example response:** `{ order_id: "231029000012345" }` — that's it. Just an ID confirming the
exchange *accepted* the request. It tells you nothing about whether it filled (see Part 3).

### Checking what happened (`kite.GetOrders()` / `GetOrderHistory`)

**Example response row:** `{ OrderId: "231029000012345", Status: "COMPLETE",
FilledQuantity: 150, PendingQuantity: 0, AveragePrice: 121.35 }` — note `AveragePrice` can differ
slightly from the price you expected at submission time (that's normal for a `MARKET` order).
`LiveOrderReconciler.ApplyUpdateAsync` reads exactly these fields to update our own
`LiveOrder`/`LiveFill` rows.

### Checking available money (`kite.GetMargins(segment)`)

**Example response:** `{ Available: { Cash: 85000.00 }, Utilised: { Span: 12000, Exposure: 3000,
OptionPremium: 9000, M2MUnrealised: -450 } }` — `Available.Cash` is real spendable money right now
(checked by `LiveOrderExecutionService` before a real buy); `Utilised` breaks down *why* the rest
of your capital is tied up.

### Checking current holdings (`kite.GetPositions()`)

**Example response row:** `{ TradingSymbol: "NIFTY24NOV24500CE", Quantity: 150, AveragePrice: 121.35,
PNL: 340.00, M2M: 340.00 }` — `Quantity` is **signed** (positive = long, negative = short) and in
real units (150), not lots (2). This is the broker's own ground truth — our self-recorded
`LivePosition` is periodically compared against it (see `docs/ARCHITECTURE.md`), never assumed to
already match.

### Historical candles (`kite.GetHistoricalData(...)`)

**Example response row:** `{ TimeStamp: "2024-10-15", Open: 24200, High: 24450, Low: 24150,
Close: 24400, Volume: 128000000 }` — one row per time bucket (a `day`, `minute`, `5minute`, etc.).
This is literally what one bar of a candlestick chart is made of. `BacktestEngine` replays a list of
these, in order, to simulate what a strategy would have done watching that price history live.

### Cancelling / modifying (`kite.CancelOrder` / `kite.ModifyOrder`)

Only meaningful while an order is still sitting `OPEN` at the exchange — you're withdrawing/
changing a request that hasn't become a trade yet. A trade that's already `COMPLETE` can never be
"undone" this way — the only way to reverse it is to place a brand-new *opposite* trade.

---

## Part 7 — How it all maps back to this project's own entities

| Real-world concept | This project's entity/field |
|---|---|
| A trade actually happening | `PaperFill` / `LiveFill` |
| A submitted order (may or may not fill) | `PaperOrder` / `LiveOrder` |
| Your current net exposure to an instrument | `PaperPosition` / `LivePosition` |
| Weighted average cost of a position | `PositionUpdater.ApplyFill`'s `NewAverageEntryPrice` |
| Unrealized/mark-to-market P&L | `PnLCalculator.CalculateUnrealizedPnL` |
| The stop-loss/take-profit percent governing a position | `PaperPosition`/`LivePosition`'s own `StopLossPercent`/`TakeProfitPercent` (falls back to `Strategy:StopLossPercent`/`TakeProfitPercent` if the strategy didn't set one) |
| An exchange order book snapshot | `MarketQuote` (bid/ask/LTP) |
| A candlestick chart bar | `HistoricalBar` |
| Collateral held against a risky position | `CashLedgerEntry` (`MarginBlock`/`MarginRelease`) |

See `docs/ARCHITECTURE.md` for how these fit together as code, and `docs/REQUEST_FLOWS.md` for a
line-by-line trace of a request moving through all of it.
