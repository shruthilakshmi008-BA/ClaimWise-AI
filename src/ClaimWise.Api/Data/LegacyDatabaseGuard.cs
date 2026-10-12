using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Data;

/// <summary>
/// Stage 1 created SQLite databases with EnsureCreated, which leaves no migration history. Migrating such a
/// file would fail with an obscure "table already exists" error, so detect it and say what to do instead.
/// </summary>
public static class LegacyDatabaseGuard
{
    public static async Task EnsureNotLegacyAsync(ClaimWiseDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite()) return;

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM sqlite_master WHERE name = 'Claims'), " +
                              "(SELECT COUNT(*) FROM sqlite_master WHERE name = '__EFMigrationsHistory')";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            if (reader.GetInt64(0) > 0 && reader.GetInt64(1) == 0)
                throw new InvalidOperationException(
                    "The SQLite database was created by an earlier version without migrations. " +
                    "It holds only synthetic data: stop the app, delete the database file (see ConnectionStrings:Default, " +
                    "default claimwise.db) and start again so it is recreated with migrations.");
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
