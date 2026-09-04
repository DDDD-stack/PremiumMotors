# Seller panel — placeholder scope and the "own domain?" decision

Status: **functional panel, integrated into the main site, built so it can be extracted.**

## The decision that is still open

The panel can live in either of two places, and the code is arranged so the choice can be
made later without redoing the domain logic:

**A. Stays in this app** (what ships today). `/Seller/*` MVC pages. One deploy, one session
cookie, one domain. Simplest to run.

**B. Its own front-end on its own domain.** `seller.premiummotors.…` talking to
`/api/v1/seller` with a JWT. Better if the seller experience diverges a lot from the buyer
one, or if a separate team ends up owning it.

Nothing has to be rewritten to switch. Every seller operation lives in
`Services/Marketplace/SellerService.cs` and `OfferService.cs`, and is exposed **twice**:

| Operation | Website | API |
|---|---|---|
| Become a seller | `POST /Seller/Start` | `POST /api/v1/seller/profile` |
| Dashboard | `GET /Seller/Dashboard` | `GET /api/v1/seller/dashboard` |
| My listings | `GET /Seller/Listings` | `GET /api/v1/seller/listings` |
| Offer inbox | `GET /Seller/Offers` | `GET /api/v1/seller/offers` |
| Accept offer | `POST /Seller/AcceptOffer` | `POST /api/v1/seller/offers/{id}/accept` |
| Decline offer | `POST /Seller/DeclineOffer` | `POST /api/v1/seller/offers/{id}/decline` |
| Mark sold | `POST /Seller/MarkSold` | `POST /api/v1/seller/listings/{id}/sold` |
| Reopen | `POST /Seller/Reopen` | `POST /api/v1/seller/listings/{id}/reopen` |
| Archive / publish | `POST /Seller/Archive` / `Relist` | `POST /api/v1/seller/listings/{id}/archive` / `publish` |
| Profile | `GET|POST /Seller/Profile` | `GET|PUT /api/v1/seller/profile` |

`Controllers/SellerController.cs` is intentionally a thin shell: it reads the session, calls a
service, and picks a redirect. **Keep it that way.** The moment real logic lands in that file,
option B stops being cheap.

If option B is chosen, the work is: build the front-end against the API, point a subdomain at
it, add that origin to `Cors:AllowedOrigins`, delete `Controllers/SellerController.cs` and
`Views/Seller/`, and change the navbar link. The database, services and API do not move.

## Seller is a capability, not a role

`User.IsSeller` is a flag alongside `Role`. It is deliberately **not** a role swap: a seller
keeps every buyer ability — browsing, favourites, and making offers on other people's cars.
The session mirrors it as `SessionKeys.IsSeller`, written on login and refreshed when someone
opts in. `[SellerOnly]` redirects a signed-in buyer to the opt-in form rather than returning
403, because becoming a seller is self-service, not a permission someone grants.

## Two ways in, and only one of them asks questions

Registration forks at `/Account/Signup`:

| Route | Creates | Seller panel |
|---|---|---|
| `/Account/Register` | Personal account | Locked until they opt in at `/Seller/Start` |
| `/Account/RegisterBusiness` | Dealer account | Unlocked immediately |

A business gives its company details **once**, at signup: name, registration number, VAT,
trading address, website and responsible person. `RegisterBusinessAsync` sets `IsSeller`,
`SellerType = Dealer` and the business record in one step.

This is why `/Seller/Start` no longer asks "private seller or dealer?" — that question only
existed because signup did not know the answer. Anyone reaching that form came through the
personal route, so they are a private seller by construction. `SellerType` is likewise not
editable on the seller profile: a private seller who starts trading should register a business
account rather than flip a dropdown and skip the business record entirely.

## The panel lives inside the profile

`_ProfileNav.cshtml` is one sub-navigation across `/Account/*`, `/Seller/*`, `/Favorites` and
`/Messages`, so a seller has one place to go rather than two. `ProfileNavService` fills in its
badge counts; every controller that renders the partial calls it, deliberately rather than a
global filter, because the browse page does not need those four queries.

This is presentation only. The seller routes and the `/api/v1/seller` mirror are unchanged, so
the extraction procedure above still applies.

## What is deliberately not built

1. **Dealer verification.** `User.SellerVerified` exists and is never set to `true`. Dealers
   need business registration / VAT / ID checks and a review queue. None of that exists — no
   upload, no reviewer UI, no appeal. The profile page says so out loud.
2. **A public seller page.** There is no `/seller/{name}` showing all of a seller's cars, and
   no seller rating or review.
3. **Per-seller limits.** Nothing caps how many listings one account can post, so nothing
   stops a scraper-fed dealer flooding the front page.
4. **Payouts or escrow.** Money never touches the platform. The sale completes off-platform
   between the two parties, and both sides get the other's contact details on acceptance.
5. **Bulk tools.** No CSV import, no bulk price edit, no stock feed — a dealer with 80 cars
   would have to use the form 80 times.
6. **Analytics.** No listing views, no offer-conversion rate. `Car` has no view counter, which
   also blocks the paid-boost monetization in `MONETIZATION.md` — that needs per-view counting.

## Order to build them in

1. Listing view counts. Blocks monetization and is a day's work.
2. Public seller page. Sellers ask for it immediately; it is also the natural home for #3.
3. Dealer verification, before charging dealers anything.
4. Bulk tools, only once a real dealer signs up.
