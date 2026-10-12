using ClaimWise.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClaimWise.Tests;

public sealed class DatabaseMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"claimwise-mig-{Guid.NewGuid():N}.db");

    private SqliteClaimWiseDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqliteClaimWiseDbContext>().UseSqlite($"Data Source={_path}").Options);

    [Fact]
    public async Task Migrate_CreatesSchemaAndRecordsHistory_AndIsIdempotent()
    {
        await using (var db = NewContext())
        {
            await LegacyDatabaseGuard.EnsureNotLegacyAsync(db);
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync(); // second run must be a no-op
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(0, await db.Claims.CountAsync());
        }
    }

    [Fact]
    public async Task Guard_ExplainsHowToRecoverFromAnEnsureCreatedDatabase()
    {
        await using (var legacy = NewContext())
            await legacy.Database.EnsureCreatedAsync(); // what Stage 1 did

        await using var db = NewContext();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyDatabaseGuard.EnsureNotLegacyAsync(db));
        Assert.Contains("delete the database file", ex.Message);
    }

    [Fact]
    public void ModelHasNoPendingChanges_ForEitherProvider()
    {
        // Fails when an entity changes without a matching migration (a classic source of production-only errors).
        using var sqlite = NewContext();
        Assert.False(sqlite.Database.HasPendingModelChanges(), "SQLite model differs from its migration snapshot.");

        using var postgres = new PostgresClaimWiseDbContext(new DbContextOptionsBuilder<PostgresClaimWiseDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        Assert.False(postgres.Database.HasPendingModelChanges(), "PostgreSQL model differs from its migration snapshot.");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
