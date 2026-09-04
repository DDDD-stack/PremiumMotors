# PremiumMotors API

Every endpoint below is JSON over HTTPS and is browsable in Swagger at `/swagger`.
Authenticated calls use `Authorization: Bearer <accessToken>`.

## How accounts stay portable

One account works on the website and in the mobile app because both authenticate against the
same `AccountService` and receive the same credential type:

* **Access token** — a short-lived (60 min) JWT. Keep it in memory only.
* **Refresh token** — opaque, long-lived (30 days), stored **hashed** server-side. Keep it in
  Keychain (iOS) / EncryptedSharedPreferences (Android).

Refresh tokens are **single-use**: every refresh rotates the token and revokes the old one.
Presenting an already-rotated token is treated as a leak and revokes every session for that
account.

The website continues to use its server-side session cookie; `ICurrentUser` resolves identity
from a JWT claim or the session, so shared code does not care which client it is serving.

## Auth — `/api/v1/auth`

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/auth/register` | — | Create account, returns token pair |
| POST | `/api/v1/auth/login` | — | Sign in with **username or email** |
| POST | `/api/v1/auth/refresh` | — | Exchange refresh token for a new pair |
| POST | `/api/v1/auth/logout` | — | Revoke one refresh token (idempotent) |
| POST | `/api/v1/auth/logout-all` | Bearer | Sign out every device |
| GET | `/api/v1/auth/me` | Bearer | Current account |
| POST | `/api/v1/auth/change-password` | Bearer | Change password, optionally revoking other sessions |
| GET | `/api/v1/auth/sessions` | Bearer | List live sessions/devices |
| DELETE | `/api/v1/auth/sessions/{id}` | Bearer | Revoke one session |

`/api/v1/users/register` and `/api/v1/users/validate` still exist as aliases for older clients, but
now go through the same code path and also return tokens.

### Example

```http
POST /api/v1/auth/login
Content-Type: application/json

{ "username": "denis", "password": "hunter2", "device": "ios" }
```

```json
{
  "accessToken": "eyJhbGci...",
  "accessTokenExpiresUtc": "2026-08-31T22:30:00Z",
  "refreshToken": "8Xk2...",
  "refreshTokenExpiresUtc": "2026-09-30T21:30:00Z",
  "tokenType": "Bearer",
  "user": { "id": 4, "username": "denis", "email": "d@example.com", "phone": "", "role": "User", "registeredUtc": "..." }
}
```

Clients should refresh on a `401` and retry once; if the refresh also fails, sign the user out.

## Browsing — `/api/v1/cars`

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/cars` | — | Paged, filtered listings |
| GET | `/api/v1/cars/{id}` | optional | One listing, with full vehicle spec |
| GET | `/api/v1/cars/filters` | — | Makes, models, years, types, countries, fuels, gearboxes in one call |
| GET | `/api/v1/cars/stats` | — | Totals |
| POST | `/api/v1/cars/{id}/offers` | Bearer | Place a private offer |
| GET | `/api/v1/cars/{id}/offers` | Bearer | Read offers — **seller/admin only** |

`GET /api/v1/cars` supports `search, type, make, model, year, country, minPrice, maxPrice,
maxMileage, fuelType, transmission, availableOnly, sortBy, page, pageSize` and returns
`{ items, page, pageSize, totalCount, totalPages, hasMore }`. `sortBy` accepts
`newest, price_asc, price_desc, year_asc, year_desc, mileage_asc`.

`availableOnly=true` narrows to `Active` — cars that can still be offered on, excluding
reserved and sold.

### Listing status

`status` is a string enum and drives everything the client shows:

| Status | Public? | Takes offers? | Meaning |
|---|---|---|---|
| `Draft` | no | no | Created, not published (unpaid fee) |
| `Active` | yes | yes | On the market |
| `Reserved` | yes | no | Seller accepted an offer; sale in progress |
| `Sold` | yes | no | Sale completed |
| `Archived` | no | no | Taken off the market by the seller |

`acceptsOffers` is returned alongside it so a client does not have to hard-code the rule.

### Vehicle specification

`GET /api/v1/cars/{id}` returns a `spec` object: `mileage, serviceHistory,
serviceHistoryNotes, fuelType, transmission, drivetrain, engineSizeCc, powerHp, doors, seats,
exteriorColour, previousOwners, condition, hasAccidentHistory, firstRegistration, hasVin`.

