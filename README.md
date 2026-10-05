# Sard backend

ASP.NET Core API for Sard (سرد, [sardnovels.com](https://www.sardnovels.com)), an Arabic web-novel platform. The
layout, commands and house rules are in [CLAUDE.md](CLAUDE.md).

## Configuration

Secrets come from host configuration (environment variables in production, `dotnet user-secrets` locally), never
from the `appsettings*.json` files. An environment variable `Section__Key` sets the setting `Section:Key`.

### Mobile app config: `GET /api/app/config`

The mobile apps read this at startup: to force an update when an API change breaks older builds, to suggest one
when a newer build is out, and to show a maintenance message. Each app reads its own section (`android`, `ios`). It is
anonymous and cacheable for five minutes (`Cache-Control: public, max-age=300`). `gifts` says what gifts accept (#31):
clients show the gift message box only when `gifts.messageMaxLength` is there (an older server has no `gifts`). `posts`
says what a new post accepts (#43, [Posts](#posts-writing-one-43)): the longest text in user-perceived characters, the
largest picture in bytes and the picture types. Those are the rules in the code (`Application/Posts/PostRules.cs`), not
settings: an `AppConfig:Posts` section changes nothing. A server from before #43 has no `posts`; the apps then use the
same numbers as their fallback.

```json
{
  "android": { "minVersion": "1.0.0", "latestVersion": "1.0.0" },
  "ios": { "minVersion": "1.0.0", "latestVersion": "1.0.0" },
  "maintenance": { "enabled": false, "messageAr": null },
  "gifts": { "messageMaxLength": 200 },
  "posts": { "contentMaxLength": 5000, "imageMaxBytes": 5242880, "imageTypes": ["image/jpeg", "image/png", "image/webp"] }
}
```

| Key | Default | Meaning |
|---|---|---|
| `AppConfig:Android:MinVersion` | `1.0.0` | The oldest app version that still works with the API; older builds must update. |
| `AppConfig:Android:LatestVersion` | `1.0.0` | The newest version on Google Play; older builds may offer an update. |
| `AppConfig:Ios:MinVersion` | `1.0.0` | The same for the iOS app (#25, ready before its first release). |
| `AppConfig:Ios:LatestVersion` | `1.0.0` | The newest version on the App Store. |
| `AppConfig:Maintenance:Enabled` | `false` | While `true`, the app shows the maintenance message. |
| `AppConfig:Maintenance:MessageAr` | `null` | The Arabic text of that message; empty or `null` lets the app use its own. |
| `AppConfig:Gifts:MessageMaxLength` | `200` | The longest gift message, in user-perceived characters (below). `POST /api/gift/send` checks messages against this same setting, so the apps' counters and the server agree. |

- Versions are `major.minor.patch`, digits only, and `MinVersion` can't be above `LatestVersion`, for each platform. A
  value that breaks either rule makes the endpoint answer 500 and log why, instead of telling every installed app
  something wrong. So does a `MessageMaxLength` outside 1 to 1000 (and gifts with a message fail with 500 until it is
  fixed; gifts without one still go).
- The defaults are in `Sareed-novels-backend/appsettings.json`. To change a value without a deploy, edit
  `appsettings.Production.json` on the host (it is re-read when it changes, so the next request sees it), or set an
  environment variable with `__` in place of `:`, such as `AppConfig__Maintenance__Enabled=true` (read when the app
  starts, so recycle the app pool). With the five-minute cache, apps see a change within five minutes.

### Push notifications (Firebase Cloud Messaging)

The mobile app receives push notifications through the FCM HTTP v1 API, authenticated with a Firebase service
account.

| Environment variable | Required | Value |
|---|---|---|
| `Fcm__ServiceAccountJson` | yes | The whole content of the service account's JSON key file, as is or base64-encoded. |
| `Fcm__ProjectId` | no | The Firebase project id. Defaults to `project_id` from the key. |

Getting the key: Firebase console → Project settings → Service accounts → Generate new private key. The Firebase
project must be the one the Android app's `google-services.json` comes from, and its "Firebase Cloud Messaging API
(V1)" must be enabled (the default for new projects).

Setting it on MonsterASP: Control panel → Websites → Manage website → Scripting → Environment Variables, then restart
the site. Base64 avoids trouble with the quotes and `\n` sequences in the JSON:

```bash
base64 -w0 service-account.json                                             # Linux
[Convert]::ToBase64String([IO.File]::ReadAllBytes("service-account.json"))   # PowerShell
```

At startup the API logs either `Push notifications are on (Firebase project ...)` or `Push notifications are
disabled: <reason>`. While push is disabled, the app can still register devices; pushes are recorded as skipped,
not sent. The key is a secret: never commit it or put it in `appsettings*.json`.

How delivery works: creating a notification also queues a push for each of the recipient's devices in the
`PushOutbox` table, in the same transaction, so pushes survive app-pool recycles. A background worker in the API
(`PushNotificationWorker`) sends them with bounded batches and parallelism, retries FCM 5xx/429 and network errors with
exponential backoff (honoring `Retry-After`) up to 8 attempts, removes device tokens FCM reports as unregistered or
invalid, and skips groups the user switched off (`/api/notifications/preferences`). Finished rows are deleted after
3 days.

Devices: the app registers its FCM token with `POST /api/notifications/devices` (signed in) and unregisters it on
sign-out with `DELETE /api/notifications/devices/{token}` (URL-encoded). The DELETE also works without a valid session
(signed out by a 401, or offline and sent later): then it removes the token whoever registered it, since the token is
a secret only that phone knows and removing it only stops pushes to it. With a valid session it removes only the
caller's own registration, so a pending unregister of a previous account's token goes without the `Authorization`
header. Always 204; both endpoints share a limit of 30 requests a minute per IP.

### Google Play point packs (Play Billing)

The Android app (`com.sardnovels.app`) sells point packs as Google Play consumable in-app products. The server
verifies every purchase with the Google Play Developer API, credits it once, consumes it on Google Play, and takes
the points back if Google later voids it (refund, chargeback). The website's manual recharge is unchanged.

| Environment variable | Required | Value |
|---|---|---|
| `PlayBilling__ServiceAccountJson` | yes | The whole content of the Google Cloud service account's JSON key file, as is or base64-encoded. A secret. |
| `PlayBilling__PackageName` | no | The app's package name. Default `com.sardnovels.app`. |
| `PlayBilling__Products__<productId>` | no | Points a product credits, e.g. `PlayBilling__Products__points_1000=1000`. `0` takes a product out. |
| `PlayBilling__AllowTestPurchases` | no | `true` credits purchases made by Play Console license testers (not charged). Default `false`. |

- The default catalog is in `Sareed-novels-backend/appsettings.json`: `points_500`→500, `points_1000`→1000,
  `points_2500`→2500, `points_5000`→5000. The app reads it from the API, so point amounts change without an app
  release (restart the site after changing them). Prices are never on the server: the app shows Google Play's.
- Only change the points of a product that is live in Play Console knowingly (they apply to purchases verified from
  then on), and never remove one that is still active there: a purchase of an unknown product is refused and Google
  refunds it after three days. Deactivate it in Play Console first, wait a few days, then remove it here.
- `AllowTestPurchases=true` gives license testers real points for free. Turn it on only while testing, then off.
- Setting them on MonsterASP: Control panel → Websites → Manage website → Scripting → Environment Variables, then
  restart the site. Base64 avoids trouble with the quotes and `\n` sequences in the key:

  ```bash
  base64 -w0 play-service-account.json                                             # Linux
  [Convert]::ToBase64String([IO.File]::ReadAllBytes("play-service-account.json"))   # PowerShell
  ```

- At startup the API logs either `Google Play billing is on for com.sardnovels.app: points_500=500, ...` or
  `Google Play billing is disabled: <reason>`. While it is disabled, `GET /api/wallet/play-products` answers
  `"enabled": false` with no products, and purchases answer 503 `BillingUnavailable`.

#### Setting it up (owner)

1. **Products.** Play Console → the Sard app → Monetize with Play → Products → One-time products (called "In-app
   products" in older consoles) → Create one for each pack: product id `points_500`, `points_1000`, `points_2500`,
   `points_5000` (the ids must match the catalog exactly), an Arabic name and description, a price per country, then
   activate it. Leave the default (backwards-compatible) purchase option, and leave multi-quantity purchases off: the
   server refuses a purchase of more than one pack. Google keeps 15% of the first $1M a year, so price the packs with
   that in mind. Products can only be created once a build with Play Billing has been uploaded (internal testing is
   enough).
2. **Service account.** In the [Google Cloud console](https://console.cloud.google.com/), in any project you own:
   APIs & Services → Library → "Google Play Android Developer API" → Enable. Then IAM & Admin → Service Accounts →
   Create service account (e.g. `sard-play-billing`; it needs no Cloud roles) → open it → Keys → Add key → Create new
   key → JSON. Keep the downloaded file secret; delete it once it is in the environment variable.
3. **Access to the app.** Play Console → Users and permissions → Invite new users → the service account's email
   (`...@...iam.gserviceaccount.com`) → App permissions → Add app → Sard, with **View app information (read-only)**,
   **View financial data, orders, and cancellation survey responses** (to verify purchases and read voided ones) and
   **Manage orders and subscriptions** (to consume them) → Apply → Invite user (a service account doesn't have to
   accept). New permissions can take up to a day or two to reach the API; until then Google answers 401/403 and
   purchases answer `BillingUnavailable` (logged as an error).
4. **Environment variables** as above, restart, and check the startup log line.
5. **Test** with a license tester (Play Console → Settings → License testing) and `AllowTestPurchases=true`, then set
   it back to `false`.

#### The mobile app's side

Everything needs the user's `Authorization: Bearer <token>`. The user always comes from the token, never the body.

`GET /api/wallet/play-products`

```json
{
  "enabled": true,
  "message": null,
  "obfuscatedAccountId": "5f1c...64 lowercase hex characters",
  "products": [ { "productId": "points_500", "points": 500 }, { "productId": "points_1000", "points": 1000 } ]
}
```

With `"enabled": false` (and an Arabic `message`) the app must not offer any pack. Prices come from Google Play
(`ProductDetails.price`), never from the API.

Buying (Flutter `in_app_purchase`, Android):

1. Query the product ids from the catalog with `InAppPurchase.instance.queryProductDetails`.
2. Buy with `buyConsumable(purchaseParam: PurchaseParam(productDetails: p, applicationUserName: obfuscatedAccountId),
   autoConsume: false)`. `applicationUserName` is what Play records as the purchase's `obfuscatedAccountId`; the
   server refuses a purchase whose account id isn't the caller's. The value is the catalog's `obfuscatedAccountId`:
   the lowercase hex SHA-256 of the UTF-8 bytes of the user's id (the token's `nameidentifier` claim), 64 characters.
3. When the purchase stream reports `PurchaseStatus.purchased` (or `restored`), send

   `POST /api/wallet/play-purchase` with `{ "productId": "points_1000", "purchaseToken": "<purchase.verificationData.serverVerificationData>", "orderId": "<purchase.purchaseID>" }`

   and on 200 show `{ "pointsAdded": 1000, "currentBalance": 1450 }`.
4. **Never consume or acknowledge in the app**: no `autoConsume`, no `consumePurchase`, and no `completePurchase` on
   Android (it acknowledges). The server consumes right after crediting. If the app acknowledged or consumed a
   purchase that then never reached the server, Google would keep the money while the user has no points; left
   alone, a purchase the server never credited is refunded by Google after three days.
5. Sending the same purchase again is always safe: the same user gets the same `pointsAdded` (and the current
   balance), and it is never credited twice. So retry on network errors and 503, and on every app start call
   `restorePurchases()` and send any point pack it reports: those are purchases the server hasn't consumed yet.

Errors are `{ "code": "...", "message": "<Arabic, for the user>" }`. Act on `code`, show `message`:

| HTTP | `code` | Meaning | The app |
|---|---|---|---|
| 400 | `InvalidRequest` | `productId` or `purchaseToken` missing or malformed | bug: report it |
| 400 | `UnknownProduct` | not a pack in the catalog | refresh the catalog |
| 400 | `PurchaseNotFound` | Google doesn't know this token for this app and product | stop retrying |
| 400 | `ProductMismatch` | the token is for another product | stop retrying |
| 400 | `UnsupportedQuantity` | more than one pack in one purchase | stop; Google refunds it |
| 403 | `AccountMismatch` | the purchase's `obfuscatedAccountId` isn't this user's (or is missing) | stop retrying |
| 403 | `TestPurchaseNotAllowed` | a license-tester purchase while those are off | stop retrying |
| 409 | `PurchasePending` | payment not complete yet (Play's pending state) | send again once it is `purchased` |
| 409 | `PurchaseCanceled` | canceled | stop retrying |
| 409 | `PurchaseVoided` | refunded or voided by Google | stop retrying |
| 409 | `AlreadyUsedByAnotherUser` | this purchase was credited to another account | stop retrying |
| 503 | `BillingUnavailable` | the server isn't set up for Play Billing (or Google refuses its key) | keep the purchase, retry later |
| 503 | `VerificationUnavailable` | Google Play couldn't be reached | keep the purchase, retry later |

#### Refunds and voided purchases

When Google voids a purchase (refund, chargeback, revocation), its points are taken back even if that takes the
balance below zero (ledger type `PlayRefund`). A negative balance blocks all spending (gifts, early-access
subscriptions, withdrawals) until purchases or recharges bring it back up. What the balance can't cover was given away:
it is taken back from the earnings still on hold that the buyer paid for, and from whom those authors passed the points
on to (the clawback in "Wallet: what can be withdrawn" below). A purchase Google voids before anyone claims it is
recorded so it can never be credited.

#### How it works

- `PlayPurchases` has one row per purchase token (unique index). The row, the wallet credit (one SQL `UPDATE`) and the
  `PlayPurchase` ledger entry commit together, so concurrent or repeated requests credit a token once.
- Consuming: right after crediting. If that fails, the purchase stays credited and the background worker
  (`PlayBillingWorker`, every 5 minutes) retries with backoff (5 minutes, doubling, at most hourly), from a row that
  survives restarts. A failure it can explain is settled: already consumed counts as done, canceled takes the points
  back. From two days on every failure is logged as an error, since Google refunds an unconsumed purchase after three.
- Voids: the worker reads Google's Voided Purchases API every hour from a cursor stored in `PlaySyncCursors` (with a
  day of overlap; Google keeps 30 days) and applies each once. Real-time developer notifications (Pub/Sub) aren't
  needed; if they are added later, their voided-purchase notification calls the same `ApplyVoidAsync`.
- The app pool can idle or recycle on shared hosting: the worker picks up where it left off at the next start.

### Wallet: what can be withdrawn

Bought points have no cash value (the terms: «النقاط لا قيمة نقدية لها»). Only what an author earns from readers can be
paid out, and only once a refund can no longer take it back (#22, #27).

Each user's points are in three pools, worked out from their ledger (`PointTransactions`) read in time order
(`WalletPools.Fold`). They have no columns of their own: the ledger is the only record, and the wallet page, a
withdrawal request, its approval, the admin list and a refund all read the same rows.

| Pool | What goes in | Withdrawable |
|---|---|---|
| Bought | website top-ups (`RechargeApproved`), Google Play packs (`PlayPurchase`), points a clawback gives back to whom paid (the positive `EarningReversed`), and a balance from before the ledger (the wallet balance less the ledger's sum) | never |
| Held | each `GiftReceived` and `PrivilegeRevenue`, as a lot of its own, until its `AvailableAt` (its `CreatedAt` plus the hold) | not yet |
| Released | held lots whose `AvailableAt` has passed | yes |

1. **Spending** (gifts, early-access subscriptions) comes out of Bought, then Held (the lot released last first, so
   older earnings keep their release date), then Released. So does every other debit: a Play refund (`PlayRefund`), a
   forfeited balance. What they can't cover is owed (the balance is below zero, which blocks all spending), and new
   credits, earnings included, pay what is owed before they count anywhere else.
2. **Taking an earning back** (the author's negative `EarningReversed`) takes that very lot while it is held; what the
   author spent of it comes out like any debit.
3. **An approved withdrawal** (`WithdrawalApproved`) comes out of Released. One approved before earnings were held (#22)
   may be larger: the rest comes out of Bought, then Held, so it doesn't count against later earnings.
4. **Withdrawable** = max(0, min(Released, balance) − pending withdrawal requests); what an approval can pay,
   **Payable**, is max(0, min(Released, balance)). Pending requests aren't deducted from the balance until approval, so
   they are reserved: two requests can't use the same points. A request is checked against Withdrawable when made, and
   approval checks Payable again; both read the ledger while holding the wallet's row lock, from the check to the insert
   or the debit. So points an author spent never stay withdrawable: a gift received or a pack bought after spending
   earnings is held or bought like any other, and a top-up gifted back and forth between two accounts is withdrawable
   once at most.
5. **Refund clawback.** A refund that takes the buyer below zero means they gave away points they never paid for. The
   deficit (how far below zero this refund went) is taken back from every earning still on hold that the buyer paid for
   with gifts and early-access subscriptions, whenever it was paid (the hold bounds how far back), newest payment first.
   When taking one back takes its author below zero, the author passed those points on: their own payments are taken
   back the same way, for how far below zero this refund took them, at most three accounts past the buyer's own
   payments; each account's payments are walked once, which ends cycles. Earnings of deleted accounts are left alone
   (their balance was forfeited already), and so are earnings that are released or about to be (within a second). Each
   reversal is two `EarningReversed` rows, both with `RelatedRequestId` = the voided purchase and
   `ReversedTransactionId` = the earning row: negative on the author (whose balance may go below zero) and positive on
   whom paid for it. Both rows of a gift or subscription share `RelatedRequestId` (the gift or subscription record),
   which is how a payment finds its earning; payments from before #22 have none, but their earnings are all released.
6. **Locks.** A refund works out without locks which wallets it and its clawback change, then locks all of them and
   the buyer's in one pass, in the order a gift locks its two (user id), then works it out again under the locks and
   writes it. If the ledger changed in between so that it needs another wallet, it rolls back and starts again with
   that wallet locked too (at most 3 attempts; then it logs the error and the next hourly poll tries again). A gift or
   an early-access subscription that SQL Server picks as a deadlock victim runs once more in a new transaction, and is
   charged once.
7. **Existing data**: the migration `EarningsHoldAndReversal` released every earning row written before it at once
   (`AvailableAt` = `CreatedAt`); the hold applies to earnings from then on. A balance with no ledger rows behind it
   (production's ledger started empty) counts as bought. The database refuses an earning row without `AvailableAt` (a
   check constraint).

| Environment variable | Required | Value |
|---|---|---|
| `Wallet__EarningsHoldDays` | no | Days an earning is held before it can be withdrawn. Default `30`. Below 30 it is raised to 30 in Production and kept elsewhere (tests use short holds), with a warning in the startup log either way: the clawback only takes back earnings still on hold, and Google lists voided purchases for 30 days. A change applies to earnings credited from then on. A value outside 0 to 365 stops the API at startup. |

API (additive):

- `GET /api/wallet` also returns `withdrawable` (what a request can ask for now), `pendingEarnings` (earnings still on
  hold, less what was spent or taken back of them) and `nextReleaseAt` (UTC, when the next of those is released; `null`
  when none is on hold). `currentBalance` means what it always did.
- `POST /api/wallet/withdraw` for more than `withdrawable`: 400 `{ "code": "InsufficientWithdrawableBalance",
  "message": "<Arabic>" }`, where the message says what can be withdrawn now, when more is released and the rule, e.g.
  «يمكنك سحب 1200 نقطة فقط الآن، وتصبح أرباحك التالية قابلة للسحب خلال 5 أيام. تُسحب أرباح الهدايا واشتراكات الوصول
  المبكر وحدها، بعد 30 يومًا من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب.» `BelowMinimumWithdrawal` is checked
  first, as before; `InsufficientBalance` is no longer returned by this endpoint.
- `DELETE /api/wallet/withdraw/{id}` (#27): the member cancels their own pending request, which frees its points (none
  were deducted). 204, also when they cancelled it already (a retry is done). The request becomes `Rejected` with
  `rejectionReason` «ألغاه صاحب الطلب», as the requests an account deletion cancels, so apps that don't know about
  cancelling show it as they already show those; each request in `GET /api/wallet/withdraw` also has
  `cancelledByOwner` (`true` for these, `false` otherwise), to show it as cancelled («ملغى») rather than refused.
  Errors are `{ "code", "message" }`:

  | HTTP | `code` | When | `message` |
  |---|---|---|---|
  | 401 | | not signed in | |
  | 404 | `RequestNotFound` | an unknown id, or another member's request | «طلب السحب غير موجود» |
  | 409 | `AlreadyProcessed` | an admin approved it | «قُبل طلب السحب هذا من قبل، فلا يمكن إلغاؤه.» |
  | 409 | `AlreadyProcessed` | an admin rejected it | «رُفض طلب السحب هذا من قبل، فلا يمكن إلغاؤه.» |

- `PATCH /api/admin/withdraw/{id}/approve` refuses a request the member can no longer be paid with the same code (the
  message tells the admin what can be paid now), and the request stays pending: reject it with an Arabic reason, or
  the member can cancel it. Approving or rejecting a request the member cancelled answers `AlreadyProcessed` «ألغى
  صاحبه هذا الطلب من قبل».
- `GET /api/admin/withdraw/pending`: each request also has `requesterWithdrawable` (Payable: what approving can pay the
  requester now, before their pending requests are taken out) and `recentEarningReversals` (their `EarningReversed`
  rows of the last 90 days, at most 10: `id`, `amount`, `description`, `createdAt`, `purchaseId`,
  `reversedTransactionId`, `novelId`). The member's own `GET /api/wallet/withdraw` doesn't have these fields.
- The wallet history (`GET /api/wallet/transactions`) has the type `EarningReversed`, with an Arabic `description`
  («أُلغيت أرباح 500 نقطة لأن عملية الشراء التي جاءت منها استُرد مبلغها» on the author, «استُرجعت 500 نقطة من أرباح
  الكاتب وأُعيدت إلى رصيدك، ...» on whom paid); apps need a label for it.

Account deletion is unchanged: the whole balance, held earnings included, is forfeited, and pending withdrawals are
cancelled.

### Gift messages (#31)

A reader may write the author a short message with a gift. **It is public**: it shows under the novel's recent gifts.

**Sending.** `POST /api/gift/send` takes an optional `message` (string) with `giftId`, `novelId` and `count`. It is
trimmed; empty or whitespace-only is no message. The limit is `AppConfig:Gifts:MessageMaxLength` (200), counted in
user-perceived characters, .NET's `new StringInfo(text).LengthInTextElements`, as Flutter's counter and the web's
`Intl.Segmenter` count: an emoji, a flag or a letter with its tashkeel is one. The column is `nvarchar(4000)`
(`GiftTransactions.Message`, migration `AddGiftMessage`), room for 200 of any emoji (a family emoji is 11 UTF-16
units); text within the limit but longer than that (letters under hundreds of combining marks) is refused as too long.
Both refusals are checked before any payment, so **nothing is charged**:

| HTTP | `code` | When | `message` |
|---|---|---|---|
| 400 | `GiftMessageTooLong` | over the limit | «الرسالة طويلة: الحد الأقصى 200 حرف.» (the configured number) |
| 403 | `Blocked` | a message, and the novel's author blocked the sender (`IsBlockedAsync(authorId, senderId)`; only that way, while comments on posts are refused either way since #52) | «لا يمكنك إرسال رسالة إلى هذا الكاتب.» |

The 400 has the endpoint's other refusals' shape, `{ "success": false, "code", "message" }`; the 403 is `{ "code",
"message" }`. A gift without a message is sent as before, blocked or not. A suspended member can't send anything (their
sessions are refused: 401), as with comments.

**Where it comes back** (always the stored text; `null` when there is none):

| Where | Field |
|---|---|
| `GET /api/gift/novel/{novelId}` (public) | `message` on each item, anonymous callers included; `null` for a signed-in viewer who blocked the sender or whom the sender blocked (either way; the gift stays listed) |
| `GET /api/notifications`, `GiftReceived` | `giftMessage` and `giftTransactionId` (the gift record's id, the target to report the message), next to `giftId`, `giftNameAr`, `giftCount`; `message` (the sentence) is unchanged. Read from the record, so a moderator's removal shows here too |
| Push for `GiftReceived` | title «هدية جديدة»; body the sentence, then a new line with «the message», cut at 100 characters (whole ones: an emoji is never split) with «…», and at most 300 UTF-16 units. Read from the record when the push is sent. The data keys are unchanged |
| `GET /api/gift/my-history` | `message` on each item (the sender's own) |

Top supporters and the leaderboards carry no messages.

**Moderation.** `POST /api/reports` with `targetType` `GiftMessage` and `targetId` the gift record's id (the item's `id`
in `novel/{novelId}` and `my-history`, the notification's `giftTransactionId`). Any signed-in member can report it but
its sender (400 `CannotReportOwnContent`); a gift without a message, or whose message was removed, is 404
`TargetNotFound`. The report keeps the sender as owner and the message as excerpt; the admin list links it to
`/novel/{slug}`. `RemoveContent` sets the message to null and keeps the gift, its payment and the author's earning;
`SuspendUser` suspends the sender. Account deletion treats the message like the sender's comments: it stays, under
«مستخدم محذوف» (open reports about it close as `AccountDeleted`).

### Editing a review (#34)

The author of a review edits it in place: it keeps its `id`, likes and `createdAt`. Before, fixing a typo or a score
meant deleting the review and writing it again, which lost its likes.

`PATCH /api/{novelId}/reviews/{reviewId}` (the review's own route, as `.../reviews/{reviewId}/like`), signed in, with a
JSON body whose fields are all optional:

```json
{ "writingQualityScore": 4, "updatingStabilityScore": 5, "characterDevelopmentScore": 4, "worldBuildingScore": 3,
  "content": "نص المراجعة", "isSpoiler": false }
```

- A field left out, or `null`, stays as it is. A field sent is checked as when writing a review, with the same
  messages: each score from 1 to 5, the text 5 to 2000 characters. `"content": ""` (or only spaces) removes the text;
  the review then has `content: null`, like one written without text.
- 200 answers the review itself, with no `{ success, message }` around it: exactly the item `GET /api/{novelId}`
  lists in `reviews`, the same fields (`reviewer`, `id`, `totalAverageScore`, `content`, `isSpoiler`, `likeCount`,
  `isLikedByCurrentUser`, `createdAt`, `updatedAt`) and values, to replace it in place: the same `id`, `likeCount`
  and `createdAt`, the new `content`, `isSpoiler` and `totalAverageScore`, and `updatedAt`. (Writing a review,
  `POST /api/{novelId}`, still answers `{ success, message, review }`.)
- `updatedAt` is new on every review: in `GET /api/{novelId}` (the `reviews` items and `currentUserReview`, which has
  the four scores to fill the form in) and in the answer of `POST /api/{novelId}`. It is when its author last edited
  it, in the format of `createdAt` (UTC, no `Z`), and `null` for a review never edited (every review from before
  this). Show «(معدّلة)» when it isn't `null`.
- Saving without a change (the form as it was, or `{}`) writes nothing: 200 with the review as it is, `updatedAt`
  unchanged, so it isn't marked edited.
- A changed score recomputes the review's own `totalAverageScore` and the novel's averages (novel page, novel lists,
  search), with the same recount as writing and deleting a review do; `reviewCount` stays. Rankings read the new
  score on their next run, as they read a new review.

Errors are `{ "code", "message" }`:

| HTTP | `code` | When | `message` |
|---|---|---|---|
| 400 | `ValidationFailed` | a field sent breaks a rule of writing a review; `errors` has it under the field's name, e.g. `WritingQualityScore` | the rule's, e.g. «تقييم جودة الكتابة يجب أن يكون من 1 إلى 5» |
| 401 | | not signed in | |
| 403 | `NotOwner` | another member's review, the novel's author included | «يمكنك تعديل مراجعاتك فقط» |
| 404 | `ReviewNotFound` | no such review (deleted, for instance), or it is about another novel than `{novelId}` | «المراجعة غير موجودة» |

The migration `AddReviewUpdatedAt` adds the nullable column `Reviews.UpdatedAt`; existing reviews have null.

### Account deletion: `DELETE /api/User/me`

Both stores require in-app account deletion; the web has the same at `https://www.sardnovels.com/delete-account`, the
URL Google Play lists. The member confirms it's them again, with the `Authorization: Bearer <token>` of their session:

| Body | For |
|---|---|
| `{ "password": "..." }` | accounts with a password (`GET /api/User/my-profile` says `"hasPassword": true`) |
| `{ "googleIdToken": "..." }` | an ID token from signing in with Google just now, whose subject is the account's Google sign-in: any account with one, and the way for accounts without a password |
| none (or `{}`) | only an account without a password whose access token is from a sign-in in the last 10 minutes (the web has the member sign in with Google again, then confirm) |

Errors are `{ "code": "...", "message": "<Arabic, for the user>" }`:

| HTTP | `code` | Meaning | The app |
|---|---|---|---|
| 204 | | deleted | sign out (below) |
| 401 | | not signed in, or the account is deleted already (its tokens are refused) | sign out |
| 403 | `ReauthenticationFailed` | wrong password, an invalid Google token, or another Google account | show `message`, let them try again |
| 403 | `ReauthenticationRequired` | nothing sent where a password is needed, or the sign-in isn't recent enough | ask for the password, or a fresh Google sign-in |
| 403 | `AdminCannotDeleteAccount` | an admin account (the team removes those) | show `message` |
| 429 | `TooManyDeletionAttempts` | 5 attempts an hour per account | show `message` |

After 204 every token of the account is refused: delete the stored token, clear what the app cached about the user and
show the signed-out app (the server already removed the account's push devices, so there's nothing to unregister). If
the response was lost and the retry gets 401, the deletion went through. Suspended members can't sign in, so they
email `support@sardnovels.com` instead, and an admin deletes the account for them (`OwnerRequest`, below).

What deletion does (`IAccountDeletionService`, one transaction): the user row stays, anonymized, so comments, reviews,
posts and gift messages keep an author shown as «مستخدم محذوف» (user name `deleted-<id>`); email, phone, bio, links, photo and banner
(also from storage, after the commit), password, external sign-ins and old user names are removed. Their novels are
soft-deleted. Library, reading lists (with others' follows of them), follows, notifications to them, likes (counters
adjusted), privilege subscriptions, devices, preferences and blocks are deleted; notifications they caused lose their
name and photo. Open reports about them close as `AccountDeleted`. The wallet balance is forfeited (set to zero, ledger
type `BalanceForfeited`) and pending withdrawals are cancelled; the ledger and Play purchases stay. Deleted accounts
have no profile (404) and are left out of search, supporters and follower lists.

The `deleted-` user name prefix is reserved for them (`UserNameRules.LooksDeleted`, ignoring case), so a client can tell
a deleted author by `userName` alone and hide the profile link, report and block: sign-up, Google sign-up and renames
to such a name are refused with code `ReservedUserName` (register: 400 `result.code`; update-me: 400 `code`).

### Admin: deleting a member's account: `DELETE /api/admin/users/{userId}`

For when Sard must delete an account the member doesn't delete themselves: the terms say Sard is for 13 and older and
that an account is deleted once we learn it belongs to someone under 13 (`Underage`); enforcing the rules when
suspending isn't enough (`PolicyViolation`); or a member who asks by email and can't do it in the app, a suspended one
for instance (`OwnerRequest`, once you have checked the email comes from the account's own address). The other admin
endpoints (reports, suspensions, wallet requests) are under `/api/admin` too; all need an admin's token.

```bash
curl -X DELETE https://api-sareed.runasp.net/api/admin/users/<userId> \
  -H "Authorization: Bearer <admin token>" -H "Content-Type: application/json" \
  -d '{ "reason": "Underage", "note": "optional, at most 500 characters" }'
```

- `reason`: `Underage`, `PolicyViolation` or `OwnerRequest` (by name, any case; numbers are refused). `note` is optional:
  the admin's own words for the record, such as where the request came from (a support email's date, a report id).
  Never put the member's personal data in it (name, email, age, documents): it is kept after everything else about
  them is gone.
- There is no re-authentication and no undo. The result is exactly the member's own deletion (above): anonymized as
  «مستخدم محذوف», novels hidden, private data deleted, balance forfeited, pending withdrawals cancelled, open reports
  about them closed as `AccountDeleted`, every session ended, push devices and images removed.
- 200 answers what it did. `filesNotDeleted` above 0 means an image stayed in storage (the error log names its key):

```json
{ "userId": "5f1c...", "reason": "Underage", "deletedAt": "2026-09-28T12:00:00.1234567Z", "novelsHidden": 2,
  "forfeitedBalance": 500, "withdrawalsCancelled": 1, "reportsClosed": 2, "filesDeleted": 2, "filesNotDeleted": 0 }
```

| HTTP | `code` | Meaning |
|---|---|---|
| 400 | `ValidationFailed` | no body, a missing or unknown `reason`, or a `note` over 500 characters (`message` says which) |
| 401 | | not signed in |
| 403 | | not an admin (the role check, before the endpoint runs) |
| 403 | `CannotDeleteAdmin` | an admin's account, yours included: its admin role has to be removed first |
| 404 | `UserNotFound` | no user with that id |
| 409 | `AlreadyDeleted` | deleted already, by the member or by an earlier request whose response was lost |

The record: each deletion writes a row to the `AdminAuditLogs` table in the deletion's own transaction, so there is a
row exactly when an account was deleted: the admin's id, the action (`DeleteAccount`), the member's user id, the reason,
the note and the time (the member's `DeletedAt`). It holds ids and the admin's note only, nothing copied from the
account. The API also logs `Admin {AdminId} deleted the account of user {UserId} ({Reason})` at Information level.
There is no endpoint to read the table yet (query it in the database); it is meant for later admin actions too. The
reports the deletion closes show no admin in `resolvedById`, as after a member's own deletion; the audit row says who.

### Setting a password for an account made with Google: `POST /api/User/set-password` (#53)

An account made with Google has no password (`GET /api/User/my-profile` says `"hasPassword": false`). This sets its
first one from the app; before, the reset-password email was the only way.

```bash
curl -X POST https://api-sareed.runasp.net/api/User/set-password \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{ "newPassword": "...", "googleIdToken": "optional" }'
```

- `newPassword`: the rules of update-password and reset-password (`PasswordRules`): required, at least 8 characters;
  then ASP.NET Identity's password options, as those endpoints apply them (today at least 6 characters and nothing
  else, so they refuse nothing more).
- `googleIdToken`, optional: an ID token from signing in with Google just now, whose subject is the account's Google
  sign-in, as for `DELETE /api/User/me`. Without it, the access token must come from a sign-in in the last 10 minutes.
  When it is sent it must hold, even within those 10 minutes.
- **204**, no body: done. `hasPassword` is `true` from then on, and the member signs in with their email address (or
  user name) and this password, or with Google as before. Every session stays signed in, with its push devices.

Checked in this order; the first refusal is the answer, `{ "code", "message" }` with an Arabic message:

| Order | HTTP | `code` | When | `message` |
|---|---|---|---|---|
| 1 | 429 | `TooManyRequests` | over 10 requests a minute from one address (the sign-in limit), signed in or not | «طلبات كثيرة خلال وقت قصير. حاول مرة أخرى بعد بضع دقائق.» |
| 2 | 401 | | not signed in, or the token is refused; no body | |
| 3 | 400 | `PasswordAlreadySet` | the account has a password: update-password changes it | «لحسابك كلمة مرور بالفعل، غيّرها من «تغيير كلمة المرور».» |
| 4 | 400 | `ValidationFailed` | `newPassword` left out or null | «اكتب كلمة المرور الجديدة» |
| 4 | 400 | `ValidationFailed` | under 8 characters, empty included | «يجب أن تحتوي كلمة المرور الجديدة على 8 أحرف على الأقل» |
| 4 | 400 | Identity's (`PasswordRequiresDigit`...) | Identity's password options refuse it (none do today) | Identity's Arabic description; the body adds `succeeded: false` and every problem in `errors: [{ "code", "description" }]`, as update-password's does |
| 5 | 403 | `ReauthenticationRequired` | no `googleIdToken`, and the sign-in behind the token is over 10 minutes old | «لتعيين كلمة مرور لحسابك سجّل الدخول بحساب Google مرة أخرى، ثم عيّنها خلال 10 دقائق» |
| 5 | 403 | `ReauthenticationFailed` | `googleIdToken` isn't a valid Google ID token for Sard | «تعذّر التحقق من حساب Google، سجّل الدخول به مرة أخرى» |
| 5 | 403 | `ReauthenticationFailed` | `googleIdToken` belongs to another Google account | «حساب Google هذا غير مرتبط بحسابك في سرد» |

A body that isn't JSON, or has a value of the wrong type, is refused by ASP.NET before step 3, as everywhere: 400
`ValidationFailed`.

- **Why this order.** An account that has a password is told so whatever it sent. The rules come before the proof, so
  nobody signs in with Google again only to learn the password is too short.
- **The same rules and messages as update-password.** For a password the rules refuse, `code` and `message` are
  exactly what update-password answers for the same new password. Only the body around them differs for the
  8-character rule: update-password's is ASP.NET's validation problem (its `errors` list the problems by field),
  set-password's is `{ code, message }`, because the handler checks that rule itself, after `PasswordAlreadySet`.
- **403, not 401,** for a proof that fails or is missing, as for `DELETE /api/User/me`: a 401 to a request that carried
  a token tells the app its session ended, and it signs out.
- **The app.** Offer the form when `hasPassword` is false. Right after a Google sign-in, send `{ newPassword }`;
  otherwise sign in with Google again and send its ID token with it (or do that on `ReauthenticationRequired` and send
  again). On `PasswordAlreadySet`, read my-profile again and offer change password instead.
- **Two requests at once** (a double tap): one gets 204, the other `PasswordAlreadySet`.
- **What is saved.** One SQL statement, only while the account has no password and isn't deleted: the password hash, a
  new security stamp and a new concurrency stamp, nothing else of the account (UserManager's `AddPasswordAsync` would
  write every column from the copy it read, undoing a counter or a suspension changed meanwhile). The new concurrency
  stamp makes an update-me saving at that moment fail instead of writing the account back without its password.
- **The security stamp** isn't checked on API requests: access tokens don't carry it, and there are no refresh tokens
  (an access token lives 60 days and is checked against the sign-out-everywhere cut-off, suspension and deletion,
  none of which this changes). It only makes Identity's emailed links valid, so a reset-password or email
  confirmation link sent before the password was set stops working (`InvalidToken`), as after any password change.
- **The web** doesn't use it yet: its settings page always offers «تغيير كلمة المرور», which asks for the current
  password, so an account without one still sets it through the reset-password email.

### Reading lists: editing, «أضف إلى قائمة» and order (#35)

For the app's list forms and its «أضف إلى قائمة» sheet. Every call needs the member's token (401 without); errors have
a `code` and an Arabic `message`.

**Editing: `PATCH /api/readinglist/{id}`** (multipart form-data, the owner only). A field left out stays as it is:

| Field | |
|---|---|
| `Name` | 1 to 100 characters; empty is ignored (the name stays) |
| `Description` | at most 1000 characters; `""` (or blank) clears it, and it comes back `null` |
| `IsPublic` | `true` or `false` |
| `CoverImage` | a new picture: JPEG, PNG or WebP, at most 5 MB |
| `RemoveCover` | `true` removes the picture, and `coverImageUrl` comes back `null` |

200 is `{ "success": true, "message": "حُفظت التغييرات" }` without the list: refetch it. Removing the picture unlinks
it; the file stays in storage, as the old one does when a picture is replaced. The web sends every text field of its
form, so emptying the description there now clears it too.

| HTTP | `code` | When |
|---|---|---|
| 400 | `CoverConflict` | a `CoverImage` and `RemoveCover=true` together: «لا يمكن رفع صورة جديدة وإزالة الصورة في الطلب نفسه»; nothing changes |
| 400 | `ValidationFailed` | a name over 100 or a description over 1000 characters, or a picture of another type or over 5 MB (edits weren't checked before: the first two were a 500, and any file was stored) |
| 400 | `DuplicateListName` | the member has another list with that name |
| 400 | `UploadFailed` | storing the picture failed; try again |
| 403 | `NotOwner` | someone else's list |
| 404 | `ReadingListNotFound` | no such list |

`CoverConflict`, `DuplicateListName` and `UploadFailed` are `{ "success": false, "code", "message" }`;
`ValidationFailed` is the validation problem (`code`, `message`, `errors` by field).

**Which of my lists have a novel: `GET /api/readinglist/my-lists?containsNovelId={novelId}`.** Each list then also has
`containsNovel` (bool): whether the novel is on it, whatever the novel's state now (one made a draft since still counts,
and removing it from there works). Only with the parameter: without it the field is absent, as on the other list
pages and in the list `POST /api/readinglist` returns. Paging (`pageNumber`, `pageSize` up to 100) is unchanged. The
sheet shows a check where it is true; tapping a checked list sends `DELETE /api/readinglist/{id}/novels/{novelId}`, an
unchecked one `POST /api/readinglist/{id}/novels/{novelId}` (adding twice still answers 400 `AlreadyInList`). An
unknown novel is on no list; a value that isn't an id is 400 `ValidationFailed`.

**Order: `PATCH /api/readinglist/{id}/novels/order`** (the owner only), with a JSON body:

```json
{ "orderedNovelIds": ["<novelId>", "<novelId>", "..."] }
```

Send every novel of the list's `novels` (`GET /api/readinglist/{id}`), each once, in the new order. It is saved as
their `orderIndex` (0, 1, 2...), which the list's page and its cards' `previewNovels` follow; the list's `updatedAt`
changes when the order did. 200 is `{ "success": true, "message": "حُفظ ترتيب الروايات" }`, also when nothing moved.

| HTTP | `code` | When |
|---|---|---|
| 400 | `NovelOrderMismatch` | the ids aren't exactly the list's novels: one missing (added elsewhere since), extra (removed since, or never on it) or repeated. Nothing changes; refetch the list and let the member try again. «الترتيب المرسل لا يطابق روايات القائمة: يجب أن يضم كل رواية فيها مرة واحدة، ولا شيء غيرها. حدّث القائمة وحاول مرة أخرى.» |
| 400 | `ValidationFailed` | no `orderedNovelIds`, or an empty body |
| 403 | `NotOwner` | someone else's list |
| 404 | `ReadingListNotFound` | no such list |

The list's novels are the ones readers can open. A novel still on the list but hidden (a draft, or deleted) isn't in
`novels`, isn't sent, and keeps its place: it reappears where it was if it is published again.

### Reading, social and account API notes for the apps (#25)

The small fixes the Android app asked for. Everything is additive unless it says otherwise; errors are
`{ "code", "message" }` with an Arabic message, as everywhere.

**Offline reading.** Every unlocked `GET /api/novel/{novelId}/chapter/{id}` counts a read (once per visitor per day). A
download for offline reading adds `?prefetch=true` (or `1`), or the header `X-Sard-Prefetch: 1`: the same answer, no read
counted. When the reader opens a downloaded chapter, the app sends `POST /api/novel/{novelId}/chapter/{id}/view` (queued
while offline): it counts one read with the reader's rules, the same visitor key (the signed-in user, or the device)
and once a day, and nothing for the novel's author, a chapter locked for the caller or a crawler. It answers 204
whether or not this call counted (so a repeat is harmless), 404 `NovelNotFound`/`ChapterNotFound` for a chapter readers
can't open, and 500 when the count failed, so keep it queued and send it again. It works signed out too.

**Blocks and reading lists.** To a member the list's owner blocked, the list doesn't exist: `GET /api/readinglist/{id}`
and following it answer 404 `ReadingListNotFound`, what a list nobody has answers, and it drops out of their
`GET /api/readinglist/followed` (their follow stays for an unblock). The blocker still opens the blocked member's lists.

**Chapter counts.** A novel's `chapterCount` (novel page, lists, my works) counts published chapters only, the chapters
the list shows. The migration `RecountPublishedChaptersAndNotificationGifts` recounted every novel.

**Reviews by page.** Every sorting of `GET /api/{novelId}` is a total order, so pages never repeat or skip a review:
`likes` (and any unknown sorting) by likes, then newest, then id; `newest`/`oldest` by date, then id.

**Notification parts.** Each item of `GET /api/notifications` also has the parts its message names, so the message
needn't be parsed. Names are current, like `novelSlug` (a rename shows here; `message` keeps the words it was sent
with), and null when what they name was deleted since, or when the message doesn't name one:

| Field | Set for |
|---|---|
| `novelTitle` | the five types with `novelId`: GiftReceived, PrivilegeSubscribed, NewChapterInLibrary, ReviewOnNovel, LikeOnReview |
| `chapterId`, `chapterTitle` | NewChapterInLibrary (the new chapter) and CommentOnChapter (the chapter commented on) |
| `readingListName` | ReadingListFollowed (the list is `relatedEntityId`) |
| `giftId`, `giftNameAr`, `giftCount` | GiftReceived (gift notifications from before #25 have none) |
| `giftTransactionId`, `giftMessage` | GiftReceived (#31, below): the gift record and the sender's message, read from the record (null when none or removed); gift notifications from before #31 have neither |

**Wallet.** Each entry of `GET /api/wallet/transactions` with a `giftId` also has `giftNameAr`, the gift's Arabic name,
retired gifts included (the public catalog lists only active ones).

**Sign-up errors.** A refused `POST /api/identity/Register` (400) keeps `result` and also has, at the top level, `code`
and `message` (the same as `result`'s) and `errors`: every problem as `[{ "code", "description" }]`, e.g. both
`DuplicateUserName` and `DuplicateEmail`.

**update-me.** `PATCH /api/User/update-me`: a user name that differs from one's own only in letter case is accepted (it
was refused as `UserNameTaken`); another member's name in any case is still `UserNameTaken`. A refusal by the identity
rules answers its own code (`InvalidUserName`, `DuplicateUserName`...), not `OperationFailed`. `UploadFailed` adds
`field`: `ProfilePhoto` or `ProfileBanner`. Since #44 a success also answers with the saved profile (below).

**My works.** `totalAverageScore` in `GET /api/myworks`, `/api/myworks/{id}` and `/api/myworks/user/{userId}` has its
fraction (`3.75`), like the other novel lists; it was a whole number. Since #46
`/api/myworks/user/{userId}?withChapters=true` lists only novels with a published chapter (below).

**Search.** `GET`/`POST /api/search/novels` with a genre (name or slug) that doesn't exist answers 404 `GenreNotFound`,
as `GET /api/genre/{slug}/novels` does, instead of an empty list. Blank genre values are ignored.

**Idempotent writes: 204 when already done** (a change of status). A write whose state is already as asked answers
204 No Content, no body (it was 400 with the code in brackets):

| Request | Already as asked |
|---|---|
| `POST /api/User/follow`, `DELETE /api/User/unfollow` | following (`AlreadyFollowing`) / not following (`NotFollowing`) |
| `POST /api/comment/{id}/like`, `DELETE /api/comment/{id}/unlike` | liked (`AlreadyLiked`) / not liked, or the comment is gone (`NotLiked`) |
| `POST /api/posts/{id}/like`, `DELETE /api/posts/{id}/unlike` | liked / not liked |
| `POST /api/{novelId}/reviews/{id}/like`, `DELETE .../unlike` | liked / not liked |
| `POST /api/readinglist/{id}/follow`, `DELETE .../unfollow` | following / not following |
| `DELETE /api/readinglist/{id}/novels/{novelId}` | not in the list (`NotInList`; removing it was already 204) |
| `DELETE /api/novel/{novelId}/privilege/subscription` | not subscribed (a subscriber is still refused: 400 `SubscriptionCannotBeCancelled`) |

A request that changes the state answers 200 with its result as before, and a refusal 400 with its code
(`CannotFollowSelf`, `PostNotFound`...); since #52 a like between two members who blocked each other is 403 `Blocked`
(below). Adding a novel a list already has stays 400 `AlreadyInList`. Concurrent
requests (a double tap, a retry) change the state once: the others get the 204, never a 500.

**Counts include blocked members' comments** (a decision, not a bug). A paragraph's `commentsCount` (the reader's
marker), a chapter's `totalCommentsCount` and a post's `commentsCount` count every comment, including those by members
the viewer blocked, while the comment lists leave those out for the viewer. So a marker can say 3 while the sheet
lists 2. The counts are stored per paragraph, chapter and post, shared by every viewer; subtracting per viewer would cost
a query per item, so it isn't done. Clients should treat these counts as "about this many" and not as the list's
length: page through the list with its own `totalItemsCount`, which does leave blocked members out.

### Library: removing a novel, and muting one novel's new chapters (#33)

Every novel a reader opens joins her library (`POST /api/library/track-progress/{chapterId}`), and every new chapter of
a library novel notifies her once, in the app and by push, when it comes out (#39, below). Now she can take a novel out
of her library, or keep it and mute its new chapters. Both act for the signed-in reader (`Authorization: Bearer`; 401
without); errors are `{ "code", "message" }` with an Arabic message.

| Request | Answer |
|---|---|
| `DELETE /api/library/novel/{novelId}` | 204 No Content, no body. Deletes her progress entry for the novel, nothing else. Also 204 when the novel isn't in her library (already as asked, like the idempotent writes since #25) |
| `PATCH /api/library/novel/{novelId}`, body `{ "notifyNewChapters": true }` or `false` | 204 No Content, no body, also when it was so already. 404 `NotInLibrary` «هذه الرواية ليست في مكتبتك.» when the novel isn't in her library (never added, or removed). 400 `ValidationFailed` when the body has no boolean `notifyNewChapters` |

New fields (additive):

| Where | Field |
|---|---|
| `GET /api/library/reading-progress`, each item | `notifyNewChapters` (bool); `lastChapterPublishedAt` (UTC with `Z`, e.g. `"2026-09-29T21:57:47.1234567Z"`, or `null` when the novel has no published chapter); `newChaptersCount` (int, #45) |
| `GET /api/library/novel/{novelId}/progress`, in `progress` | `notifyNewChapters` (bool); `newChaptersCount` (int, #45) |

- **Muted** (`notifyNewChapters: false`): a chapter published in that novel, new or a draft published later, creates
  no `NewChapterInLibrary` notification for her, so no push either. The novel stays in her library with its progress;
  reading it doesn't turn notifications back on. Her other novels, other readers and her push preferences are
  unchanged; the push still also follows her `chapters` push group.
- **Removed**: `reading-progress` no longer lists it and `novel/{novelId}/progress` answers `{ "hasProgress": false }`;
  her `libraryNovelsCount` goes down by one. Reading a chapter of the novel again adds it back through track-progress
  as before: a new entry at that chapter, with notifications on. For «تراجع», hold the DELETE until the undo bar
  closes, or undo with `POST /api/library/track-progress/{lastReadChapterId}` (her position comes back, with
  notifications on and `lastReadAt` now).
- **`lastChapterPublishedAt`**: when the newest of the novel's published chapters came out, read from the chapters with
  the page (not stored). «فصول جديدة» when it is later than `lastReadAt`; both are UTC (`lastReadAt` is sent without the
  `Z`, as before). A chapter comes out when it is first published: a chapter created published, when it is created; a
  chapter saved as a draft and published later, when it is published, so a draft written before her last read and
  published after it shows as new. A chapter unpublished and published again keeps the time it first came out, so it
  isn't new a second time, and since #39 its new-chapter notification and push aren't sent again either. The field's
  name, type and format are unchanged.
- **`newChaptersCount`** (#45): how many of the novel's published chapters came out after `lastReadAt`, for «N فصول
  جديدة». Each chapter counts from when it came out, as for `lastChapterPublishedAt` (a draft published later, from
  when it was published; a chapter unpublished and published again, from the first time), and only if that is strictly
  later than `lastReadAt`: one out at the very instant of her last read (both are stored to 100 ns) isn't new. Drafts
  aren't counted, nor chapters unpublished or deleted since they came out. It is counted in the page's own query, over
  the same chapters as `lastChapterPublishedAt`, so the page costs no extra query and the two agree:
  `newChaptersCount > 0` exactly when `lastChapterPublishedAt` is later than `lastReadAt`. It counts what came out
  since her last read, not what she has left to read: halfway through a novel where nothing new came out, it is 0.
  Reading any chapter of the novel (track-progress, an earlier chapter too) sets `lastReadAt` to now, so it goes back
  to 0. An entry exists only once she has read a chapter and always has a `lastReadAt`, so there is no "never read"
  case: the field is always a number, 0 or more, never null. An API from before #45 doesn't send it; the app then keeps
  its approximation.
- **Schema**: `UserNovelProgress.NotifyNewChapters bit NOT NULL DEFAULT 1`, migration `AddLibraryNotifyNewChapters`:
  every existing entry keeps its notifications. `Chapters.PublishedAt datetime2 NULL` (UTC, null while the chapter has
  never been published), migration `AddChapterPublishedAt`, which fills it in for the chapters that came out before
  it: the first new-chapter notification sent for the chapter (by its id, `RelatedEntityId`), which is when it actually
  came out, or its `CreatedAt` when none was sent (nobody had the novel in their library then). A chapter unpublished
  since keeps the time of its notification; a draft never published stays null. The chapters' `(NovelId, Status)`
  index became `IX_Chapters_Novel_Status_PublishedAt (NovelId, Status, PublishedAt)`, migration
  `AddPublishedAtToChapterNovelStatusIndex` (#45), so `lastChapterPublishedAt` and `newChaptersCount` are index seeks
  and the page doesn't read every chapter of the platform for each novel.

### Chapter dates: when a chapter came out (#39)

A chapter comes out when it is first published: when it is created, if it is created published; when it is published,
if it was saved as a draft first. That time is kept if the chapter is unpublished and published again. Until #39,
several places took a chapter's creation time for it, or counted a draft as an update, so a draft published days after
it was written was dated the day it was written. Everything below now follows when chapters came out.

New field (additive; nothing is renamed or removed, and `createdAt` stays as it was: when the chapter was written):

| Where | Field |
|---|---|
| `GET /api/novel/{novelId}/chapter` (the novel's chapter list), each item | `publishedAt` |
| `GET /api/myworks/{workId}/chapters` (the author's list), each item | `publishedAt` (null for a draft never published) |
| `GET /api/novel/{novelId}/chapter/{chapterId}` (the reader) | `publishedAt` |
| `GET /api/myworks/{workId}/chapters/{chapterId}`, and the chapter `POST /api/novel/{novelId}/chapter` returns | `publishedAt` |

`publishedAt` is UTC with `Z` (e.g. `"2026-09-29T21:57:47.1234567Z"`, like `lastChapterPublishedAt` in the library), and
`null` while the chapter has never been published. It is the date to show for a chapter: the web shows it in the
novel's and the author's chapter lists and in the reader page's published-time tags (the SEO worker's chapter pages
too), falling back to `createdAt` against an API without it.

- **New-chapter notification and push**: sent once per chapter, when it comes out (created published, or a draft
  published for the first time). Unpublishing a chapter and publishing it again tells no one again, as the library's
  «فصول جديدة» doesn't show it again. Whether a save is a chapter's first publish is decided in the database, so two
  saves publishing a draft at the same moment tell readers once.
- **The novel's `lastUpdatedAt`** (the "last updated" search sort, the novel page, my works, the sitemap): moves to the
  time a chapter comes out, and only then. Writing a draft, saving or editing a chapter that is out, unpublishing a
  chapter, publishing it again or deleting one don't move it (editing never did). It only moves forward. The migration
  `RecomputeNovelLastUpdatedAt` put the stored values under this rule: when the novel's newest chapter came out (a
  chapter unpublished since counts, it came out then), or when the novel was created if none has; a chapter deleted
  after it came out no longer counts.
- **Rankings**: Trending's boost for a fresh chapter, the New lists' 60-day window and head start, and the tie-break by
  the newest chapter use when the novel's published chapters came out. Drafts and unpublished chapters don't count. The
  lists are recomputed a minute after the app starts and every 30 minutes, so a deploy updates them.
- **Sitemap** (`GET /api/seo/sitemap`): a chapter's `lastModified` is when it came out, and a novel's is the later of
  its `lastUpdatedAt` and when its newest published chapter came out.
- A published chapter always has a publish date (AddChapterPublishedAt filled them in, #33). Only code from before
  that could publish one without it after the migration ran (during a deploy or after a rollback): the rankings then
  place the novel by its other chapters (or leave it out while none has a date) and log an error naming it, and the
  sitemap gives that chapter no `lastModified`.

### Editing the profile: update-me answers with the saved profile (#44)

`PATCH /api/User/update-me` (signed in; the text fields in the query string or the form, the pictures in the form)
answers a success with the profile as it is after the save, so the app doesn't have to read `my-profile` again:

```json
{ "success": true, "message": "تم تحديث الملف الشخصي", "profile": { "id": "...", "userName": "...", ... } }
```

- **`profile`** is exactly what `GET /api/User/my-profile` returns, the same fields with the same values (`userName`,
  `displayName`, `userBio`, `profilePhoto`, `profileBanner`, `facebookUrl`, `twitterUrl`, `discordUrl`, the counts,
  `hasPassword`...): the handler reads it after the save through the query behind `my-profile`, so the two can't
  differ. A new user name (a change of letter case too), a new photo or banner URL, and a bio or link cleared with
  `""` (then `null`) are all in it.
- **Refusals are unchanged** and have no `profile`: 400 `{ "success": false, "code", "message" }` with `UserNameTaken`,
  `ReservedUserName` (a `deleted-` name), the identity rules' code (`InvalidUserName`, `DuplicateUserName`...) or
  `UploadFailed` (which adds `field`: `ProfilePhoto` or `ProfileBanner`); 400 `ValidationFailed` (the validation
  problem, with `errors`) for a field the validators refuse; 401 signed out.
- An API from before #44 answers `{ success, message }` only; the app then reads `my-profile` as before. The web reads
  only the status and reloads the profile itself, so nothing changes for it.

### Competitions: the status follows the schedule (#42)

A competition's `status` used to be whatever an admin last stored, so a contest whose dates had long passed still said
`Upcoming` (production's «انا مميز», months after its results date). It now follows the dates, wherever it is read and
when a novel joins or leaves. The field keeps its name and its four values; what it says is the status in effect now.

| `status` | From | Until | `canJoin` | `POST .../join` | `DELETE .../leave/{novelId}` |
|---|---|---|---|---|---|
| `Upcoming` | creation | `participationStartDate` | `false` | 403 `CompetitionClosed` | allowed |
| `Participation` | `participationStartDate` | `participationEndDate` | `true` (when `isActive`) | allowed (when `isActive`) | allowed |
| `Judging` | `participationEndDate` | finalized | `false` | 403 `CompetitionClosed` | 403 `ParticipationEnded` |
| `Completed` | finalized, or an admin | | `false` | 403 `CompetitionClosed` | 403 `ParticipationEnded` |

- **Boundaries.** A date is the first instant of the phase it opens: at exactly `participationStartDate` the
  competition is open, at exactly `participationEndDate` it is closed. `judgmentStartDate`, `judgmentEndDate` and
  `resultsDate` are for showing: the competition stays `Judging` through them and after, until an admin finalizes it.
  The dates are UTC, sent without the `Z` as before (`"2025-12-25T14:45:38.138"`): read them as UTC. Admins send
  them as UTC too, with the `Z` (`toISOString()`): a date with another offset is converted to the server's local time
  before it is stored.
- **Where.** The list `GET /api/competition`, the page `GET /api/competition/{idOrSlug}`, what creating and updating
  answer, and `competitionStatus` in `GET /api/competition/my-participations`; joining and leaving decide by the same
  status. One request reads the clock once, so a list and its filter agree.
- **`?status=`** (`GET /api/competition?status=Judging`): the competitions whose status is that now, decided in the
  database by the same rule. One of the four names in any letter case; blank lists them all; anything else is 400
  `InvalidStatus` (it used to answer an empty list).
- **Admin override** (`PUT /api/competition/{id}` with `status`, admin). The stored status counts only where it is
  further along than the dates, in the order `Upcoming` < `Participation` < `Judging` < `Completed`: an admin can open
  a competition early (`Participation` before its start date), close it early (`Judging`) or complete it early
  (`Completed`), and the dates take over again once they pass the override. A stored status behind the dates changes
  nothing (storing `Upcoming` in the participation window doesn't close it). To postpone, move the dates and store
  `Upcoming`, which is no override. The answer's `status` is the one in effect. `status` must be one of the four names
  (any letter case, stored as written above), else 400 `InvalidStatus`. Creating a competition, or moving a
  participation date, so that participation doesn't end after it starts is 400 `InvalidSchedule`.
- **Finalizing** (`POST /api/competition/{id}/finalize`, admin): the top three participants win, as before. A
  competition nobody joined is completed with no winners (200, `[]`); it used to fail with 500. Finalizing again
  keeps the winners and answers the same, and always leaves the competition `Completed`. A competition past its
  results date stays `Judging` until an admin finalizes it, also when nobody joined, so finalize each one that ends.
- **Existing data.** The migration `CompleteEndedCompetitionsWithoutParticipants` completed every competition whose
  participation had ended and whose results date had passed, with nobody in it: in production, «انا مميز». One with
  participants is left in `Judging` for an admin to finalize (that chooses its winners). Its down leaves them completed.
- **Nothing else moves with a status.** Changing a status never sent a notification, an email or points, and a status
  reached by the dates alone doesn't either.

### Posts: writing one (#43)

`POST /api/posts`, signed in, multipart form-data with three fields, each optional but **not all**: `Content` (the
text), `Image` (a picture file) and `NovelId` (a novel to attach). Field names are case-insensitive, as the web sends
them (`content`, `image`, `novelId`). This is the only way posts are created; they can't be edited (only deleted,
liked and unliked), so these rules are all there is.

**Rules**

- **Text, a picture or a novel.** `Content` is trimmed, and stored trimmed; left out, empty or only whitespace is no
  text. Text is required only when neither a picture nor a novel is attached. A post without text is stored, and read
  everywhere, with `content: ""` (not `null`).
- **At most 5000 characters**, counted after trimming in user-perceived characters, as gift messages are (#31): .NET's
  `new StringInfo(text).LengthInTextElements` (`Application/Common/TextElements.cs`), which Flutter's `characters.length`
  and the web's `Intl.Segmenter` match. An emoji, a flag or a letter with its tashkeel is one character, whatever its
  UTF-16 length. Text within 5000 characters but over 100,000 UTF-16 units is refused as too long as well: that is
  only a letter under thousands of combining marks; real text never gets near it (5000 of the longest emoji take
  75,000).
- **A picture: JPEG, PNG or WebP, at most 5 MB (5,242,880 bytes)**, like every picture members upload. The type is the
  `Content-Type` the client declares for the file part, exactly `image/jpeg`, `image/png` or `image/webp` (`image/jpg`
  is accepted as an alias); the server doesn't look at the bytes. So the apps must send the right type, and convert
  what isn't one of these (HEIC from an iPhone camera, GIF) before sending. An empty file is refused as not a picture.

**Refusals** are 400 with the endpoint's usual shape, `{ "success": false, "code", "message", "post": null }` (as
`NovelNotFound` always was), with an Arabic `message`. An answer carries one code: the first rule broken, in this
order. The first four are checked before anything is looked up or stored.

| Order | HTTP | `code` | When | `message` |
|---|---|---|---|---|
| 1 | 400 | `PostContentRequired` | no text, and no picture or novel | «المنشور فارغ: اكتب نصًا أو أرفق صورة أو رواية.» |
| 2 | 400 | `PostContentTooLong` | over 5000 characters (or 100,000 UTF-16 units) | «المنشور طويل: الحد الأقصى 5000 حرف.» |
| 3 | 400 | `PostImageType` | a picture that isn't JPEG, PNG or WebP, or an empty file | «صيغة الصورة غير مدعومة: اختر صورة JPEG أو PNG أو WebP.» |
| 4 | 400 | `PostImageTooLarge` | a picture over 5,242,880 bytes | «الصورة كبيرة: الحد الأقصى 5 ميغابايت.» |
| 5 | 400 | `NovelNotFound` | `NovelId` is no novel, or a deleted one (unchanged) | «الرواية غير موجودة» |
| 6 | 400 | `UploadFailed` | the picture couldn't be stored (below) | «تعذّر رفع الصورة، حاول مرة أخرى.» |
| | 401 | | not signed in, before any rule | |

So a picture makes text optional even when that picture is then refused (no text with a GIF is `PostImageType`), and
a picture's type comes before its size (a 6 MB GIF is `PostImageType`). A `NovelId` that isn't a GUID is refused by
ASP.NET as before (400 `ValidationFailed`). The rules are `Application/Posts/PostRules.cs`, checked by
`CreatePostCommandValidator`, which the handler runs itself so each refusal keeps its code.

**A picture that can't be stored.** The picture is stored (Cloudflare R2) before the post is saved, so **no post goes
out without its picture**: when storing it fails, the answer is 400 `UploadFailed`, nothing is created, and the failure
is logged at Error with its exception (it was a 500 without a code). The same request can simply be sent again. A
request cancelled by the client isn't counted as a failed upload. If saving the post fails after its picture was
stored, the picture is deleted (as far as the storage allows) and the answer stays 500 `ServerError`.

**Request size.** The API keeps ASP.NET Core's default limit on a request body, **30,000,000 bytes** (about 28.6 MB),
which is also IIS's default (`maxAllowedContentLength`, unless the host changed it); nothing in the code changes
either, and ASP.NET's multipart form limit is higher (128 MB). So a picture far over 5 MB, up to about 28 MB, still gets
`PostImageTooLarge`, and text of any length within the request gets `PostContentTooLong`. A bigger request is refused
by the server before the post rules run, without a post code: it closes the connection, so a client still sending
sees a network error (one that waits for `100 Continue` reads 400 `ValidationFailed`; IIS's own filter answers
404.13). Checking the 5 MB limit before uploading keeps the apps far from it.

**Mirroring the limits.** `GET /api/app/config` serves them as `posts`, the very values the server checks, from the
code (they aren't settings); without it (a server from before #43), use the same numbers:

```json
"posts": { "contentMaxLength": 5000, "imageMaxBytes": 5242880, "imageTypes": ["image/jpeg", "image/png", "image/webp"] }
```

A client that mirrors them counts characters as above (after trimming), offers or converts to JPEG, PNG or WebP,
compresses or refuses a picture over `imageMaxBytes` before uploading, lets a post go with only a picture or a novel,
and shows the server's Arabic `message` for any refusal, branching on `code`.

### An author's works for «أعمال أخرى للكاتب»: `withChapters=true` (#46)

`GET /api/myworks/user/{userId}` (signed in or not; `pageNumber` from 1, `pageSize` 1 to 50, 10 by default) lists a
member's public novels, latest update first. Since #46 it takes `withChapters`. The answer doesn't depend on who asks:
the author gets what anyone gets.

| Request | Listed, and counted in `totalItemsCount`, `totalPages` and `itemsTo` |
|---|---|
| `withChapters=true` | only the novels a reader can open: not a draft, with at least one published chapter |
| without it, or `withChapters=false` | every public novel, with or without a published chapter, as before #46 |

- **The app** sends `withChapters=true` for the «أعمال أخرى للكاتب» shelf on the novel page: ask for as many novels as
  the shelf shows and use `totalItemsCount` for «more»; there is nothing left to filter out. An API from before #46
  ignores the parameter and answers every public novel.
- **The web doesn't send it.** Other members' profiles (and their `noindex` rule) and the SEO worker's profile pages
  list every public novel, as before, like search and the genre pages, which list novels before their first chapter
  on purpose. A member's own profile reads `GET /api/myworks`.
- **A published chapter** is one whose status is `Published` now. With the flag, a novel is left out while it has no
  chapter, only drafts (never published, or published and made a draft again), or none left after its published
  chapters were deleted, and it is listed as soon as one of its chapters is published. This is the rule of new
  arrivals, the rankings, recommendations and the sitemap, and of the reader, which opens only the published chapters
  of a novel that isn't a draft.
- **Drafts and deleted novels** are never listed, with or without the flag. The author's drafts are in
  `GET /api/myworks`.
- **Pages** never repeat or skip a novel: novels updated at the same moment go by id.
- **A value other than `true` or `false`** (`withChapters=abc`, or `1`) is refused like any query value of the wrong
  type: 400 `ValidationFailed`, the validation problem, with `errors.withChapters` «القيمة المرسلة غير صالحة» and
  `message` «البيانات المرسلة غير صالحة.». It is never a 500.
- **No token is needed.** A token that doesn't validate (expired, signed out everywhere, or the web's `Bearer undefined`
  when signed out) is answered as signed out: 200, never 401.

### Search and browsing with something to read: `withChapters=true` (#58)

`GET /api/search/novels` (query parameters) and `POST /api/search/novels` (the same fields in a JSON body) search
novels by title, or browse them without a query: `genres`, `status`, `chapterRanges`, `sortBy`, `pageNumber` from 1,
`pageSize` 1 to 50 (20 by default); signed in or not. Since #58 both take `withChapters`, with the meaning #46 gave it
on a member's works:

| Request | Listed, and counted in `totalItemsCount`, `totalPages` and `itemsTo` |
|---|---|
| `withChapters=true`, or `"withChapters": true` | only the novels a reader can open: not a draft, with at least one published chapter |
| without it, `false`, an empty value (`withChapters=`) or a JSON `null` | every novel that isn't a draft, with or without a published chapter, as before #58 |

- **The default doesn't change.** By the owner's decision (25 September 2026), a novel shows in search and on the genre
  pages before its first chapter is published; drafts never do. The rankings, new arrivals, recommendations and the
  sitemap list only novels with a published chapter, as before.
- **The app** sends `withChapters=true` for browsing («آخر التحديثات»: no query, `sortBy=LastUpdated`) and can drop
  its own filter: every item has a `chapterCount` of at least 1, and every page but the last is full. An API from
  before #58 ignores the parameter and lists every novel that isn't a draft.
- **The web doesn't send it**: its search page lists novels before their first chapter, as before.
- **A published chapter** is one whose status is `Published` now, as in #46: with the flag a novel is left out while it
  has no chapter, only drafts (never published, or published and made a draft again), or none left after its published
  chapters were deleted, and it is listed as soon as one of its chapters is published.
- **The other fields** narrow it further: the query words, `genres`, `status`, `chapterRanges` (which already need at
  least one published chapter), and every `sortBy`. Deleted novels are never listed.
- **Pages** never repeat or skip a novel: novels with the same sort value go by id.
- **A value other than `true` or `false`** is 400 `ValidationFailed` with an Arabic `message`, never a 500:
  - GET (`withChapters=abc`, or `1`), as #46 answers: `errors.withChapters` «القيمة المرسلة غير صالحة» and `message`
    «البيانات المرسلة غير صالحة.».
  - POST (`"withChapters": "abc"`, `1`, or the string `"true"`), as any JSON value of the wrong type in any request
    body: the error is under the value's JSON path, `errors["$.withChapters"]`, and `message` is
    «تعذّرت قراءة البيانات المرسلة. تأكد من صيغتها وحاول مرة أخرى.».

### The author of each novel in search results and on a reading list (#59)

Each item of `GET`/`POST /api/search/novels` (`items`) and each novel of a reading list (`GET /api/readinglist/{id}`,
`novels`) has `author`: the novel page's own `author` (`GET /api/novel/{slug}`, `GET /api/novel/by-id/{id}`), the same
object, so one model reads all three.

```json
"author": { "id": "3f2a9c1b-…", "userName": "sara_writes", "displayName": "سارة", "profilePhoto": "https://…/photo.webp" }
```

| Field | |
|---|---|
| `id` | the author's account id (the `userId` of `GET /api/myworks/user/{userId}`) |
| `userName` | their user name now (the profile is `GET /api/User/{userName}`) |
| `displayName` | the name readers see, now |
| `profilePhoto` | the photo's URL, or null when they have none (beyond the issue's three fields, as the novel page sends it) |

- **Current, never a copy.** It is read from the account together with the novel, so a new user name, display name or
  photo shows in the very next answer.
- **No extra requests or queries.** Search reads it in the page's own SQL query, and a reading list with its novels:
  the number of queries doesn't grow with the page size or the list's length.
- **A deleted account** (by the member or by an admin): its novels are deleted with it, so they are in neither list, nor
  in its counts (`totalItemsCount`, `novelsCount`). If one were ever restored, it would name its author as the novel
  page does: «مستخدم محذوف», a `deleted-…` user name and no photo.
- **Additive.** An API from before #59 leaves `author` out: the app keeps its own lookup when the field is missing. The
  web doesn't read it yet. `GET /api/search/suggest` is unchanged.

### The author of each novel in the rankings and a genre's lists (#68)

Every list that answers with ranked novels (`NovelInRankingDto`) gives each one the same `author` as #59, the novel
page's own: `{ id, userName, displayName, profilePhoto }` (`profilePhoto` null without one).

| Request | |
|---|---|
| `GET /api/rankings/site-wide/{type}` | `Trending`, `AllTime`, `NewArrivals`: the app's home rails and rankings screen |
| `GET /api/rankings/{genreSlug}/{type}` | a genre's `trending`, `top_rated` and `new` rankings |
| `GET /api/genre/{genreSlug}/novels` | a genre's novels in every `sorting`: the ranked `trending`, `top_rated`, `new`, and `popular`, `newest`, `rating`, `most_reviewed` |

- **Current, never a copy.** A ranking is stored as its novels' places only (recomputed every 30 minutes); what each
  novel shows, now its author too, is read with the page. So a new user name, display name or photo shows in the very
  next answer, without waiting for the rankings to be computed again. The API doesn't cache these answers (no
  `Cache-Control`); how long the app keeps a page it loaded is the app's choice.
- **No extra queries.** The author is joined in the page's own SQL query: a page of many novels by many authors is as
  many queries as a page of one.
- **A deleted account**: its novels are deleted with it and leave every one of these lists at once, before the
  rankings are computed again.
- **Additive.** A client that doesn't read `author` is unaffected, and the app shows no author line where it is missing
  (an API from before #68).

### Blocks: a single post, its discussion, likes, comments and notifications (#52)

A block now reaches a single post and its discussion, likes, comments on posts, replies and notifications, as it
already reached a member's profile, post list and reading lists (#10, #25). Errors are `{ "code", "message" }` with an
Arabic message: branch on `code`.

**A single post: `GET /api/posts/{postId}`** (signed in or not).

| Viewer | Answer |
|---|---|
| the post's author blocked them (both blocking each other included) | 404 `PostUnavailable` «هذا المنشور غير متاح» |
| they blocked the author, and the author didn't block them | 200, the post with `authorBlockedByMe: true` (so the app can offer to unblock) |
| the author, anyone else, anonymous | 200, `authorBlockedByMe: false`, as before |
| anyone, for a deleted post | 404 `PostNotFound` «هذا المنشور لم يعد موجودًا», as before (checked first) |

`authorBlockedByMe` is on every post the API answers: the single post, each item of `GET /api/posts/user/{userId}`
(always false there: the page is empty, as before, when either of the two blocked the other) and the `post` of
`POST /api/posts` (false: its author). An API from before #52 leaves it out: read a missing field as false.

**The post's discussion** answers the same 404 `PostUnavailable` to a member the post's author blocked, and only to
them: `GET /api/comment/post/{postId}`, the replies `GET /api/comment/chapter/comments/{commentId}` of a comment on the
post, and `GET /api/notifications/comment/{commentId}` for a comment or reply on it. A member who blocked the author
reads them as before (their lists leave out the author's comments).

**Likes.** `POST /api/posts/{postId}/like`, `POST /api/comment/{commentId}/like` and
`POST /api/{novelId}/reviews/{reviewId}/like` answer 403 `Blocked` «لا يمكنك التفاعل مع هذا المستخدم.» when the liker
and the author of the post, comment or review blocked each other, either way, and write nothing: no like, no count, no
notification. It comes after the not-found answers and `CannotLikeOwnContent`, and before the 204 of a repeated like.
Taking a like back (`DELETE .../unlike`) is never refused, so a like from before the block can be removed. One's own
content is as before: an author may like their post, not their comment or review.

**Comments on posts, and replies** (beyond the issue's text, for consistency with the notifications below): refused
either way, 403 `Blocked`, with a message for each direction.

| Request | The other member blocked you | You blocked them |
|---|---|---|
| `POST /api/comment/post/{postId}`: a comment on the post, or a reply under it | «لا يمكنك التعليق على منشورات هذا المستخدم» (as before) | «ألغِ حظر هذا المستخدم أولاً لتتمكن من التعليق على منشوراته» |
| a reply (`ParentCommentId`) to their comment, on a chapter, a paragraph or a post | «لا يمكنك الرد على تعليقات هذا المستخدم» (as before) | «ألغِ حظر هذا المستخدم أولاً لتتمكن من الرد على تعليقاته» |

Nothing is created. Under a post, the post's author is checked first, then the author of the comment answered. A
top-level comment on a chapter or a paragraph stays open to everyone, blocked or not: it is the novel's public
discussion (it just notifies nobody across a block).

**Notifications.** A notification from one member to another isn't created, so not pushed either, when either of them
blocked the other: `NewFollower`, `CommentOnChapter`, `CommentOnPost`, `ReplyToComment`, `ReviewOnNovel`,
`LikeOnReview`, `LikeOnComment`, `LikeOnPost`, `ReadingListFollowed`, and any type added later. Three are still
stopped only when the recipient blocked the actor, as every type was before #52: `NewChapterInLibrary` (a new chapter,
to every reader who keeps the novel in their library; its actor is the novel) and the payments `GiftReceived` and
`PrivilegeSubscribed`, which an author learns of even from a member who blocked them. The rule is
`Domain/Constants/NotificationBlocking.cs`, applied by `NotificationsRepository` on each of its three ways to create
notifications (one, deduplicated, and the batched fan-out, still one block query per batch); `NotificationBlockingTests`
fails when a type is added without deciding its rule. Blocking still deletes the notifications the blocked member had
caused the blocker; those going the other way stay.

### A member's reviews and comments, and the counts on their profile (#54)

Two public lists (signed in or not) behind `reviewsCount` and `commentsCount` on the profile. Errors are
`{ "code", "message" }` with an Arabic message.

| Request | Lists |
|---|---|
| `GET /api/User/{userName}/reviews` | the member's reviews |
| `GET /api/User/{userName}/comments` | the member's comments on chapters and paragraphs, replies included |

- **Pages** as the other lists: `pageNumber` from 1, `pageSize` 1 to 50 (10 by default); the answer is
  `{ items, totalPages, totalItemsCount, itemsFrom, itemsTo }`. Newest first; items written at the same moment go by
  id, so pages never repeat or skip one.
- **The member** is found as `GET /api/User/{userName}` finds them: the name they have now, in any letter case, or a
  name they used before (an old link after a rename). A deleted account, by its old names or its `deleted-...` name,
  and a name nobody has, are 404 `UserNotFound` «المستخدم غير موجود».
- **Blocks.** When the signed-in viewer and the member blocked each other, either way, both lists are an empty page
  (`totalItemsCount` 0), as the member's posts (`GET /api/posts/user/{userId}`) are; never a 403 for the block itself.
  Everyone else, signed out included, and the member themselves, see the lists in full, unless the member hid one
  (#61, below: 403 `ListHidden`, which comes before the block rule).
- **Likes.** `isLikedByCurrentUser` is the signed-in viewer's like, false when signed out. Everything else in an item
  is the same for everyone.

**What is listed.** An item is listed only where a reader could open the place it was written:

| List | Listed | Left out |
|---|---|---|
| reviews | reviews on a novel readers can open: not a draft, with at least one published chapter (the rule of #46) | reviews on a novel that is a draft, has no published chapter (none, only drafts, or its chapters unpublished or deleted), or is deleted: by its author, removed by a moderator, or hidden when its author deleted their account |
| comments | top-level comments and replies on a published chapter, or on a paragraph of one, of a novel readers can open | comments on posts and the replies under them; deleted comments; replies whose parent comment was deleted (its thread is gone); comments on a chapter that isn't published (never, or not any more); comments on a novel that is a draft or deleted, as for reviews |

A review or comment a moderator removes (a report resolved with `RemoveContent`) is deleted outright, with a comment's
replies, and so is every comment on a chapter or paragraph that is deleted. A suspended member's content is not hidden
anywhere, so it stays listed. A chapter locked for early access is still published: the comments on it are listed.

**Review items** (`reviews`):

| Field | |
|---|---|
| `id` | the review |
| `writingQualityScore`, `updatingStabilityScore`, `characterDevelopmentScore`, `worldBuildingScore` | its four scores, 1 to 5 |
| `totalAverageScore` | its rating: the average of the four |
| `content` | its text, or null |
| `isSpoiler` | the text gives the story away: hide it until the reader asks |
| `likeCount`, `isLikedByCurrentUser` | as in the novel's review list |
| `createdAt`, `updatedAt` | UTC with "Z"; `updatedAt` is null when it was never edited (#34) |
| `novel` | `{ id, slug, title, coverImageUrl }` |

**Comment items** (`comments`):

| Field | |
|---|---|
| `id`, `content`, `attachedImageUrl` | the comment, its text and its picture (or null) |
| `likesCount`, `isLikedByCurrentUser` | as in the comment lists |
| `createdAt`, `updatedAt` | UTC with "Z"; `updatedAt` is null (comments can't be edited yet) |
| `isReply`, `parentCommentId` | whether it answers another comment, and which: the thread it is in |
| `parentComment` | for a reply, the comment it answers (#60), whole as its thread shows it (#67): `{ id, content, attachedImageUrl, likesCount, isLikedByCurrentUser, createdAt, totalRepliesCount, user: { id, userName, displayName, profilePhoto } }`, its full text (the apps shorten it) and its author as the comment lists show authors, with the names they have now (the fields are below). Null for a top-level comment, and for a reply to someone the signed-in viewer blocked (see below) |
| `novel` | `{ id, slug, title, coverImageUrl }` |
| `chapter` | `{ id, title, number }`: the chapter it was written on, for a paragraph comment the paragraph's; `number` is the chapter's number as readers see it, its position among the novel's published chapters from 1 (as the library's `lastReadChapterNumber`), which changes when chapters before it are published, unpublished, deleted or reordered |
| `paragraphId` | the paragraph it was written on, or null for a comment on the chapter itself. A reply in a paragraph's thread is on that paragraph too |
| `paragraphExcerpt` | the start of that paragraph as plain text, at most 140 characters (a longer paragraph is cut, on a word where it can, and ends in "…"), exactly as the comment context gives it (`GET /api/notifications/comment/{id}`, #60). Null when `paragraphId` is null, when the paragraph has no text (only a picture, or a blank line), and when the reader wouldn't show the chapter to the viewer: a chapter in early access, to anyone but the novel's author and its subscribers. The comment is listed either way |

The website opens a chapter at `/novel/{novel.slug}/chapter/{chapter.id}`.

**What a reply answers (#60).** `parentComment` is always there for a reply but in one case: the signed-in viewer
blocked the parent's author. The comment lists leave a blocked member's comments out for the one who blocked them
(not the other way round), so here the reply stays listed, with `isReply` and `parentCommentId`, and
`parentComment` is null; the list's total stays the profile's `commentsCount`. The parent's author shows with their
names as they are now (a rename shows at once); a parent whose author deleted their account shows as the comment
lists show it, `{ userName: "deleted-...", displayName: "مستخدم محذوف", profilePhoto: null }`, with its text. Replies
whose parent was deleted aren't listed at all (above). With `parentComment`, `parentCommentId` and
`chapter`/`paragraphId`, an app can say what a reply answers and open its thread (the replies are
`GET /api/comment/chapter/comments/{parentCommentId}`) without reading the comment context first; the context still
gives the page of the thread that holds the reply.

**The parent, whole (#67).** `parentComment` has what the chapter and paragraph comment lists give a top-level
comment to draw it, under the same names and with the same meanings, so the app draws it on top of the thread at once,
without reading the comment first:

| Field | |
|---|---|
| `id`, `content`, `user` | as above (#60) |
| `attachedImageUrl` | its picture, or null |
| `likesCount` | its likes |
| `isLikedByCurrentUser` | the signed-in viewer's like; false when signed out |
| `createdAt` | when it was written, UTC with "Z", like the list's other dates (the thread lists send the same instant without the "Z") |
| `totalRepliesCount` | the replies its thread shows the viewer, as the thread lists count them: not those by members the viewer blocked, nor deleted ones. At least 1: the listed reply is one of them |

Of the thread lists' fields, three aren't sent: `parentCommentId` (always null: threads are one level deep), `chapterId`
(the listed reply's `chapter` and `paragraphId` say where the thread is) and `hasMoreReplies` (always true here). The
thread lists have no `updatedAt` (comments can't be edited). The fields are additive; a client from before #67 ignores
them. Blocks and nulls are #60's: when `parentComment` is null, there is nothing of it to send.

**The counts.** `reviewsCount` and `commentsCount` on `GET /api/User/{userName}` and `GET /api/User/my-profile` (and so
in update-me's `profile`) are the totals of these two lists as anyone signed out sees them, counted by the same
queries on every request. They were the stored counters `User.ReviewsCount` and `User.CommentsCount`, which are still
kept up to date but are no longer shown. So:

- `commentsCount` no longer counts comments on posts, nor replies under them; it counts only what the comment list
  shows. It also leaves out what the stored counter still counted: comments on unpublished chapters, on draft or
  deleted novels, and replies in deleted threads. `reviewsCount` leaves out reviews on novels readers can't open.
- A viewer who blocked the member still opens their profile (`isBlockedByMe`) and sees the counts anyone sees, while
  both lists are empty for them.

**Performance.** Each profile counts both lists: the reviews on the (ReviewerId, NovelId) index, and the comments on
`IX_Comments_UserId_CreatedAt`, (UserId, CreatedAt descending) with every column the comment list filters on, which
the migration `AddCreatedAtToCommentsUserIndex` puts in place of `IX_Comments_UserId`. Both read the member's own
rows, and a page of comments reads them in order. A page of comments is its total and one query for its items, which
also reads each reply's parent and its author and each paragraph's text, by primary key, and counts each parent's
replies on `IX_Comments_ParentCommentId` (#67); the viewer's likes are one query for the items and their parents
together; whether the viewer may read a chapter is decided once for each chapter on the page that has a paragraph
comment (the early-access check), never once per comment. #67 added no query: a signed-out page of chapter comments
is 3 SQL commands (the member, the total, the page) and a signed-in one 5 (adding the block check and the likes), as
before.

**User names.** `followers-list` and `following-list` are reserved like `blocked` and `my-profile` (sign-up and
update-me refuse them, in any letter case): `GET /api/User/followers-list/reviews` is the followers route, so a member
with that name could never have their lists opened.

### Privacy: hiding a member's reviews or comments list (#61)

Each member chooses, for each of their two lists (#54), who may browse it: `Everyone` (the default, and how the lists
were before) or `OnlyMe`. There is no "followers only", on purpose: anyone can follow anyone without approval, so it
would protect nothing. The setting hides the lists on the profile, not the content: each review stays under its novel
and each comment under its chapter or paragraph, as before, for everyone.

**Reading and changing the settings** (signed in; 401 otherwise), as `GET/PATCH /api/notifications/preferences` work:

| Request | Answer |
|---|---|
| `GET /api/User/me/privacy` | `{ "reviews": "Everyone", "comments": "OnlyMe" }` |
| `PATCH /api/User/me/privacy` with `{ "reviews"?, "comments"? }` | the settings after the change, as `GET` answers them |

- A value is `Everyone` or `OnlyMe` in any letter case (`"onlyme"` works); it is stored and answered as `Everyone` or
  `OnlyMe`. A field left out, or `null`, keeps its setting; `{}` or an empty body changes nothing and answers the
  settings.
- Any other value is 400 `ValidationFailed`, and nothing is saved, not even a valid value sent with it: another text
  (an unknown name, `""`, `"1"`) has its Arabic message under `errors.Reviews` or `errors.Comments` and in `message`;
  a value that isn't text (a number, a boolean) is refused while the body is read, under `errors["$.reviews"]` (or
  `$.comments`).
- `GET /api/User/my-profile` (and so update-me's `profile`) carries the same values as `reviewsVisibility` and
  `commentsVisibility`.

**What everyone else gets** (anyone but the member: signed in or not, admins included, on these public routes;
moderation and admin tools are unchanged):

- **The lists.** A hidden `GET /api/User/{userName}/reviews` (or `/comments`) is 403
  `{ "code": "ListHidden", "message": "اختار صاحب الحساب إخفاء مراجعاته" }` (for comments
  «اختار صاحب الحساب إخفاء تعليقاته»). The answers come in this order: 404 `UserNotFound` (a name nobody has, or a
  deleted account), then 403 `ListHidden`, then the block rule (#54: an empty page). So someone in a block with the
  member, either way, gets the 403 for a hidden list and the empty page for the other one. An old user name finds the
  member as before, and the list is hidden all the same.
- **The profile.** `GET /api/User/{userName}` carries `reviewsHidden` and `commentsHidden`: whether that list is hidden
  from this viewer, so the app can show the list as hidden instead of opening it. Always false for the member
  themselves. A client that doesn't read them gets the 403 when it opens the list; missing fields read as "not
  hidden".
- **The counts stay** (the owner's decision, 2026-10-01): `reviewsCount` and `commentsCount` keep their real values for
  everyone, as social sites show a friend count while the list itself is private. They are still the lists' totals as
  the lists would be shown to anyone (#54).
- The member always sees their own lists in full.

**Storage.** Two columns on the user, `ReviewsVisibility` and `CommentsVisibility` (text, `Everyone` by default; the
migration `AddProfileListVisibility` gives every existing member `Everyone`). A change is one UPDATE of those two
columns and a new concurrency stamp, never a save of the whole user row, so a change made at the same moment elsewhere
(a counter, a suspension) is kept, and an update-me that read the account before the change fails (400
`ConcurrencyFailure`, try again) instead of writing the old settings back. The lists and the profile read the setting
with the member they already load, so they cost no extra query.

**Routes.** No user name had to be reserved for `me/privacy`: it matches no path of the `{userName}` routes (their
second segment is `reviews` or `comments`, and `GET /api/User/{userName}` has one segment), and `me` is shorter than
the 3 characters a user name needs anyway.

### Stored counters recounted once (#66)

The counts the API shows are stored next to what they count (a post's `commentsCount`, a chapter's
`totalCommentsCount`, a review's likes...), and since 2026-09-25 they move with atomic SQL. Before that the code read
them, changed them and wrote whole rows back, some of it in the background with failures only logged, so concurrent
changes and failed writes were lost, and nothing ever recounted them: post `17f1d72d` said `commentsCount: 0` with a
comment (and a reply to it) under it. The migration `RecountStoredCounters` sets each of them, once, at the deploy,
from the rows it counts, by the rule the code keeps it by:

| Counter | Counts |
|---|---|
| `Posts.CommentsCount` | top-level comments on the post that their author hasn't deleted; replies don't count |
| `ChapterParagraphs.CommentsCount` | top-level comments on the paragraph, not deleted |
| `Chapters.CommentsCount`, `TotalCommentsCount` | top-level comments on the chapter itself, not deleted; the total adds its paragraphs' |
| `AspNetUsers.CommentsCount`, `ReviewsCount` | the member's comments anywhere (replies included, not deleted) and reviews; not shown since #54 |
| `Posts.LikesCount`, `Comments.LikesCount`, `Reviews.LikeCount` | likes; a deleted post or comment keeps its likes |
| `Novels.ReviewCount` and its five average scores | the novel's reviews, and their averages as writing a review stores them (2 decimals) |
| `AspNetUsers.LibraryNovelsCount` | novels in the member's library, deleted and draft novels included |
| `ReadingLists.NovelsCount`, `FollowersCount` | novels on the list (hidden ones too; this one isn't shown), and its followers |
| `Chapters.ParagraphsCount`, `Novels.ChapterCount` | the chapter's paragraphs; the novel's published chapters |

- Every row is recounted, deleted posts, novels and accounts too, as the code keeps theirs. A comment a moderator
  removed, or on a chapter or paragraph since deleted, is gone and counts nowhere; one its author deleted doesn't count,
  and the replies under it still count for their authors.
- Only rows whose value differs change, and running it again changes nothing. `Down` changes nothing: the drifted
  values can't be told apart from right ones.
- It holds what it counts until it commits and runs at high deadlock priority: a comment, like, review or library
  change sent during the deploy waits for it (or, in a deadlock, fails and can be sent again), and is counted once.
- Not recounted: views, points and money, and what the scheduled jobs rebuild (rankings, the supporters board).

**Before deploying: the diagnostic.** `Infrastructure/Migrations/20261004164302_RecountStoredCounters.Diagnostic.sql` is
the same recount as three SELECT statements, generated from the migration's definitions (a test keeps the two equal and
checks that it reports exactly the rows the migration fixes). It writes nothing; run each statement on production with
a read-only connection:

1. One row per counter: `RowsChecked`, `RowsToChange` (the rows the migration will update), `StoredTooHigh`,
   `StoredTooLow`, and `TotalAbsoluteDrift`, the sum of |stored - recounted| (in points for the five score columns).
2. Every row that will change: the counter, the row's id, its stored value and the recounted one.
3. The comments by place (post, paragraph, chapter): "several places" and "no place" should not appear.

After the deploy, statements 1 and 2 report nothing to change.

### Google sign-up: the user name, `isNewAccount` and checking a user name (#69)

A new account made with Google used to get `sarduser` and six digits as its user name, the public handle
(`/profile/{userName}`, the @handle on profiles, comments and lists). Now the handle comes from the person's Google name
when that is written in Latin letters, the app learns that the account is new, and it can check a user name before
saving it. Existing accounts keep their names. Nothing tells the member that a name was made for them.

**`POST /api/identity/google-login`** answers one more field, `isNewAccount`:

```json
{ "accessToken": "...", "expiresFor": "...", "passwordReset": false, "isNewAccount": true }
```

- `true` only on the sign-in that created the account. Every other sign-in answers `false`: the next Google sign-ins,
  a Google sign-in that links or takes over an existing account, and `POST /api/identity/Login`.
- On `true` the app shows «اختر اسم المستخدم» once. The field is prefilled with the account's `userName` (from
  `GET /api/User/my-profile`), or empty when that is the fallback: `sarduser` and six digits, `^sarduser[0-9]{6}$`.
  Skipping keeps the handle the account has. Choosing one is `PATCH /api/User/update-me` with `UserName`, as always
  (its rename history keeps the old `/profile/sarduser…` link working).
- The web's Google sign-in (`/google-callback`) makes accounts the same way; its redirect doesn't carry the flag.

**The handle from the Google name** (`Application/Users/GoogleUserNames.cs`):

1. The name is Google's full name, or the given and family names when it has none. Never the email address.
2. Every letter must be Latin once its accents are stripped. One Arabic (or other non-Latin) letter, and the name gives
   no handle: nothing is transliterated. A name without any Latin letter gives none either.
3. Lower case, accents stripped (é → e; the Latin letters that have none to strip as written without one: ß → ss,
   æ → ae, ø → o, ł → l, đ → d, þ → th, ı → i), styled letters as plain ones (𝓢 → s, Ｓ → s). Digits stay in the words.
4. Words (split by spaces, `-`, `_`, `.` and dashes) are joined with `-`. Every other character is dropped: apostrophes,
   brackets and other punctuation, symbols, emoji. So a handle never has `--` or a `-` at either end.
5. Longer than 20 characters: cut after the last whole word that fits, unless that leaves fewer than 3 characters;
   then cut at 20.
6. The handle must pass update-me's rules as it is (the same code, `UserNameCheck`): 3 to 20 characters, only
   `a-z A-Z 0-9 - . _ +`, no `@`, not a reserved name, not starting with `deleted-`. A name that breaks one (a two-letter
   name like «Al», «Blocked») gives no handle.
7. In use: `-2`, `-3` … up to `-20`, the handle giving up letters at its end so the whole stays within 20 characters.
   In use means an account holds the name now, in any letter case, or a member gave it up (the rename history, which
   keeps their old profile links opening them). The twenty are looked up in one query.
8. Otherwise (no handle from the name, or all twenty in use): `sarduser` and six digits, as before, skipping one in use
   the same way: a member who chooses a handle in «اختر اسم المستخدم» leaves their `sarduser` one in the history.
   All the handles a sign-up may try, these included, are looked up in that one query.

Counting the names members gave up is a deliberate difference from update-me and the check below, which let a member
take such a name by hand (its old links then open them). A name made for a newcomer never takes over an existing
member's old profile links on its own, `/profile/sarduser…` ones included.

| Google name | Handle |
|---|---|
| «Shahd Elattar» | `shahd-elattar`; the next «Shahd Elattar» gets `shahd-elattar-2`, then `-3`… |
| «Shahd Elattar», when a member renamed away from `shahd-elattar` | `shahd-elattar-2`: `/profile/shahd-elattar` still opens that member |
| «Zoë Saldaña» | `zoe-saldana` |
| «Jean-Luc O'Brien ✨» | `jean-luc-obrien` |
| «Agent 47» | `agent-47` |
| «Maria de los Angeles Garcia» | `maria-de-los-angeles` (20 characters) |
| «Christopher Alexander» (21) | `christopher` |
| «Mohamed Abdelrahman», when `mohamed-abdelrahman` is taken | `mohamed-abdelrahma-2` |
| «شهد العطار», «Shahd شهد», «✨», «Al» | `sarduser` and six digits |

Two first sign-ins that take the same handle at the same moment both succeed: the database's unique index refuses the
second account, and that sign-in takes the next handle.

**`GET /api/User/username-available?userName=…`** (signed in): whether the caller could take a user name now, that is
what `PATCH /api/User/update-me` would answer it with, through the same checks in the same order. Always 200 when
signed in:

```json
{ "available": false, "code": "UserNameTaken", "message": "اسم المستخدم مستخدم بالفعل، اختر اسمًا آخر" }
```

The first refusal is the answer:

| Order | `code` | When | `message` |
|---|---|---|---|
| 1 | `InvalidUserName` | `userName` missing, empty or blank | «اختر اسم مستخدم» |
| 1 | `InvalidUserName` | fewer than 3 or more than 20 characters | «يجب أن يكون اسم المستخدم من 3 إلى 20 حرفًا» |
| 1 | `InvalidUserName` | an `@` | «لا يمكن أن يحتوي اسم المستخدم على الرمز @» |
| 1 | `ReservedUserName` | `blocked`, `my-profile`, `username-available`, `followers-list` or `following-list`, in any letter case | «اسم المستخدم هذا محجوز، اختر اسماً آخر» |
| 2 | `ReservedUserName` | starts with `deleted-`, in any letter case | «لا يمكن أن يبدأ اسم المستخدم بـ deleted-، فهذه البداية محجوزة للحسابات المحذوفة» |
| 3 | `UserNameTaken` | another account holds it, in any letter case | «اسم المستخدم مستخدم بالفعل، اختر اسمًا آخر» |
| 4 | `InvalidUserName` | another character than `a-z A-Z 0-9 - . _ +` (a space, Arabic letters, `!`…) | «اسم المستخدم يجب أن يتكوّن من أحرف إنجليزية وأرقام فقط، ويمكن إضافة الرموز - . _ +» |
| | `null`, with `"available": true` | none of these | «اسم المستخدم متاح» |

- The caller's own name, in any letter case, is available (a change of case is a rename update-me accepts). It is read
  from the account, not from the token, which may carry a name changed since. Only a name from before today's rules
  that breaks one of order 1 (say 21 characters) is refused as update-me would refuse saving it.
- A name another member gave up is available, as update-me lets anyone take it: their old profile link then opens the
  new holder, since a member who holds a name now wins over the rename history. Google sign-up never picks such a name
  for a new account on its own (above).
- The messages are update-me's own. For the same name update-me refuses with the same message: those of order 1 in
  its `ValidationFailed` problem, the one of order 4 after «تعذّر تحديث الملف الشخصي.».
- 401 when signed out. 429 `TooManyRequests` past 60 checks a minute from one address: debounce the typing (e.g.
  300 ms) and treat a 429 as "not known yet", leaving it to update-me when the member saves.
- It only checks: saving is update-me, which checks again (someone may take the name in between).
- `username-available` is reserved because `GET /api/User/username-available` would shadow a profile with that name.
  Nobody had it (production answered 404 for that path before).

### A new novel can start as a draft (#76)

`POST /api/myworks` takes an optional form field `isDraft` (default `false`, so the web is unchanged): `true` creates the novel as a draft, as `PATCH /api/myworks/{id}/draft` makes one (hidden from every public list and the rankings, listed with `isDraft: true` in `GET /api/myworks` and `/api/myworks/{id}`, published with `PATCH /api/myworks/{id}/publish`); the response has `isDraft` (`null` when refused), and an edit (`PATCH /api/myworks/{id}`) now refuses a title, summary or genre list with creating's rules and messages.

### Chapter format v1: the chapter's text, for the app and the web (#74)

The server stores and serves a chapter's text in one format, whoever sends it: the app's editor, the web's, or any
client. The app's editor leads; the web follows. Anything else in what is sent is dropped, and paragraphs stored
before this are served in the format too.

**A chapter is a list of paragraphs**, each with a kind (`contentType`) and content. On the wire, the `content` of
`POST /api/novel/{novelId}/chapter` and `PATCH /api/novel/{novelId}/chapter/{chapterId}` is HTML:

| `contentType` | What it is | On the wire | Stored and served `content` |
|---|---|---|---|
| `text` | a normal paragraph (the default) | `<p>…</p>` | inline content |
| `center` | a centered paragraph: a poem, a title inside the chapter, a sign | `<p data-kind="center">…</p>` | inline content |
| `quote` | a set-off block: a letter, a message, a memory, a voice from elsewhere | `<p data-kind="quote">…</p>` | inline content |
| `break` | a scene break | `<hr>` or `<p data-kind="break"></p>` | `* * *` |
| `image` | a picture | `<img src="https://…">` alone in its `<p>`, or bare; with a caption, `<p data-kind="image"><img src="https://…">the caption</p>` | the picture's address; the caption in `caption` |

**Inline content** (text, center, quote): text, `<strong>`, `<em>`, `<u>`, `<s>`, and `<br>` for a line break inside
the paragraph (a poem's lines, a list of messages). Text is HTML-escaped: `&amp;`, `&lt;`, `&gt;`, and `&nbsp;` for a
no-break space. A `"` stays as it is.

**What the server drops or rewrites**, so that what is stored is only the above, written one way:

- Every attribute. On `<p>` only `data-kind` is read (any letter case; an unknown kind is `text`), on `<img>` only
  `src`. So the web editor's `class="min-h-[1em]"`, `style`, `dir`, `on…` handlers and links' `href` all go.
- `<b>` becomes `<strong>` and `<i>` becomes `<em>`. Nested marks are written in one order (`strong`, `em`, `u`, `s`),
  and a mark around no text is dropped.
- Every other tag is unwrapped and its text kept: `span`, `a`, `font`, `sup`, ... Blocks (`div`, headings, list items,
  table cells, `blockquote`, ...) end a paragraph: their text becomes paragraphs of its own (of the kind of the `<p>`
  around them, if any).
- Elements whose content isn't text go with all of it: `script`, `style`, `iframe`, `object`, `embed`, `svg`, `math`,
  `video`, `audio`, `canvas`, `noscript`, `template`, form fields (`input`, `select`, `textarea`, `button`), and
  comments.
- Pictures: only an absolute `http`/`https` address is kept (normalized and escaped, so `HTTPS://Host/a b.png` becomes
  `https://host/a%20b.png`); any other picture (relative, `data:`, `javascript:`) is dropped. A picture among a
  paragraph's text becomes an `image` paragraph of its own, the text before and after it staying paragraphs of the
  `<p>`'s kind. In `<p data-kind="image">` with one picture, the rest of the `<p>` is the caption, stored as plain text
  (its formatting dropped, on one line). A `<p data-kind="image">` without exactly one picture is read as text.
- A break's content is ignored: it is always stored as `* * *`, so a reader that doesn't know kinds shows a line.
- Spaces a reader can't see: runs of spaces and tabs become one space; spaces at a paragraph's start or end, or next to
  a `<br>`, go, and so do `<br>`s at a paragraph's start or end. A no-break space is kept.
- Empty paragraphs (only spaces, `&nbsp;` or `<br>`) are dropped. A break never is.
- Plain text (no `<p>`) works as it always did: each block between blank lines is a `text` paragraph. A single line
  break in the text is a `<br>`, and a blank line ends the paragraph, inside a `<p>` too.

The format is canonical: sending back exactly what was read changes nothing, and cleaning it again gives the same.

**Reading back.** The author's chapter, `GET /api/myworks/{workId}/chapters/{chapterId}` (what an editor loads), the
reader's, `GET /api/novel/{novelId}/chapter/{chapterId}`, and the answer of `POST /api/novel/{novelId}/chapter`
list the paragraphs in order, each:

```json
{ "id": "…", "content": "كان <strong>الليل</strong> طويلاً<br>والريح تعوي.", "contentType": "text",
  "caption": null, "orderIndex": 0, "commentsCount": 2 }
{ "id": "…", "content": "https://files.example/map.png", "contentType": "image", "caption": "خريطة المدينة",
  "orderIndex": 1, "commentsCount": 0 }
{ "id": "…", "content": "* * *", "contentType": "break", "caption": null, "orderIndex": 2, "commentsCount": 0 }
```

`caption` is new: an image's caption as plain text (show it as text, not HTML), `null` for every other paragraph.
A paragraph's quote next to a comment on it, `paragraphExcerpt` (the comment context,
`GET /api/notifications/comment/{id}`, and a member's comment list, `GET /api/User/{userName}/comments`), is plain
text: its words, an image's caption, `null` for a break.

**Saving keeps a paragraph by its words.** A saved paragraph keeps its id and its comments when the edited chapter
has a paragraph with the same words (#15), wherever it moved. Its kind and inline formatting are not part of its words:
changing a `text` paragraph to `center`, or making a word bold, keeps it, and the stored content and kind are updated.
A picture is kept by its address and caption, a break as a break. A changed word makes a new paragraph, and the old
one goes with its comments, as before.

**Limits.** The 100,000-character limit counts the text readers see (a break and the markup count nothing), so
formatting doesn't take a writer's room: «يجب ألا يتجاوز نص الفصل 100000 حرف». As sent, formatting included, the text
may be up to 400,000 characters: «يجب ألا يتجاوز نص الفصل مع تنسيقه 400000 حرف». Both are 400 `ValidationFailed`.

**For the web.** It renders `content` as HTML today: it should show each paragraph by `contentType` (center, quote,
a separator for `break`, a picture and its `caption` for `image`, whose `content` is an address, not HTML). The
editor's output needs no change, and loading paragraphs into it as `<p>` blocks works as before. The SEO worker
(`cloudflare-worker/seo-worker.js`) keeps only paragraphs with `contentType` `text`, so center and quote paragraphs are
missing from the pages it renders until it learns the kinds.

#### Paragraphs stored before the format: the maintenance

`POST /api/admin/chapters/clean-format` (admins) runs the same cleaning over the stored paragraphs. **It is a dry run
unless `dryRun=false`.**

1. `POST /api/admin/chapters/clean-format`: nothing changes; the report says what the real pass would do.
2. Look at the report: the counts, `examples` (before and after), `skipped` (and why), `pictures`.
3. `POST /api/admin/chapters/clean-format?dryRun=false`: the real pass. A call works about 20 seconds; while
   `nextCursor` isn't `null`, call again with `&after=<nextCursor>` (the dry run too, on a large database). Add up the
   counts of the calls. Running it again changes nothing that is already converted.

`batchSize` (default 50, at most 200) is how many chapters it reads at a time. The report:

| Field | |
|---|---|
| `chaptersChecked`, `chaptersChanged`, `paragraphsChecked`, `paragraphsUnchanged` | what it went through, and what is already format v1 |
| `paragraphsChanged` | the sum of the next four |
| `paragraphsRewritten` | stored again in the format (the editor's `class`, markup, kind, a picture's address), same id and comments |
| `paragraphsSplit`, `paragraphsAdded` | a picture among a paragraph's text split out (or blocks inside one paragraph): the paragraph keeps its first part with words, with its id and comments; the other parts are new paragraphs |
| `emptyParagraphsRemoved` | paragraphs without words or picture and without comments, removed as empty paragraphs are |
| `hashesFixed` | content already in the format whose stored `ContentHash` didn't match it |
| `paragraphsSkipped`, `skipped` | left as they are, with `reason`: `VisibleTextChanged` (the cleaning would change the words a reader sees: `wordsBefore`, `wordsAfter`), `PictureDropped` (a picture without an http(s) address), `EmptyWithComments` |
| `pictures` | paragraphs holding pictures, how many pictures are `kept` (as image paragraphs) and `notKept` (their paragraphs are skipped), and those paragraphs |
| `legacyChapterContent` | chapters still holding text in the old `Chapters.Content` column, and how many of them have no paragraphs |
| `failures` | chapters whose conversion failed (nothing of them was saved): run again |
| `nextCursor` | where to continue; `null` at the end |

It never changes the words a reader sees: a paragraph whose words or picture the cleaning would lose is skipped and
reported, so nothing disappears unseen. It keeps `ContentHash` (SHA-256 of the stored content) right, and leaves the
paragraphs' `updatedAt` (the author didn't change them). Each chapter is converted in its own transaction, holding the
chapter's text as an author's save does: a save of that chapter waits for it, or it for the save, and it reads the
paragraphs again inside. Readers already get every paragraph in the format before it runs; the pass makes the stored
text match. Until it runs, a stored paragraph with a picture among its text is served as its text alone.

**The old `Chapters.Content` column.** Chapters kept their text there before paragraphs; nothing reads it. Until now,
every save copied the request's raw HTML into it; it is no longer written, and a save of the chapter's text clears it.
The maintenance reports how many chapters still hold text there (`legacyChapterContent`), and how many of those have
no paragraphs (readers see none of that text); it doesn't change the column. Dropping it is a later decision.

The migration `AddChapterParagraphCaption` adds the nullable column `ChapterParagraphs.Caption`. The cleaning is
`Application/Chapters/Paragraphs/ChapterFormat.cs`; the wiki's articles (parked) are to go through it when they come
back.

### Editing a chapter from two places: the revision, the dry run, and a status alone (#75)

The app's editor keeps a chapter on the phone while the author writes, offline too, and sends it when she saves; she may
also edit it on the web or on another phone. A save from an older copy no longer overwrites newer text silently.

**The revision.** A chapter has a `revision` (1 when created) and an `updatedAt` (UTC, with `Z`), in the author's
chapter (`GET /api/myworks/{workId}/chapters/{chapterId}`), the author's chapter list
(`GET /api/myworks/{workId}/chapters`) and the answer of `POST /api/novel/{novelId}/chapter`. Readers' payloads don't
have them.

- The revision goes up by one with every save that changes the chapter's title or text: words, a paragraph's kind or
  formatting, the order. A change of status alone doesn't move it, and nor does a save that sends the title and text
  exactly as they are (the web sends both with every save, also when only the status changed).
- `updatedAt` is when the chapter was last saved: created, or any successful `PATCH`, a status change included (not
  the scheduled publish, #77). Chapters from before this have revision 1 and `updatedAt` = when they were created.

**Saving.** `PATCH /api/novel/{novelId}/chapter/{chapterId}` takes an optional `baseRevision`, the revision the editor's
copy was loaded at:

```json
{ "title": "الفصل الأول", "content": "<p>…</p>", "status": "Published", "baseRevision": 7 }
```

- `baseRevision` given with a title or text, and not the chapter's revision (someone saved it since): **409**, and
  nothing is saved, not even the status:

  ```json
  { "code": "ChapterChanged", "message": "حُفظ هذا الفصل من مكان آخر بعد أن فتحته. حمّل آخر نسخة منه قبل أن تحفظ.", "revision": 8 }
  ```

  Reload the chapter (its `revision` is the one in the answer), let the author merge, and save with the new one.
- `baseRevision` left out: no check, as before, so the current web keeps working.
- A success answers `{ "success": true, "message": "حُفظ الفصل", "revision": 8 }`, the revision after the save; keep it
  as the copy's new `baseRevision`.
- The check and the save are one step: saves of one chapter run one at a time (the format maintenance's too), each
  reading the chapter inside, and the revision is written only over the one it read. Of two saves from the same
  revision, one is saved and the other gets the 409.

**A status alone.** `title` and `content` can be left out together to change only the status:
`PATCH …/chapter/{chapterId}` with `{ "status": "Published" }` publishes the chapter (or `"Draft"` unpublishes it)
without sending its text. It needs no `baseRevision` (one sent is not checked: it doesn't touch the text), and the
revision stays. Sent, the title and the text are both needed, with their limits (50 characters; 100,000 visible
characters, 400,000 as sent). Neither a status, a schedule (`publishAt`, #77) nor a title and text: 400
«أرسل حالة الفصل أو موعد نشره، أو عنوانه ونصه». Every combination is in #77's table below.

**Know before saving what a save would delete.** `PATCH …/chapter/{chapterId}?dryRun=true`, with the body the save
would send, runs everything the save runs (the checks above, the revision included, and the matching of paragraphs)
and saves nothing:

```json
{ "paragraphsRemoved": 2, "commentsDeleted": 3,
  "removed": [ { "paragraphId": "…", "commentsCount": 2 }, { "paragraphId": "…", "commentsCount": 1 } ] }
```

`removed` lists every paragraph the save would remove (its words changed or it was deleted), in the chapter's order,
those without comments too. A paragraph's `commentsCount` is the comments on it with all the replies below them that
readers see: comments their authors deleted aren't counted, as the comment counters don't count them (#66).
`commentsDeleted` is their sum. Use it to warn before a save that deletes comments, e.g. «سيُحذف ٣ تعليقات لأنك غيّرت
فقرات عليها تعليقات», and to point at those paragraphs.

| HTTP | `code` | When |
|---|---|---|
| 200 | | saved (`{ success, message, revision }`), or the dry run's answer |
| 400 | `ValidationFailed` | a rule above; the messages are the validators' |
| 400 | `PublishAtInPast`, `ScheduleRequiresDraft` | a schedule refused (#77) |
| 403 | `NotOwner` | not the novel's author |
| 404 | `NovelNotFound`, `ChapterNotFound` | no such novel, or the chapter isn't one of its chapters |
| 409 | `ChapterChanged` | `baseRevision` isn't the chapter's revision; `revision` is the one it has |

The migration `AddChapterRevision` adds `Chapters.Revision` (1 for every existing chapter) and `Chapters.UpdatedAt`
(set to when each was created).

### Writer extras: word counts and scheduled publishing (#77)

**Word counts.** `wordsCount` (a number, or `null`) is in what only the author sees:

| Where | `wordsCount` |
|---|---|
| `GET /api/myworks/{workId}/chapters`, each item; `GET /api/myworks/{workId}/chapters/{chapterId}`; the chapter `POST /api/novel/{novelId}/chapter` returns | the chapter's |
| `GET /api/myworks`, each item; `GET /api/myworks/{id}` | the novel's: all its chapters, drafts included; `0` without chapters |

- **The rule** (`Application/Chapters/Paragraphs/ChapterWords.cs`): a word is a run of the text a reader sees between
  whitespace that has at least one letter or digit, in any script. The text a reader sees is the chapter's paragraphs
  in chapter format v1 (#74): a `text`, `center` or `quote` paragraph's text without its markup (a tag inside a word
  doesn't split it; a `<br>` or a new paragraph does; `&nbsp;` is a space), an image's `caption` (text the author wrote
  and readers see), and nothing for a `break`, whose `* * *` only stands for the separator. So punctuation alone isn't a
  word, punctuation stuck to a word doesn't make another, and tashkeel and tatweel never split a word. A chapter is
  counted when it is created and with every save of its text, in the same transaction as its paragraphs.

  | Text | Words |
  |---|---|
  | `قَالَ الرَّجُلُ: «مَرْحَبًا يَا صَدِيقِي!» — ثُمَّ مَضَى ...` | 7 (the dash and the dots alone aren't words) |
  | `جمـــيل جدًا ـــ` | 2 (tatweel alone isn't a word) |
  | `عام ٢٠٢٦ أو 2026، بنسبة 15%` | 6 (numbers are words) |
  | `كل<strong>مة</strong> واحدة`, `سطر<br>سطر` | 2, 2 |
  | a `break`; an `image` without a caption; with the caption `خريطة المدينة` | 0; 0; 2 |

- **Chapters from before word counts** are counted by the app itself, once, in the background after the deploy
  (`ChapterWordsBackfillService`): nothing to run by hand. It counts each chapter's stored paragraphs as the API serves
  them (paragraphs stored before format v1 included), only chapters without a count (so a save meanwhile keeps its
  own), resumes after a restart, and finds nothing at later starts. Until a chapter is counted, its `wordsCount` is
  `null`, and so is its novel's (only the first moments after the deploy).

**Scheduled publishing.** A draft can be scheduled to publish itself with `publishAt`, a time to come in UTC: on
`POST /api/novel/{novelId}/chapter` and `PATCH /api/novel/{novelId}/chapter/{chapterId}` (JSON), e.g.
`"publishAt": "2026-10-06T18:00:00Z"` (a time with an offset, `+03:00`, is converted; one with neither is UTC).

- Only a draft: created with `status: "Draft"`, or a chapter that is a draft and stays one in that save (a save that
  unpublishes a chapter may schedule it).
- On `PATCH`: leaving `publishAt` out keeps the schedule (every edit does, the web's too), `null` cancels it, a new time
  moves it. Publishing by hand (`status: "Published"`) clears it. `publishAt` can be sent alone, or with `status`
  alone, without the title and text (the table below).
- Refused, before anything is saved, with 400 `{ "code", "message" }` (a dry run, `?dryRun=true`, too):
  `PublishAtInPast` «موعد النشر يجب أن يكون في المستقبل» (now or past), `ScheduleRequiresDraft` «يمكن تحديد موعد نشر
  للمسودات فقط» (created published, a published chapter, or a save that publishes it). The chapter is checked as it is
  when the save runs: one the schedule has just published is no longer a draft.
- `publishAt` (UTC with `Z`, `null` when not scheduled) is in the author's chapter list and chapter and in what `POST`
  returns. Readers' payloads don't have it.
- **When it falls due**, the chapter is published as its author publishes it: held as the author's save holds it
  (#75), so a scheduled publish and a save of the same chapter run one after the other, each reading what the other
  stored; then stored by the author's save code (status, `publishedAt` when it actually came out, the schedule
  cleared), and followed by what a publish by hand does (`ChapterStatusEffects`): sequences, the chapter count, the
  novel's `lastUpdatedAt`, the privilege window, and readers' notification and push (#33, #39). Its title and text
  don't change, so neither its `revision` nor its `updatedAt` moves. Once: a run that comes after another run, or after
  the author published, rescheduled or cancelled it, finds it no longer due. A chapter that came out before
  (unpublished, then scheduled) doesn't tell readers again. A deleted novel's chapters aren't published.
- **Who publishes it.** The scheduler in the API (`ScheduledChapterPublishingService`) runs when the app starts and then
  every minute. The host (runasp.net) stops the app while it is idle, and then nothing runs: a chapter that fell due
  meanwhile comes out at the scheduler's first run when a request starts the app again, or, if it comes first, before a
  request reading its novel is answered (the novel page by slug or id, its chapter list, a chapter; the author's work,
  chapter list and chapter), so that answer has it. Lists of many novels (search, rankings, library, my works) don't
  publish; they show it after that first run.
- **Limits.** While the app is stopped, a due chapter waits for the next request, and its readers' notifications with
  it; `publishedAt` is when it came out, not `publishAt`. To publish on time at quiet hours, keep the app awake: an uptime
  monitor calling `GET /api/app/config` every 5 minutes is enough. A save that sends `status: "Draft"` right after the
  chapter was published on schedule turns it back into a draft, as between two saves the last status sent wins (the web
  editor sends the status it shows); readers were told once, and the counts follow. A save without a status leaves it
  published, so the app should send `status` only when the author changes it.

**What a `PATCH …/chapter/{chapterId}` body may hold** (#75 and #77). A field left out is not changed; `publishAt: null`
is sent, and cancels the schedule.

| Body | Saves | `baseRevision` | `revision` |
|---|---|---|---|
| `title` and `content`, with or without `status` and `publishAt` | the title and text, and the status and schedule sent | optional; when sent and not the chapter's: 409 `ChapterChanged`, nothing saved | one more when the title or text changed |
| `status` alone | the status | not needed, not checked | stays |
| `publishAt` alone (a time, or `null`) | the schedule | not needed, not checked | stays |
| `status` and `publishAt` | both | not needed, not checked | stays |
| `title` without `content`, or `content` without `title` | nothing: 400 `ValidationFailed` «اكتب نص الفصل» / «اكتب عنوان الفصل» | | |
| none of `title`, `content`, `status`, `publishAt` | nothing: 400 `ValidationFailed` «أرسل حالة الفصل أو موعد نشره، أو عنوانه ونصه» | | |

Every successful save moves `updatedAt`, a change of status or schedule alone too; the scheduled publish moves neither
`updatedAt` nor `revision`.

The migration `AddChapterWordsCountAndPublishAt` adds the nullable columns `Chapters.WordsCount` and
`Chapters.PublishAt`, and a filtered index on `PublishAt` for the scheduler.
