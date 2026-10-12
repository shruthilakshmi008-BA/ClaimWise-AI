using System.Text.Json.Serialization;
using ClaimWise.Api.Ai;
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

// The provider ("Sqlite" or "PostgreSQL") and connection string are read when the context is first created
// (not at startup) so that tests can swap in a temporary database. Each provider has its own migration set,
// so each is registered as the implementation of the shared ClaimWiseDbContext.
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
switch (databaseProvider.ToLowerInvariant())
{
    case "sqlite":
        builder.Services.AddDbContext<ClaimWiseDbContext, SqliteClaimWiseDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default") ?? "Data Source=claimwise.db"));
        break;
    case "postgresql" or "postgres" or "npgsql":
        builder.Services.AddDbContext<ClaimWiseDbContext, PostgresClaimWiseDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                              ?? throw new InvalidOperationException("ConnectionStrings:Default is required for PostgreSQL.")));
        break;
    default:
        throw new InvalidOperationException($"Unsupported Database:Provider '{databaseProvider}'. Use 'Sqlite' or 'PostgreSQL'.");
}

builder.Services.AddScoped<IClaimService, ClaimService>();

// Policy ingestion, hybrid retrieval and grounded summaries (providers chosen by the "Ai" configuration section).
builder.Services.AddClaimWiseAi(builder.Configuration);
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<IPolicySearchService, PolicySearchService>();
builder.Services.AddScoped<IAiSummaryService, AiSummaryService>();

// Read lazily (not at registration) so configuration added later, e.g. by integration tests, is honoured.
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection(ClaimFileOptions.SectionName).Get<ClaimFileOptions>() ?? new ClaimFileOptions());
builder.Services.AddSingleton<IClaimFileStore, LocalClaimFileStore>();
builder.Services.AddScoped<IClaimDocumentService, ClaimDocumentService>();

// Throttle the expensive endpoints (provider calls, embedding, uploads) per client address.
var permitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 30);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    foreach (var policy in new[] { "ai", "upload" })
        o.AddPolicy(policy, ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) }));
});

// CORS lets the React dev server (a different port) call this API from the browser.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Fail fast on bad provider configuration instead of on the first request.
_ = app.Services.GetRequiredService<ClaimWise.Rag.Embeddings.IEmbeddingProvider>();
_ = app.Services.GetRequiredService<ClaimWise.Rag.Grounding.ISummaryGenerator>();

// Apply pending EF Core migrations at startup (set Database:AutoMigrate=false to run them yourself).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimWiseDbContext>();
    if (app.Configuration.GetValue("Database:AutoMigrate", true))
    {
        await LegacyDatabaseGuard.EnsureNotLegacyAsync(db);
        await db.Database.MigrateAsync();
    }

    if (!app.Environment.IsEnvironment("Testing") && app.Configuration.GetValue<bool>("Seed:Enabled"))
        await SeedData.ApplyAsync(db);

    var policyDir = app.Configuration["Seed:PolicyDirectory"];
    if (!app.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(policyDir))
        await SamplePolicySeeder.ApplyAsync(app.Services, Path.GetFullPath(policyDir, app.Environment.ContentRootPath));
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
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Makes the Program class visible to the integration tests.
public partial class Program { }
