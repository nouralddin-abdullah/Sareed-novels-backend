# Admin Role Setup Guide

## What Was Added

1. **RoleSeeder** (`Infrastructure/Seed/RoleSeeder.cs`)
   - Automatically creates "Admin" and "User" roles on application startup
   - Roles will be seeded when you run the application

2. **Program.cs** Updated
   - Calls `RoleSeeder.SeedRolesAsync()` on startup
   - Ensures roles exist before the application starts

3. **AdminController** Updated
   - Now requires `[Authorize(Roles = UserRoles.Admin)]`
   - Only users with Admin role can access wallet management endpoints

## How to Assign Admin Role to a User

### Option 1: Using SQL (Recommended for First Admin)

```sql
-- 1. Find your user ID
SELECT Id, UserName, Email FROM AspNetUsers WHERE Email = 'your@email.com';

-- 2. Find the Admin role ID
SELECT Id, Name FROM AspNetRoles WHERE Name = 'Admin';

-- 3. Assign the Admin role to the user
INSERT INTO AspNetUserRoles (UserId, RoleId)
VALUES (
    (SELECT Id FROM AspNetUsers WHERE Email = 'your@email.com'),
    (SELECT Id FROM AspNetRoles WHERE Name = 'Admin')
);
```

### Option 2: Create an Admin Endpoint (Development Only)

Add this temporary endpoint to `IdentityController.cs`:

```csharp
[HttpPost("make-admin/{userId}")]
[Authorize] // Remove this in production or add admin check
public async Task<IActionResult> MakeAdmin(
    string userId,
    [FromServices] UserManager<User> userManager)
{
    var user = await userManager.FindByIdAsync(userId);
    if (user == null)
    {
        return NotFound("User not found");
    }

    var result = await userManager.AddToRoleAsync(user, UserRoles.Admin);
    if (result.Succeeded)
    {
        return Ok($"User {user.UserName} is now an Admin");
    }

    return BadRequest(result.Errors);
}
```

**⚠️ IMPORTANT: Remove this endpoint before production!**

### Option 3: Create Admin During Registration

Modify `CreateUserCommandHandler` to automatically assign Admin role:

```csharp
// After user creation
await _userManager.AddToRoleAsync(user, UserRoles.User); // All users
// await _userManager.AddToRoleAsync(user, UserRoles.Admin); // Uncomment for specific users
```

## Verify Admin Role

After assigning the role, verify it works:

```sql
-- Check user's roles
SELECT u.UserName, u.Email, r.Name as RoleName
FROM AspNetUsers u
JOIN AspNetUserRoles ur ON u.Id = ur.UserId
JOIN AspNetRoles r ON ur.RoleId = r.Id
WHERE u.Email = 'your@email.com';
```

## Testing Admin Endpoints

1. Login with your admin user
2. Use the JWT token in Authorization header
3. Access admin endpoints:
   - `GET /api/admin/recharge/pending`
   - `GET /api/admin/withdraw/pending`
   - `PATCH /api/admin/recharge/{id}/approve`
   - etc.

## What Happens Now

- When you start the application, "Admin" and "User" roles are automatically created
- You need to manually assign the Admin role to your first admin user (using SQL or endpoint)
- All AdminController endpoints now require Admin role
- Regular users cannot access admin endpoints (403 Forbidden)

## Next Steps

1. Run the application (roles will be seeded automatically)
2. Assign Admin role to your account using one of the methods above
3. Login and get JWT token
4. Test admin endpoints with the token
