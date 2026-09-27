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
