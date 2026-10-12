using ClaimWise.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClaimWise.Api.Data;

public class ClaimWiseDbContext(DbContextOptions<ClaimWiseDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

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

            // SQLite cannot sort or sum the decimal type, so money is stored as a double.
            // Fine for a demo with 2-decimal amounts; PostgreSQL's numeric type avoids this limit.
            e.Property(c => c.AmountClaimed).HasConversion<double>();

            e.HasIndex(c => c.Status);
            e.HasIndex(c => c.SubmittedAt);
        });

        modelBuilder.Entity<ClaimDocument>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(200).IsRequired();
            e.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30);
            e.HasOne<Claim>().WithMany(c => c.Documents).HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Cascade);
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
