using ClaimWise.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClaimWise.Api.Data;

/// <summary>
/// The application's database model. It is provider-neutral; <see cref="SqliteClaimWiseDbContext"/> and
/// <see cref="PostgresClaimWiseDbContext"/> exist only so each provider owns its own migration set
/// (the generated SQL and column annotations differ between SQLite and PostgreSQL).
/// </summary>
public abstract class ClaimWiseDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();
    public DbSet<PolicyChunk> PolicyChunks => Set<PolicyChunk>();
    public DbSet<AiSummary> AiSummaries => Set<AiSummary>();
    public DbSet<AiSummaryStatementRecord> AiSummaryStatements => Set<AiSummaryStatementRecord>();
    public DbSet<AiSummaryPassage> AiSummaryPassages => Set<AiSummaryPassage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite returns DateTime values with an "unspecified" kind. This marks them as UTC
        // so they are serialized to JSON with a trailing "Z" and clients read them correctly.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Claim>(e =>
        {
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.Property(c => c.ClaimNumber).HasMaxLength(40).IsRequired();
            e.Property(c => c.PatientName).HasMaxLength(100).IsRequired();
            e.Property(c => c.MemberId).HasMaxLength(20).IsRequired();
            e.Property(c => c.PolicyNumber).HasMaxLength(20).IsRequired();
            e.Property(c => c.ProviderName).HasMaxLength(150).IsRequired();
            e.Property(c => c.DiagnosisCode).HasMaxLength(10);
            e.Property(c => c.ProcedureCode).HasMaxLength(10);
            e.Property(c => c.ReviewerName).HasMaxLength(100);
            e.Property(c => c.ReviewerNote).HasMaxLength(1000);

            // Store enums as readable text (e.g. "InReview") rather than numbers.
            e.Property(c => c.ClaimType).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

            // SQLite cannot sort or sum the decimal type, so there money is stored as a double.
            // Fine for a demo with 2-decimal amounts; PostgreSQL uses an exact numeric column instead.
            if (Database.IsSqlite())
                e.Property(c => c.AmountClaimed).HasConversion<double>();
            else
                e.Property(c => c.AmountClaimed).HasPrecision(18, 2);

            e.Property(c => c.Version).IsConcurrencyToken();

            e.HasIndex(c => c.Status);
            e.HasIndex(c => c.SubmittedAt);
        });

        modelBuilder.Entity<ClaimDocument>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(200).IsRequired();
            e.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30);
            e.Property(d => d.Sha256).HasMaxLength(64);
            e.Property(d => d.ContentType).HasMaxLength(100);
            e.Property(d => d.StoredFileName).HasMaxLength(80);
            e.HasOne<Claim>().WithMany(c => c.Documents).HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PolicyDocument>(e =>
        {
            e.Property(d => d.Title).HasMaxLength(200).IsRequired();
            e.Property(d => d.PolicyFamily).HasMaxLength(100);
            e.Property(d => d.Version).HasMaxLength(50);
            e.Property(d => d.FileName).HasMaxLength(200).IsRequired();
            e.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
            e.Property(d => d.UploadedBy).HasMaxLength(100).IsRequired();
            e.HasIndex(d => d.Sha256).IsUnique();
            e.HasMany(d => d.Chunks).WithOne().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PolicyChunk>(e =>
        {
            e.Property(c => c.Text).HasMaxLength(8000).IsRequired();
            e.Property(c => c.Heading).HasMaxLength(300);
            e.Property(c => c.ClauseRef).HasMaxLength(30);
            e.Property(c => c.SectionPath).HasMaxLength(600).IsRequired();
            e.Property(c => c.EmbeddingModel).HasMaxLength(120).IsRequired();
            e.HasIndex(c => new { c.DocumentId, c.Ordinal });
        });

        modelBuilder.Entity<AiSummary>(e =>
        {
            e.Property(s => s.Question).HasMaxLength(600).IsRequired();
            e.Property(s => s.RetrievalQuery).HasMaxLength(1000).IsRequired();
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.ReviewState).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.RequestedBy).HasMaxLength(100).IsRequired();
            e.Property(s => s.GeneratorName).HasMaxLength(100).IsRequired();
            e.Property(s => s.ModelId).HasMaxLength(200);
            e.Property(s => s.EmbeddingModel).HasMaxLength(120).IsRequired();
            e.Property(s => s.AbstainReason).HasMaxLength(2000);
            e.Property(s => s.FailureReason).HasMaxLength(1000);
            e.Property(s => s.ReviewedBy).HasMaxLength(100);
            e.Property(s => s.ReviewNote).HasMaxLength(1000);
            e.HasOne<Claim>().WithMany().HasForeignKey(s => s.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.Statements).WithOne().HasForeignKey(x => x.SummaryId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.Passages).WithOne().HasForeignKey(x => x.SummaryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.ClaimId, s.GeneratedAt });
        });

        modelBuilder.Entity<AiSummaryStatementRecord>(e =>
        {
            e.ToTable("AiSummaryStatements");
            e.Property(x => x.Text).HasMaxLength(1500).IsRequired();
            e.Property(x => x.Origin).HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<AiSummaryPassage>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(10).IsRequired();
            e.Property(x => x.DocumentTitle).HasMaxLength(200).IsRequired();
            e.Property(x => x.DocumentVersion).HasMaxLength(50);
            e.Property(x => x.ClauseRef).HasMaxLength(30);
            e.Property(x => x.Heading).HasMaxLength(300);
            e.Property(x => x.SectionPath).HasMaxLength(600).IsRequired();
            e.Property(x => x.Text).HasMaxLength(8000).IsRequired();
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.Details).HasMaxLength(2000).IsRequired();
            e.Property(a => a.Actor).HasMaxLength(100).IsRequired();
            e.HasOne<Claim>().WithMany(c => c.AuditEntries).HasForeignKey(a => a.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.ClaimId);
        });
    }
}

public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
