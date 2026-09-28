# Sard backend

ASP.NET Core API for Sard (سرد, [sardnovels.com](https://www.sardnovels.com)), an Arabic web-novel platform. The
layout, commands and house rules are in [CLAUDE.md](CLAUDE.md).

## Configuration

Secrets come from host configuration (environment variables in production, `dotnet user-secrets` locally), never
from the `appsettings*.json` files. An environment variable `Section__Key` sets the setting `Section:Key`.

### Mobile app config: `GET /api/app/config`

The Android app reads this at startup: to force an update when an API change breaks older builds, to suggest one
when a newer build is out, and to show a maintenance message. It is anonymous and cacheable for five minutes
(`Cache-Control: public, max-age=300`).

```json
{ "android": { "minVersion": "1.0.0", "latestVersion": "1.0.0" }, "maintenance": { "enabled": false, "messageAr": null } }
```

| Key | Default | Meaning |
|---|---|---|
| `AppConfig:Android:MinVersion` | `1.0.0` | The oldest app version that still works with the API; older builds must update. |
| `AppConfig:Android:LatestVersion` | `1.0.0` | The newest version on Google Play; older builds may offer an update. |
| `AppConfig:Maintenance:Enabled` | `false` | While `true`, the app shows the maintenance message. |
| `AppConfig:Maintenance:MessageAr` | `null` | The Arabic text of that message; empty or `null` lets the app use its own. |

- Versions are `major.minor.patch`, digits only, and `MinVersion` can't be above `LatestVersion`. A value that breaks
  either rule makes the endpoint answer 500 and log why, instead of telling every installed app something wrong.
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
subscriptions, withdrawals) until purchases or recharges bring it back up; points the user already gave away (a
gift's author) are not clawed back. A purchase Google voids before anyone claims it is recorded so it can never be
credited.

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
email `support@sardnovels.com` instead.

What deletion does (`IAccountDeletionService`, one transaction): the user row stays, anonymized, so comments, reviews
and posts keep an author shown as «مستخدم محذوف» (user name `deleted-<id>`); email, phone, bio, links, photo and banner
(also from storage, after the commit), password, external sign-ins and old user names are removed. Their novels are
soft-deleted. Library, reading lists (with others' follows of them), follows, notifications to them, likes (counters
adjusted), privilege subscriptions, devices, preferences and blocks are deleted; notifications they caused lose their
name and photo. Open reports about them close as `AccountDeleted`. The wallet balance is forfeited (set to zero, ledger
type `BalanceForfeited`) and pending withdrawals are cancelled; the ledger and Play purchases stay. Deleted accounts
have no profile (404) and are left out of search, supporters and follower lists.

The `deleted-` user name prefix is reserved for them (`UserNameRules.LooksDeleted`, ignoring case), so a client can tell
a deleted author by `userName` alone and hide the profile link, report and block: sign-up, Google sign-up and renames
to such a name are refused with code `ReservedUserName` (register: 400 `result.code`; update-me: 400 `code`).