The VIN itself is **never returned** — only `hasVin`. A VIN identifies a specific vehicle and
is worth scraping; buyers ask for it in the chat.

### Offers are private

A seller cannot offer on their own listing (`own_listing`). Admins are not blocked, since an
admin is not the seller of a user listing.

Offers are private to the seller. A public request never returns offer data. On
`GET /api/v1/cars/{id}`:

* the **seller or an admin** gets `offers` (all of them), `offerCount` and `buyerContact`;
* any other signed-in caller gets `myOffer` — their own offer and its status — and nothing else;
* an anonymous caller gets neither.

Offers do not have to beat each other or the asking price: there is no standing total to beat.
Nothing resolves on a timer — the seller answers each offer explicitly. Accepting one moves
the listing to `Reserved`, records `soldPrice`, releases contact details **in both directions**,
and automatically declines every other pending offer on that listing.

## Seller panel — `/api/v1/seller` (all Bearer)

The seller panel is exposed as an API as well as MVC pages, so it can move to its own
front-end later without touching the domain logic. See `docs/SELLER-PANEL.md`.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/seller/profile` | Seller profile (works for non-sellers too — `isSeller: false`) |
| POST | `/api/v1/seller/profile` | Become a seller |
| PUT | `/api/v1/seller/profile` | Update display name, type, location |
| GET | `/api/v1/seller/dashboard` | Counts for the panel header |
| GET | `/api/v1/seller/listings` | Your listings; `?status=` filters |
| GET | `/api/v1/seller/offers` | Offer inbox; `?status=` filters, pending first |
| POST | `/api/v1/seller/offers/{id}/accept` | Accept — reserves the car, declines the rest |
| POST | `/api/v1/seller/offers/{id}/decline` | Decline |
| POST | `/api/v1/seller/listings/{id}/sold` | Confirm the sale completed (from `Reserved` only) |
| POST | `/api/v1/seller/listings/{id}/reopen` | Sale fell through — back to `Active` |
| POST | `/api/v1/seller/listings/{id}/archive` | Take off the market |
| POST | `/api/v1/seller/listings/{id}/publish` | Put back on the market |

Selling is a **capability**, not a role: a seller keeps every buyer ability. `role` stays
`User`; check `isSeller` on `/api/v1/seller/profile` or `/api/v1/me`.

## Messaging — `/api/v1/conversations` (all Bearer)

**Placeholder scope** — polling, not push. See `docs/MESSAGING.md`.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/conversations` | Your threads, most recently active first |
| GET | `/api/v1/conversations/unread-count` | `{ count }`, for a tab badge |
| POST | `/api/v1/conversations` | Open/continue a thread (`{ carId, buyerId? }`) |
| GET | `/api/v1/conversations/{id}` | One thread with messages; marks them read |
| POST | `/api/v1/conversations/{id}/messages` | Send (`{ body }`, max 2000 chars) |

One thread per (listing, buyer). A seller opens one by passing the `buyerId`; a buyer omits it.
A thread the caller is not part of returns **404, not 403**, so the endpoint cannot be used to
probe which listings have active chats. Threads close automatically when the listing is sold.

Re-fetch on thread open and on app foreground — there is no realtime delivery yet.

## Favourites — `/api/v1/favorites` (all Bearer)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/favorites` | Saved listings |
| GET | `/api/v1/favorites/ids` | Just the ids, for painting heart icons |
| GET | `/api/v1/favorites/{carId}` | Is this saved? |
| PUT | `/api/v1/favorites/{carId}` | Save (idempotent) |
| DELETE | `/api/v1/favorites/{carId}` | Unsave (idempotent) |
| POST | `/api/v1/favorites/{carId}/toggle` | Toggle |

Identity comes from the token. The old routes took the username from the URL, which let anyone
read or modify anyone else's favourites.

## Selling — `/api/v1/listings` (all Bearer)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/listings/mine` | Your listings incl. drafts, offer counts, fee status |
| POST | `/api/v1/listings` | Create a draft |
| PUT | `/api/v1/listings/{id}` | Edit a draft |
| POST | `/api/v1/listings/{id}/photos` | Upload photos (multipart, field `photos`) |
| DELETE | `/api/v1/listings/{id}` | Delete a draft |
| POST | `/api/v1/listings/{id}/checkout` | Start/restart the listing-fee checkout |
| POST | `/api/v1/listings/payments/{paymentId}/capture` | Confirm payment after returning from PayPal |
| GET | `/api/v1/listings/{id}/payment` | Poll fee status |

