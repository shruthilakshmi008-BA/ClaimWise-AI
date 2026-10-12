using ClaimWise.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimWise.Api.Data;

/// <summary>
/// Inserts a small set of SYNTHETIC claims so the app is not empty on first run.
/// All names, IDs and providers are fictional. Do not put real patient data in this project.
/// </summary>
public static class SeedData
{
    public static async Task ApplyAsync(ClaimWiseDbContext db, CancellationToken ct = default)
    {
        if (await db.Claims.AnyAsync(ct)) return;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // (patient, provider, type, diagnosis, procedure, amount, days ago, status, documents)
        var rows = new (string Patient, string Provider, ClaimType Type, string? Dx, string? Px, decimal Amount, int DaysAgo, ClaimStatus Status, DocumentType[] Docs)[]
        {
            ("Anita Rao",      "Lakeview Demo Clinic",     ClaimType.Outpatient, "J06.9", "PRC-101", 1850.00m,  3, ClaimStatus.Submitted,
                new[] { DocumentType.ClaimForm, DocumentType.Invoice }),
            ("Rahul Menon",    "Riverside Demo Hospital",  ClaimType.Inpatient,  "K35.8", "PRC-310", 48500.00m, 10, ClaimStatus.InReview,
                new[] { DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport, DocumentType.DischargeSummary }),
            ("Meera Iyer",     "Greenleaf Demo Pharmacy",  ClaimType.Pharmacy,   null,    null,     640.50m,    2, ClaimStatus.Submitted,
                new[] { DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.Prescription }),
            ("Karan Shetty",   "BrightSmile Demo Dental",  ClaimType.Dental,     "K02.1", "PRC-220", 3200.00m,  7, ClaimStatus.NeedsInformation,
                new[] { DocumentType.ClaimForm }),
            ("Divya Nair",     "Lakeview Demo Clinic",     ClaimType.Outpatient, null,    "PRC-102", 920.00m,   5, ClaimStatus.Submitted,
                new[] { DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport }),
            ("Suresh Kulkarni","Riverside Demo Hospital",  ClaimType.Inpatient,  "I21.9", "PRC-455", 125000.00m,14, ClaimStatus.Approved,
                new[] { DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport, DocumentType.DischargeSummary }),
            ("Priya Hegde",    "Greenleaf Demo Pharmacy",  ClaimType.Pharmacy,   null,    null,     310.75m,    20, ClaimStatus.Rejected,
                new[] { DocumentType.ClaimForm, DocumentType.Invoice }),
            ("Vikram Pai",     "Harbor Demo Diagnostics",  ClaimType.Outpatient, "R10.4", "PRC-140", 2750.00m,  1, ClaimStatus.Submitted,
                new[] { DocumentType.Invoice }),
        };

        int n = 0;
        foreach (var r in rows)
        {
            n++;
            var submitted = now.AddDays(-r.DaysAgo);
            var claim = new Claim
            {
                ClaimNumber = $"SEED-{n:D3}",
                PatientName = r.Patient,
                MemberId = $"MBR-{100000 + n}",
                PolicyNumber = $"POL-GOLD-{n:D2}",
                ProviderName = r.Provider,
                ClaimType = r.Type,
                DiagnosisCode = r.Dx,
                ProcedureCode = r.Px,
                AmountClaimed = r.Amount,
                ServiceDate = today.AddDays(-r.DaysAgo - 2),
                Status = r.Status,
                SubmittedAt = submitted,
                UpdatedAt = submitted,
            };

            claim.AuditEntries.Add(new AuditEntry
            {
                Action = "ClaimCreated",
                Details = "Synthetic demo claim loaded by the seeder.",
                Actor = "seed",
                Timestamp = submitted
            });

            foreach (var doc in r.Docs)
            {
                claim.Documents.Add(new ClaimDocument
                {
                    DocumentType = doc,
                    FileName = $"{doc}-{n:D3}.pdf",
                    ReceivedAt = submitted
                });
            }

            db.Claims.Add(claim);
        }

        await db.SaveChangesAsync(ct);

        // Replace the temporary SEED- numbers with the same CW-000001 format real claims use.
        foreach (var claim in await db.Claims.ToListAsync(ct))
            claim.ClaimNumber = $"CW-{claim.Id:D6}";
        await db.SaveChangesAsync(ct);
    }
}
