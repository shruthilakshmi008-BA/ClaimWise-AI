using System.Text.Json;
using ClaimWise.Api.Dtos;
using ClaimWise.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Data;

/// <summary>
/// Development convenience: ingests the fictional sample policies (samples/policies/manifest.json) through the normal
/// ingestion pipeline when the policy table is empty, so a fresh checkout can demonstrate retrieval immediately.
/// </summary>
public static class SamplePolicySeeder
{
    private sealed record ManifestEntry(string File, string? PolicyFamily, string? Version, DateOnly? EffectiveDate);

    public static async Task ApplyAsync(IServiceProvider services, string directory, CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimWiseDbContext>();
        if (await db.PolicyDocuments.AnyAsync(ct)) return;

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SamplePolicySeeder");
        var policies = scope.ServiceProvider.GetRequiredService<IPolicyService>();
        var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(await File.ReadAllTextAsync(manifestPath, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        foreach (var entry in entries)
        {
            var path = Path.GetFullPath(Path.Combine(directory, entry.File));
            if (!path.StartsWith(Path.GetFullPath(directory), StringComparison.Ordinal) || !File.Exists(path)) continue;

            await using var stream = File.OpenRead(path);
            var form = new FormFile(stream, 0, stream.Length, "File", Path.GetFileName(path));
            await policies.UploadAsync(new UploadPolicyRequest
            {
                File = form, UploadedBy = "seed", PolicyFamily = entry.PolicyFamily, Version = entry.Version, EffectiveDate = entry.EffectiveDate
            }, ct);
        }
        logger.LogInformation("Ingested {Count} sample policy documents from {Directory}", entries.Count, directory);
    }
}
