# .NET 10 Migration Plan for Sareed Novels Backend

## Executive Summary

This document outlines the comprehensive plan to upgrade the Sareed Novels Backend solution from .NET 8 to .NET 10 (Preview). The solution consists of 4 projects with a clean architecture pattern (Domain → Application → Infrastructure → API), totaling approximately 87,864 lines of code.

**Migration Approach:** Incremental Migration (dependency-order based)
**Target Framework:** .NET 10.0 (Preview)
**Estimated Timeline:** 1-2 days
**Risk Level:** Low to Medium

## Migration Strategy: Incremental Approach

**Why Incremental?**
- 4 distinct projects with clear dependency relationships
- Total codebase of ~88K LOC requires careful testing at each stage
- Clean architecture allows for bottom-up migration
- Minimizes risk by validating each layer before proceeding
- Solution remains buildable after each phase

**Alternative Considered:** Big Bang migration was considered but rejected due to:
- Substantial codebase size
- Complex Entity Framework integration requiring careful testing
- Production system requiring stable intermediate states

## Dependency Analysis

### Project Dependency Graph

```
Sareed-novels-backend.csproj (API Layer)
├── Infrastructure.csproj
│   ├── Application.csproj
│   │   └── Domain.csproj
│   └── Domain.csproj
└── Application.csproj
    └── Domain.csproj
```

### Migration Order (Bottom-Up)

**Phase 1: Domain Layer** (0 dependencies)
- Domain.csproj
- Risk: Low
- LOC: 1,559

**Phase 2: Application Layer** (depends on Domain)
- Application.csproj  
- Risk: Low-Medium
- LOC: 11,905

**Phase 3: Infrastructure Layer** (depends on Domain + Application)
- Infrastructure.csproj
- Risk: Medium (database/EF Core)
- LOC: 71,892

**Phase 4: API Layer** (depends on all)
- Sareed-novels-backend.csproj
- Risk: Low (minimal code)
- LOC: 2,508

## Package Update Strategy

### Critical Updates Required

All package updates from the assessment **MUST** be applied. No deferrals.

| Package | Current | Target | Projects | Priority | Notes |
|---------|---------|--------|----------|----------|-------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.0 | 10.0.0 | Domain | **CRITICAL** | Identity system |
| Microsoft.Extensions.Logging.Abstractions | 9.0.5 | 10.0.0 | Application | High | Logging |
| Microsoft.AspNetCore.Authentication.Google | 8.0.17 | 10.0.0 | Infrastructure | High | OAuth |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.0 | 10.0.0 | Infrastructure | **CRITICAL** | Auth |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.6 | 10.0.0 | Infrastructure | **CRITICAL** | Database |
| Microsoft.EntityFrameworkCore.Tools | 9.0.6 | 10.0.0 | Infrastructure | **CRITICAL** | Migrations |
| Microsoft.EntityFrameworkCore.Design | 9.0.6 | 10.0.0 | API | **CRITICAL** | Design-time |

### Deprecated Package

⚠️ **FluentValidation.AspNetCore 11.3.1** (Application.csproj)
- Status: Deprecated
- Action: Keep current version (11.3.1 is latest, officially deprecated but still functional)
- Note: FluentValidation team recommends manual registration instead of AspNetCore package
- Future: Consider migrating to manual registration in Phase 2

### Compatible Packages (No Update Needed)

- AutoMapper 14.0.0 ✅
- AWSSDK.S3 4.0.4.1 ✅
- FluentValidation 12.0.0 ✅
- Google.Apis.Auth 1.70.0 ✅
- MediatR 12.5.0 ✅
- Serilog.AspNetCore 9.0.0 ✅
- Swashbuckle.AspNetCore 6.6.2 ✅
- OpenSearch.Client 1.8.0 ✅

## Phase-by-Phase Migration Plan

---

## Phase 1: Domain Layer Migration

### 1.1 Pre-Migration Checklist

- [ ] Verify on "Upgrading" branch
- [ ] Ensure clean working directory
- [ ] Backup current state (Git commit)
- [ ] Review Domain.csproj dependencies

### 1.2 Update Domain.csproj

**File:** `Domain\Domain.csproj`

**Changes:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>  <!-- Changed from net8.0 -->
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.0" />
    <!-- Changed from 8.0.0 -->
  </ItemGroup>
