using System.Text.Json.Serialization;
using ClaimWise.Api.Data;
using ClaimWise.Api.Infrastructure;
using ClaimWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Controllers + show enums as text ("InReview") in JSON instead of numbers.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Built-in OpenAPI document generation; the Scalar page (development only) renders it as interactive docs.
builder.Services.AddOpenApi();

// Consistent JSON error bodies (RFC 7807 "problem details").
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddSingleton(TimeProvider.System);

// The connection string is read when the context is first created (not at startup)
// so that tests can swap in a temporary database.
builder.Services.AddDbContext<ClaimWiseDbContext>((serviceProvider, options) =>
{
    var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                           ?? "Data Source=claimwise.db";
    options.UseSqlite(connectionString);
});

builder.Services.AddScoped<IClaimService, ClaimService>();

// CORS lets the React dev server (a different port) call this API from the browser.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Create the database on first run. Stage 1 uses EnsureCreated for simplicity;
// real migrations are introduced before deployment (see docs/decisions.md).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimWiseDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (!app.Environment.IsEnvironment("Testing") && app.Configuration.GetValue<bool>("Seed:Enabled"))
        await SeedData.ApplyAsync(db);
}

app.UseExceptionHandler();

// Baseline response hardening. The API only returns JSON, so a locked-down CSP costs nothing.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    if (!context.Request.Path.StartsWithSegments("/scalar"))
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();          // JSON description of the API at /openapi/v1.json
    app.MapScalarApiReference(); // interactive docs at /scalar/v1
}

app.UseCors("Frontend");
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Makes the Program class visible to the integration tests.
public partial class Program { }
