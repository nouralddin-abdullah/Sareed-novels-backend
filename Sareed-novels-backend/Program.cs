using Application.Extensions;
using Domain.Entities;
using Infrastructure.Extensions;
using Infrastructure.Seed;
using Sareed_novels_backend.Extensions;
using Sareed_novels_backend.Middlewares;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

//Add Services Extensions
builder.Services.AddInfrastructure(builder.Configuration);
builder.AddPresentation();
builder.Services.AddApplication();
var app = builder.Build();

// Seed roles on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await RoleSeeder.SeedRolesAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding roles");
    }
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<ExecutedTimeMiddleware>();
app.UseSerilogRequestLogging();
// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    
//}
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Apply CORS conditionally based on request method
app.Use(async (context, next) =>
{
    // Use permissive policy for read-only requests (SEO bots, pre-rendering)
    if (context.Request.Method == "GET" || context.Request.Method == "HEAD")
    {
        var corsService = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Cors.Infrastructure.ICorsService>();
        var corsPolicyProvider = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Cors.Infrastructure.ICorsPolicyProvider>();
        var policy = await corsPolicyProvider.GetPolicyAsync(context, "AllowSEO");
        if (policy != null)
        {
            var corsResult = corsService.EvaluatePolicy(context, policy);
            corsService.ApplyResult(corsResult, context.Response);
        }
    }
    await next();
});

app.UseCors("AllowFrontend");  // Apply strict policy for authenticated requests

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