</Project>
```

### 1.3 Build and Test

```bash
cd Domain
dotnet restore
dotnet build --no-restore
```

**Expected:** Clean build with no errors

### 1.4 Validation Checklist

- [ ] Domain.csproj builds without errors
- [ ] No warnings related to framework version
- [ ] Identity entity configurations still valid
- [ ] All domain entities compile successfully

### 1.5 Commit Changes

```bash
git add Domain/Domain.csproj
git commit -m "Phase 1: Migrate Domain layer to .NET 10"
```

**Rollback Plan:** `git revert HEAD` if issues occur

---

## Phase 2: Application Layer Migration

### 2.1 Pre-Migration Checklist

- [ ] Phase 1 completed successfully
- [ ] Domain layer building cleanly
- [ ] Review Application.csproj dependencies

### 2.2 Update Application.csproj

**File:** `Application\Application.csproj`

**Changes:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>  <!-- Changed from net8.0 -->
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AutoMapper" Version="14.0.0" />
    <PackageReference Include="AWSSDK.S3" Version="4.0.4.1" />
    <PackageReference Include="FluentValidation" Version="12.0.0" />
    <PackageReference Include="FluentValidation.AspNetCore" Version="11.3.1" />
    <!-- ⚠️ Deprecated but keeping - see notes -->
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.0.0" />
    <PackageReference Include="Google.Apis.Auth" Version="1.70.0" />
    <PackageReference Include="MediatR" Version="12.5.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <!-- Changed from 9.0.5 -->
    <PackageReference Include="NSwag.Annotations" Version="14.4.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Domain\Domain.csproj" />
  </ItemGroup>
</Project>
```

### 2.3 Address FluentValidation Deprecation (Optional)

**Note:** FluentValidation.AspNetCore is deprecated. Current code will continue to work, but consider this future refactoring:

**Current (deprecated):**
```csharp
services.AddFluentValidation(fv => fv.RegisterValidatorsFromAssemblyContaining<T>());
```

**Future (manual registration):**
```csharp
services.AddValidatorsFromAssemblyContaining<T>();
services.AddFluentValidationAutoValidation();
```

**Decision:** Keep current implementation for this upgrade. Schedule refactoring as separate task.

### 2.4 Build and Test

```bash
cd Application
dotnet restore
dotnet build --no-restore
```

### 2.5 Validation Checklist

- [ ] Application.csproj builds without errors
- [ ] MediatR handlers compile successfully
- [ ] Validators (FluentValidation) work correctly
- [ ] Service interfaces compatible
- [ ] DTOs and mappings (AutoMapper) valid
- [ ] No breaking changes in logging abstractions

### 2.6 Commit Changes

```bash
git add Application/Application.csproj
git commit -m "Phase 2: Migrate Application layer to .NET 10 and update packages"
```

**Rollback Plan:** `git revert HEAD` or `git reset --hard HEAD~2` to undo both phases

---

## Phase 3: Infrastructure Layer Migration

### 3.1 Pre-Migration Checklist

- [ ] Phases 1-2 completed successfully
- [ ] Review Infrastructure.csproj dependencies
- [ ] **CRITICAL:** Backup database before testing migrations
- [ ] Note current EF Core migration state

### 3.2 Update Infrastructure.csproj

**File:** `Infrastructure\Infrastructure.csproj`

**Changes:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>  <!-- Changed from net8.0 -->
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AWSSDK.Core" Version="4.0.0.14" />
    <PackageReference Include="AWSSDK.S3" Version="4.0.4.1" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.0" />
    <!-- Changed from 8.0.17 -->
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <!-- Changed from 8.0.0 -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
    <!-- Changed from 9.0.6 -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="10.0.0" />
    <!-- Changed from 9.0.6 -->
    <PackageReference Include="OpenSearch.Client" Version="1.8.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Application\Application.csproj" />
    <ProjectReference Include="..\Domain\Domain.csproj" />
  </ItemGroup>
