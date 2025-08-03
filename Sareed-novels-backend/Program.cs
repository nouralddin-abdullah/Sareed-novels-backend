using Application.Extensions;
using Domain.Entities;
using Infrastructure.Extensions;
using Sareed_novels_backend.Extensions;
using Sareed_novels_backend.Middlewares;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

//Add Services Extensions
builder.Services.AddInfrastructure(builder.Configuration);
builder.AddPresentation();
builder.Services.AddApplication();
var app = builder.Build();
var scope = app.Services.CreateScope();
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

app.UseCors("AllowFrontend");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
