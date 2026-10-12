using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimWise.Api.Data;

public sealed class SqliteClaimWiseDbContext(DbContextOptions<SqliteClaimWiseDbContext> options)
    : ClaimWiseDbContext(options);

public sealed class PostgresClaimWiseDbContext(DbContextOptions<PostgresClaimWiseDbContext> options)
    : ClaimWiseDbContext(options);

// Design-time factories are used only by `dotnet ef migrations ...`; the app itself uses DI (see Program.cs).
// The connection strings here are throw-away placeholders: migrations are generated without connecting.
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteClaimWiseDbContext>
{
    public SqliteClaimWiseDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteClaimWiseDbContext>()
            .UseSqlite("Data Source=design-time.db",
                o => o.MigrationsHistoryTable("__EFMigrationsHistory"))
            .Options);
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresClaimWiseDbContext>
{
    public PostgresClaimWiseDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresClaimWiseDbContext>()
            .UseNpgsql("Host=localhost;Database=claimwise_design;Username=design;Password=design")
            .Options);
}