</Project>
```

### 3.3 Entity Framework Core 10.0 Considerations

**Potential Breaking Changes:**

1. **DbContext Configuration**
   - Review `ApplicationDbContext.cs`
   - Check for obsolete API usage
   - Validate connection string handling

2. **Migrations**
   - Existing migrations remain valid
   - DO NOT regenerate migrations
   - Test applying migrations to test database

3. **Query Translation**
   - EF Core 10 may have improved query translation
   - Review complex LINQ queries (especially in repositories)
   - Test privilege system queries (PrivilegeService.cs has complex logic)

4. **Identity Integration**
   - ASP.NET Core Identity 10.0 compatibility
   - Review `ApplicationDbContext : IdentityDbContext<User>`
   - Test user authentication flow

### 3.4 Critical Files to Review

**Priority 1 (Must Review):**
- `Infrastructure\Persistence\ApplicationDbContext.cs` - DbContext configuration
- `Infrastructure\Services\TransactionManager.cs` - Database transactions
- `Infrastructure\Services\WalletService.cs` - Financial transactions
- `Infrastructure\Services\PrivilegeService.cs` - Complex queries

**Priority 2 (Should Review):**
- All repository implementations (30+ files)
- `Infrastructure\Services\JwtService.cs` - JWT authentication
- `Infrastructure\Extensions\ServiceCollectionExtensions.cs` - DI registration

### 3.5 Build and Test

```bash
cd Infrastructure
dotnet restore
dotnet build --no-restore
```

### 3.6 Database Migration Testing

**⚠️ CRITICAL: Use test database**

```bash
# In Sareed-novels-backend directory
dotnet ef database update --project ../Infrastructure --context ApplicationDbContext --connection "YOUR_TEST_CONNECTION_STRING"
```

### 3.7 Validation Checklist

- [ ] Infrastructure.csproj builds without errors
- [ ] No EF Core warnings about obsolete APIs
- [ ] DbContext initializes correctly
- [ ] Existing migrations apply successfully to test database
- [ ] Repository queries execute without errors
- [ ] JWT authentication services compile
- [ ] OAuth (Google) authentication configuration valid
- [ ] Transaction manager works correctly
- [ ] Wallet service financial operations safe

### 3.8 Specific Test Cases

**Test 1: Basic Database Connection**
```csharp
// Verify DbContext can connect
using (var context = new ApplicationDbContext(options))
{
    var canConnect = await context.Database.CanConnectAsync();
    Assert.True(canConnect);
}
```

**Test 2: Privilege System Queries**
```csharp
// Test complex LINQ in PrivilegeService
var lockedChapters = await privilegeService.GetLockedChaptersAsync(novelId);
```

**Test 3: Transaction Management**
```csharp
// Test database transactions
await using var transaction = await transactionManager.BeginTransactionAsync();
// ... operations
await transaction.CommitAsync();
```

### 3.9 Commit Changes

```bash
git add Infrastructure/Infrastructure.csproj
git commit -m "Phase 3: Migrate Infrastructure layer to .NET 10 and update EF Core + Auth packages"
```

**Rollback Plan:** `git revert HEAD` or restore from Phase 2 commit

---

## Phase 4: API Layer Migration

### 4.1 Pre-Migration Checklist

- [ ] Phases 1-3 completed successfully
- [ ] All layers building cleanly
- [ ] Review Sareed-novels-backend.csproj

### 4.2 Update Sareed-novels-backend.csproj

**File:** `Sareed-novels-backend\Sareed-novels-backend.csproj`

**Changes:**
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>  <!-- Changed from net8.0 -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
      <!-- Changed from 9.0.6 -->
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Application\Application.csproj" />
    <ProjectReference Include="..\Infrastructure\Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

### 4.3 ASP.NET Core 10.0 Considerations

**Potential Changes in Program.cs:**

1. **Minimal API improvements** - No code changes needed
2. **Middleware registration** - Review for deprecated APIs
3. **Endpoint routing** - Should be compatible
4. **Health checks** - Verify configuration

**Files to Review:**
- `Sareed-novels-backend\Program.cs` - Main startup
- `Sareed-novels-backend\Extensions\WebApplicationBuilderExtensions.cs` - Extensions
- All controllers (30+ files) - Controller/action attributes

### 4.4 Build Full Solution

```bash
# From solution root
dotnet restore
dotnet build --no-restore
```

### 4.5 Validation Checklist

- [ ] Full solution builds without errors
- [ ] No framework-related warnings
- [ ] Swagger/OpenAPI documentation generates
- [ ] All controllers compile
- [ ] Middleware pipeline valid
- [ ] Dependency injection configuration correct
- [ ] CORS policy applies correctly
- [ ] JWT authentication middleware works

### 4.6 Runtime Testing

**Start the application:**
```bash
cd Sareed-novels-backend
dotnet run
```

**Verify:**
- [ ] Application starts without errors
- [ ] Swagger UI accessible at `/swagger`
- [ ] Health check endpoint responds (if configured)
- [ ] Database connection successful
- [ ] Logging working (Serilog)

### 4.7 Commit Changes

```bash
git add Sareed-novels-backend/Sareed-novels-backend.csproj
git commit -m "Phase 4: Migrate API layer to .NET 10 - Migration Complete!"
```

---

## Post-Migration Testing Strategy

### Level 1: Build Verification

- [ ] `dotnet build` succeeds for all projects
- [ ] Zero build errors
- [ ] Zero framework-related warnings

### Level 2: Unit Tests (if available)

```bash
dotnet test --configuration Release
```

### Level 3: Integration Testing

**Critical User Flows to Test:**

1. **Authentication**
   - [ ] User registration
   - [ ] User login (JWT)
   - [ ] Google OAuth login
   - [ ] Token refresh

2. **Core Novel Features**
   - [ ] Create novel
   - [ ] Publish chapter
   - [ ] Read chapter
   - [ ] Search novels

3. **Privilege System** (High complexity - your current file)
   - [ ] Enable privilege for novel
   - [ ] Subscribe to privilege
   - [ ] Check locked chapters
   - [ ] Daily unlock process
   - [ ] Transaction rollback on error

4. **Wallet/Points System**
   - [ ] Request recharge
   - [ ] Approve recharge (admin)
   - [ ] Transfer points
   - [ ] Request withdrawal
   - [ ] Transaction history

5. **Gift System**
   - [ ] Send gift
   - [ ] View leaderboards
   - [ ] Gift transaction recording

6. **Notifications**
   - [ ] Receive notification
   - [ ] Mark as read
   - [ ] Notification count

### Level 4: Performance Testing

- [ ] Response times comparable to .NET 8
- [ ] Database query performance unchanged or improved
- [ ] Memory usage stable

### Level 5: Database Integrity

- [ ] All migrations applied successfully
- [ ] No data corruption
- [ ] Foreign key constraints valid
- [ ] Transaction isolation levels correct

---

## Breaking Changes Catalog

### .NET 10 Potential Breaking Changes

**Based on .NET 10 Preview announcements:**

1. **Obsolete API Removals**
   - Some .NET 8 obsolete APIs removed in .NET 10
   - **Action:** Review compiler warnings during build

2. **Entity Framework Core 10**
   - Improved query translation (generally beneficial)
   - Some edge-case LINQ queries may translate differently
   - **Action:** Test complex queries in PrivilegeService

3. **ASP.NET Core Identity 10**
   - Minor changes to default password policies
   - **Action:** Verify existing user data migrates correctly

4. **JSON Serialization**
   - System.Text.Json improvements
   - **Action:** Test API response serialization

5. **Nullable Reference Types**
   - Stricter enforcement in .NET 10
   - **Action:** Already enabled in projects, should be safe

### Package-Specific Breaking Changes

**Entity Framework Core 9.0 → 10.0:**
- Review: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes

**ASP.NET Core Authentication:**
- JWT validation may have stricter defaults
- Google OAuth endpoints stable

**FluentValidation.AspNetCore:**
- Already deprecated, no new breaking changes

---

## Risk Management

### Risk Matrix

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| EF Core query translation changes | Low | Medium | Test all repositories |
| JWT auth compatibility issues | Low | High | Test authentication flow |
| Database migration failures | Very Low | Very High | Use test database first |
| Performance regression | Low | Medium | Performance benchmarks |
| Transaction rollback issues | Very Low | Very High | Test WalletService thoroughly |
| Privilege system logic breaks | Low | High | Comprehensive testing |

### Rollback Strategy

**Per-Phase Rollback:**
Each phase is committed separately, allowing:
```bash
git revert <commit-hash>  # Revert specific phase
git reset --hard <commit-hash>  # Reset to before phase
```

**Full Rollback:**
```bash
git checkout master  # Return to pre-upgrade state
git branch -D Upgrading  # Delete upgrade branch
```

**Production Rollback:**
- Keep .NET 8 binaries available
- Blue-green deployment recommended
- Database migrations are forward-only (plan carefully)

---

## Success Criteria

### Migration is complete when:

#### Technical Criteria
- [ ] All 4 projects target net10.0
- [ ] All 7 package updates applied
- [ ] Solution builds with zero errors
- [ ] Solution builds with zero warnings (framework-related)
- [ ] All existing database migrations apply successfully
- [ ] Application starts and runs without errors

#### Quality Criteria
- [ ] All critical user flows tested and passing
- [ ] Authentication and authorization working
- [ ] Database transactions commit/rollback correctly
- [ ] Privilege system calculations accurate
- [ ] Wallet financial operations safe
- [ ] No data corruption observed
- [ ] No performance degradation (< 5% acceptable)

#### Operational Criteria
- [ ] Swagger documentation accessible
- [ ] Logging functional (Serilog)
- [ ] Error handling works as expected
- [ ] Production deployment tested in staging
- [ ] Team trained on any new patterns/features
- [ ] Monitoring dashboards updated

---

## Timeline Estimate

| Phase | Duration | Tasks | Notes |
|-------|----------|-------|-------|
| **Phase 1: Domain** | 30 min | Update project, build, test | Low risk |
| **Phase 2: Application** | 1 hour | Update project, address validation, test | FluentValidation decision |
| **Phase 3: Infrastructure** | 4-6 hours | Update project, review EF Core, test database, review services | Highest risk phase |
| **Phase 4: API** | 1 hour | Update project, verify startup, test endpoints | Final integration |
| **Post-Migration Testing** | 4-6 hours | Integration tests, performance tests | Comprehensive |
| **Documentation & Handoff** | 1 hour | Update README, deployment docs | Knowledge transfer |
| **Buffer** | 2-4 hours | Unexpected issues | Contingency |

**Total Estimated Time:** 14-20 hours (2-3 work days)

**Recommended Schedule:**
- **Day 1 Morning:** Phases 1-2
- **Day 1 Afternoon:** Phase 3 (Infrastructure - allow ample time)
- **Day 2 Morning:** Phase 4 + Initial testing
- **Day 2 Afternoon:** Comprehensive integration testing
- **Day 3:** Performance testing, documentation, staging deployment

---

## Additional Considerations

### .NET 10 SDK Installation

**Before starting:**
```bash
dotnet --list-sdks
```

**Required:** .NET 10.0 SDK (Preview)
**Download:** https://dotnet.microsoft.com/download/dotnet/10.0

### Global.json Considerations

If `global.json` exists in solution root:
- Update SDK version to 10.0.x
- Remove or update any SDK version pinning

### CI/CD Pipeline Updates

**Update build pipelines to:**
- Use .NET 10 SDK
- Update Docker base images (if applicable)
- Update deployment scripts

**Example GitHub Actions:**
```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v3
  with:
    dotnet-version: '10.0.x'
```

### Production Deployment Checklist

- [ ] Staging environment upgraded first
- [ ] Database backup completed
- [ ] Rollback plan documented
- [ ] Monitoring alerts configured
- [ ] Team notified of deployment window
- [ ] Feature flags for gradual rollout (if available)

---

## Appendix: Package Update Reference

### Consolidated Package Updates

```xml
<!-- Domain.csproj -->
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.0" />

<!-- Application.csproj -->
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />

<!-- Infrastructure.csproj -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="10.0.0" />

<!-- Sareed-novels-backend.csproj -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0" />
```

### Package Changelogs

**Review before migrating:**
- [EF Core 10 What's New](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [ASP.NET Core 10 What's New](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [.NET 10 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)

---

## Contact & Support

**Upgrade Questions:**
- Review this plan carefully before each phase
- Test in isolated environment first
- Commit frequently with descriptive messages

**Emergency Rollback:**
- Use Git to revert changes per phase
- Restore database from backup if needed
- Contact team lead for production issues

---

**Plan Version:** 1.0  
**Created:** 2024  
**Target .NET Version:** 10.0 (Preview)  
**Solution:** Sareed Novels Backend  
**Branch:** Upgrading  

---

**Ready to begin Phase 1? Review checklist and proceed when ready!**