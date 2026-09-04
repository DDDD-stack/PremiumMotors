# Monetization plan

Decided 1 September 2026. Supersedes the €5 listing fee.

## Where things stand

**Launch: free.** `Listing:ListingFeeCents` is `0`, so listings publish immediately
and no payment provider is involved. This is deliberate — it removes the friction that
was blocking supply, and it sidesteps the fact that card processing is hard to arrange
from Albania.

**Later: paid daily boosts.** Once there is traffic worth advertising to, sellers pay a
flat daily rate to have a listing featured. Not built yet.

## Why flat-rate per day rather than per view or per click

A per-view model (€0.003/view, €3 CPM) was considered and rejected for now:

* **It monetizes an audience that does not exist yet.** At €3 CPM you need roughly
  33,000 boosted views a month to clear €100. At launch that is approximately zero
  revenue for a substantial amount of work.
* **It is the most fraud-exposed model available.** The seller pays per view, so anyone
  can drain a competitor's budget by refreshing. Doing it properly needs impression
  deduplication, bot filtering, viewability rules and hard budget caps — a real
  anti-abuse system, not a feature.
* **It breaks the money columns.** €0.003 is 0.3 cents. `Payment.AmountCents` is a
  `long` in whole cents, so every charge would truncate to zero and bill nothing. Per-view
  pricing would require migrating all money to micro-euros (`long` millionths) or
  `decimal(18,6)`.

Flat-rate avoids all three. €2/day is 200 cents, which the existing schema stores exactly.
There is no impression table, no viewability question, and no invalid-traffic surface —
a boost either is or is not active on a given day.

## Design

### Wallet

Sellers top up a balance, then spend it on boosts. One payment event per top-up rather
than one per boost, which matters a lot given how constrained payment collection is in
Albania — fewer, larger transactions mean fewer provider interactions and lower fees.

The balance is the **sum of a ledger**, never a mutable column. A stored balance that
drifts from its transaction history is unreconcilable.

```
WalletEntry
    Id, UserId, AmountCents (signed), Kind (topup | spend | refund | adjustment),
    Reference (PaymentId or BoostId), CreatedUtc
```

### Boosts

```
ListingBoost
    Id, CarId, UserId, RatePerDayCents, Days,
    StartUtc, EndUtc, TotalCostCents, Status (active | expired | cancelled), CreatedUtc
```

A listing is featured when it has a boost whose window covers now. Selecting featured
listings is then a single indexed query — no counters to increment on every page view.

### Payment entity

`Payment` is **repurposed from listing fees to wallet top-ups**. Its provider-agnostic
fields (`Provider`, `ProviderOrderId`, `ProviderCaptureId`, `Status`, `AmountCents`,
`PaidUtc`) carry over unchanged, and the existing capture flow is reused.

These fields become obsolete and should be dropped when the old flow is removed:
`CarId`, `OfferConsumed`, `RelistCount`.

### Placement and labelling

Boosted listings need a distinct slot in browse results and **must be visibly labelled as
sponsored** — that is a legal requirement for advertising in the EU, not a design choice.
Rotate fairly between competing active boosts rather than always ordering by spend.

## What is inert right now

With `ListingFeeCents = 0`, the listing-token economics are dormant but still present:
`Payment.OfferConsumed`, `RelistCount`, `MaxFreeRelists`, the free-relist reuse in
`SellController.Create`, and the token release in `AuctionCloseService`. None of it runs.
It is left in place deliberately so a fee can be reinstated by changing one config value
if boosts underperform.

## Open questions for when this gets built

* Price point. €2/day is a placeholder; nothing validates it.
* Minimum top-up. Needs to be comfortably above the payment provider's fixed fee —
  a €2 top-up is mostly provider fee.
* Refunds for a boost cancelled mid-run, and what happens to a boost when its auction
  closes early.
* Whether unused balance is refundable. This has consumer-law implications and should be
  settled in the terms before any money is taken.
* Which provider collects top-ups. See the Albania options in the pre-launch review.
