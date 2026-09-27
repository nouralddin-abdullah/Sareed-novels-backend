# Sard backend

ASP.NET Core API for Sard (سرد, [sardnovels.com](https://www.sardnovels.com)), an Arabic web-novel platform. The
layout, commands and house rules are in [CLAUDE.md](CLAUDE.md).

## Configuration

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
