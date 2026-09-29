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
clients show the gift message box only when `gifts.messageMaxLength` is there (an older server has no `gifts`).

```json
{
  "android": { "minVersion": "1.0.0", "latestVersion": "1.0.0" },
  "ios": { "minVersion": "1.0.0", "latestVersion": "1.0.0" },
  "maintenance": { "enabled": false, "messageAr": null },
  "gifts": { "messageMaxLength": 200 }
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
| 403 | `Blocked` | a message, and the novel's author blocked the sender (`IsBlockedAsync(authorId, senderId)`, as comments) | «لا يمكنك إرسال رسالة إلى هذا الكاتب.» |

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
- 200 answers `{ "success": true, "message": "تم تعديل مراجعتك", "review": { ... } }`, where `review` is exactly the
  item `GET /api/{novelId}` lists in `reviews` (the same shape and values), to replace it in place: the same `id`,
  `likeCount` and `createdAt`, the new `content`, `isSpoiler` and `totalAverageScore`, and `updatedAt`.
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
`field`: `ProfilePhoto` or `ProfileBanner`.

**My works.** `totalAverageScore` in `GET /api/myworks`, `/api/myworks/{id}` and `/api/myworks/user/{userId}` has its
fraction (`3.75`), like the other novel lists; it was a whole number.

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
(`CannotFollowSelf`, `PostNotFound`...). Adding a novel a list already has stays 400 `AlreadyInList`. Concurrent
requests (a double tap, a retry) change the state once: the others get the 204, never a 500.

**Counts include blocked members' comments** (a decision, not a bug). A paragraph's `commentsCount` (the reader's
marker), a chapter's `totalCommentsCount` and a post's `commentsCount` count every comment, including those by members
the viewer blocked, while the comment lists leave those out for the viewer. So a marker can say 3 while the sheet
lists 2. The counts are stored per paragraph, chapter and post, shared by every viewer; subtracting per viewer would cost
a query per item, so it isn't done. Clients should treat these counts as "about this many" and not as the list's
length: page through the list with its own `totalItemsCount`, which does leave blocked members out.

### Library: removing a novel, and muting one novel's new chapters (#33)

Every novel a reader opens joins her library (`POST /api/library/track-progress/{chapterId}`), and every chapter
published in a library novel notifies her, in the app and by push. Now she can take a novel out of her library, or keep
it and mute its new chapters. Both act for the signed-in reader (`Authorization: Bearer`; 401 without); errors are
`{ "code", "message" }` with an Arabic message.

| Request | Answer |
|---|---|
| `DELETE /api/library/novel/{novelId}` | 204 No Content, no body. Deletes her progress entry for the novel, nothing else. Also 204 when the novel isn't in her library (already as asked, like the idempotent writes since #25) |
| `PATCH /api/library/novel/{novelId}`, body `{ "notifyNewChapters": true }` or `false` | 204 No Content, no body, also when it was so already. 404 `NotInLibrary` «هذه الرواية ليست في مكتبتك.» when the novel isn't in her library (never added, or removed). 400 `ValidationFailed` when the body has no boolean `notifyNewChapters` |

New fields (additive):

| Where | Field |
|---|---|
| `GET /api/library/reading-progress`, each item | `notifyNewChapters` (bool); `lastChapterPublishedAt` (UTC with `Z`, e.g. `"2026-09-29T21:57:47.1234567Z"`, or `null` when the novel has no published chapter) |
| `GET /api/library/novel/{novelId}/progress`, in `progress` | `notifyNewChapters` (bool) |

- **Muted** (`notifyNewChapters: false`): a chapter published in that novel, new or a draft published later, creates
  no `NewChapterInLibrary` notification for her, so no push either. The novel stays in her library with its progress;
  reading it doesn't turn notifications back on. Her other novels, other readers and her push preferences are
  unchanged; the push still also follows her `chapters` push group.
- **Removed**: `reading-progress` no longer lists it and `novel/{novelId}/progress` answers `{ "hasProgress": false }`;
  her `libraryNovelsCount` goes down by one. Reading a chapter of the novel again adds it back through track-progress
  as before: a new entry at that chapter, with notifications on. For «تراجع», hold the DELETE until the undo bar
  closes, or undo with `POST /api/library/track-progress/{lastReadChapterId}` (her position comes back, with
  notifications on and `lastReadAt` now).
- **`lastChapterPublishedAt`**: the newest of the novel's published chapters, read from the chapters with the page
  (not stored). «فصول جديدة» when it is later than `lastReadAt`; both are UTC (`lastReadAt` is sent without the `Z`,
  as before). Chapters have no publish date, so this is the chapter's creation time: exact for a chapter published as
  it was written, but a chapter saved as a draft and published later carries the time the draft was created. If she
  read the novel between those two times, no badge shows for it (she still gets its notification and push).
- **Schema**: `UserNovelProgress.NotifyNewChapters bit NOT NULL DEFAULT 1`, migration `AddLibraryNotifyNewChapters`:
  every existing entry keeps its notifications.
