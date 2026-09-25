# Sard backend (Sareed-novels-backend)

ASP.NET Core API for Sard (سرد, sardnovels.com), an Arabic web-novel platform. Live in production with real users.
Load the `sard` skill for platform context: production database access, audit findings, roadmap, decisions.

## Layout

Clean architecture: `Domain/` (entities, repo interfaces, exceptions) <- `Application/` (MediatR commands/queries per
feature under `<Feature>/Commands|Queries/<UseCase>/`, DTOs, AutoMapper, FluentValidation) <- `Infrastructure/`
(EF Core `ApplicationDbContext`, repositories, services, migrations) <- `Sareed-novels-backend/` (thin controllers
that call `mediator.Send`). `BackgroundJobs/` is an Azure Functions app that is being retired (roadmap).

## Commands

```bash
dotnet build Sareed-novels-backend.sln          # passes as of 2026-09-24 (warnings only)
dotnet run --project Sareed-novels-backend
dotnet ef migrations add <PascalCaseName> -p Infrastructure -s Sareed-novels-backend
```

## Rules

- **Never commit secrets.** `appsettings*.json` hold placeholders only; real values come from environment/host
  config and `dotnet user-secrets` locally.
- Every admin/maintenance endpoint gets `[Authorize(Roles = UserRoles.Admin)]`. Every endpoint that acts as a user
  gets `[Authorize]` and reads the user via `IUserContext`, never from the request body.
- Don't swallow failures into "empty success" (the search services returned empty results for months after
  OpenSearch died). Log and surface an error, or degrade visibly.
- Counters are incremented atomically in SQL (`ExecuteUpdateAsync(s => s.SetProperty(x => x.C, x => x.C + 1))`),
  not read-modify-write.
- Schema changes only via EF migrations; name them in PascalCase describing the change.
- Ranking/search/wallet logic ships with unit tests (create `Tests/` xUnit project on first need).
- Sard is Arabic-only (no English version planned). Messages the web app shows to users should be Arabic; many API
  messages are still English, so switch them to Arabic when you touch them.
- Arabic text: normalize for matching (strip tashkeel/tatweel; أإآ->ا, ة->ه, ى->ي). Slugs are `<5 hex>-<title-dashes>`.

## Before you say it's done

`dotnet build` passes, tests pass, and for anything touching data you checked the result against production data
with the `sard` skill's read-only DB runner.
