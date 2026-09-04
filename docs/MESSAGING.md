# Buyer/seller messaging — placeholder scope

Status: **working stub, not production messaging.**

The thread is real. Messages persist in Postgres, access control is enforced server-side,
and both the website and the API return the same data. That part is deliberately not faked,
because faking access control is how private conversations leak.

Everything that makes messaging *feel* like messaging is still missing.

## What works today

| | |
|---|---|
| Persistence | `Conversations` + `Messages` tables, one thread per (listing, buyer) |
| Access control | Buyer, listing owner and admins only — enforced in `ConversationService` |
| Read state | `Message.ReadUtc`, stamped when the other participant opens the thread |
| Unread counts | Per-thread in the inbox, plus `GET /api/v1/conversations/unread-count` |
| Entry points | Listing page, seller offer inbox, offer note (auto-opens a thread) |
| Auto-close | Threads close when the listing is marked sold |

## What is deliberately not built

1. **Realtime delivery.** Messages appear on page load or refresh. There is no websocket, no
   SignalR hub, no long-poll. The React Native client should re-fetch on open and on
   foreground.
2. **Notifications.** Nobody is told a message arrived — no push, no email, no badge outside
   the app. In practice this is the single biggest gap: a seller who does not open the site
   will not know a buyer asked a question.
3. **Attachments.** Text only. Buyers asking for extra photos have to be sent to the listing.
4. **Moderation, blocking and reporting.** No way to report abuse, block a user, or review a
   thread. There is also no filtering of contact details, so users can and will move the
   conversation to WhatsApp immediately — which for a car marketplace may be fine, but it is
   a product decision nobody has made yet.
5. **Typing indicators, delivery receipts, editing, deletion.**
6. **Retention policy.** Messages are kept forever. GDPR erasure anonymizes the sender
   (`Message.SenderId` goes null via `ON DELETE SET NULL`) but does not remove message bodies,
   which may themselves contain personal data. Decide a retention window before launch.

## Order to build them in

1. Email notification of an unread message older than ~15 minutes. Cheapest fix for the
   biggest gap, and it reuses `IEmailSender`, which already exists.
2. Push notification, once the React Native app exists — the API already exposes the unread
   count a badge needs.
3. Report/block, before opening registration to the public.
4. Realtime (SignalR), last. It is the most visible and the least important: a car sale is
   not a chat app, and a 30-second delay costs nobody a sale.

## Where the code is

| | |
|---|---|
| Domain | `Services/Marketplace/ConversationService.cs` |
| Entities | `Models/Conversation.cs` |
| Website | `Controllers/MessagesController.cs`, `Views/Messages/` |
| API | `Controllers/Api/MessagesApiController.cs` (`/api/v1/conversations`) |