`POST`/`PUT` take the full vehicle spec: `make, model, type, year, description, price, country,
city, mileage, serviceHistory, serviceHistoryNotes, fuelType, transmission, drivetrain,
engineSizeCc, powerHp, doors, seats, exteriorColour, previousOwners, condition,
hasAccidentHistory, vin, firstRegistration`.

`POST /api/v1/listings` returns a `status` of:

* `published` — free-listing mode or a free relist; already live.
* `payment_required` — open `checkoutUrl`, then call the capture endpoint.
* `payment_failed` — the draft was saved but checkout could not start.

Mobile clients pass their own deep links as `?returnUrl=&cancelUrl=`; omitting them falls back
to the website's pages.

## Account tab — `/api/v1/me` (all Bearer)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/me` | Profile |
| PUT | `/api/v1/me` | Update email/phone |
| GET | `/api/v1/me/offers` | Offers you placed, with status and the seller's reply |
| GET | `/api/v1/me/purchases` | Cars you bought (an offer of yours was accepted) |
## Errors

Failures return `{ "error": "human readable", "code": "machine_readable" }`. Codes in use:
`registration_failed`, `invalid_credentials`, `invalid_refresh_token`, `account_missing`,
`change_password_failed`, `not_found`, `forbidden`, `invalid_amount`, `auction_closed`,
`invalid_auction_end`, `already_published`, `no_photos`, `unsupported_type`, `update_failed`,
`own_listing`.

Login failures are deliberately identical whether the account exists or the password is wrong,
so the endpoint cannot be used to enumerate accounts.

---

## Conventions for mobile clients

All routes are versioned under `/api/v1/`. Once the React Native app ships you cannot change a
response shape without breaking installed copies, so treat v1 as frozen and add `/api/v2/`
for breaking changes.

**Enums are names, not numbers** — `"type": "Sedan"`, never `"type": 0`. Numbers are still
accepted on input.

**All timestamps are UTC ISO 8601 with a `Z` suffix** — `"2026-12-15T11:00:00Z"`. Everything is
stored as `timestamptz`; convert to local time for display only. When sending a date, send UTC;
a value without an offset is interpreted as UTC.

**Image URLs are absolute** — a React Native `<Image>` cannot resolve a site-relative path.
Set `App:PublicBaseUrl` in production so they stay stable behind a proxy.

**List endpoints are paged**: `{ items, page, pageSize, totalCount, totalPages, hasMore }`.
Built for infinite scroll.

**Errors are uniform**: `{ "error": "...", "code": "..." }`, including `429` rate-limit
rejections, so one client-side handler covers everything.

### Token handling

Access token in memory, refresh token in Keychain (iOS) / EncryptedSharedPreferences (Android) —
never `AsyncStorage`. On `401`, refresh once and retry; if the refresh also fails, sign out.
Refresh tokens are **single-use**: each refresh rotates them, and replaying an old one revokes
every session for that account, so never issue two refreshes concurrently.

Send `X-Client-Device: ios` (or `android`) so sessions are labelled in `/api/v1/auth/sessions`.

### Rate limits

10 requests/minute on `/api/v1/auth/*`, 300/minute elsewhere, per user or per IP. Rejections are
`429` with `Retry-After`. Back off rather than retrying immediately.

## Account recovery

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/auth/forgot-password` | — | Emails a reset link. **Always 204** |
| POST | `/api/v1/auth/reset-password` | — | Redeems the token, revokes all sessions |
| POST | `/api/v1/auth/send-verification` | Bearer | Sends/resends the verification link |
| POST | `/api/v1/auth/verify-email` | — | Redeems the verification token |

Tokens are single-use and hashed at rest; reset expires in 1 hour, verification in 3 days.
`forgot-password` returns 204 whether or not the address exists — otherwise it becomes an
account-enumeration oracle. For mobile, register deep links for `/reset-password?token=` and
`/verify-email?token=`.

## Privacy and account control

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/v1/me/export` | Bearer | GDPR data export as JSON |
| POST | `/api/v1/me/delete` | Bearer | GDPR erasure — needs password + `"confirm": "DELETE"` |

Deletion **anonymizes**: personal data is scrubbed everywhere it appears, including the
denormalized username copies on listings, bids and payments, while auction history survives
under an anonymous handle. Irreversible.

## Operations

`/health/live` — process is up. `/health/ready` — database reachable.
