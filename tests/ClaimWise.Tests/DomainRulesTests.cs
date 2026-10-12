using ClaimWise.Api.Domain;
using Xunit;

namespace ClaimWise.Tests;

public class ClaimRulesTests
{
    private static Claim MakeClaim(ClaimType type, string? diagnosis, string? procedure, params DocumentType[] docs)
    {
        var claim = new Claim { ClaimType = type, DiagnosisCode = diagnosis, ProcedureCode = procedure };
        foreach (var d in docs)
            claim.Documents.Add(new ClaimDocument { DocumentType = d, FileName = $"{d}.pdf" });
        return claim;
    }

    [Theory]
    [InlineData(ClaimType.Outpatient, 3)]
    [InlineData(ClaimType.Inpatient, 4)]
    [InlineData(ClaimType.Pharmacy, 3)]
    [InlineData(ClaimType.Dental, 2)]
    public void RequiredDocuments_HaveExpectedCountPerClaimType(ClaimType type, int expectedCount)
    {
        Assert.Equal(expectedCount, ClaimRules.RequiredDocumentsFor(type).Count);
    }

    [Fact]
    public void Validate_CompleteOutpatientClaim_IsComplete()
    {
        var claim = MakeClaim(ClaimType.Outpatient, "J06.9", "PRC-101",
            DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport);

        var result = ClaimRules.Validate(claim);

        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingDocuments);
        Assert.Empty(result.MissingFields);
    }

    [Fact]
    public void Validate_ReportsEveryMissingDocument()
    {
        var claim = MakeClaim(ClaimType.Inpatient, "K35.8", "PRC-310", DocumentType.ClaimForm);

        var result = ClaimRules.Validate(claim);

        Assert.False(result.IsComplete);
        Assert.Equal("Invoice,MedicalReport,DischargeSummary", string.Join(",", result.MissingDocuments));
    }

    [Fact]
    public void Validate_OutpatientWithoutDiagnosis_ReportsMissingField()
    {
        var claim = MakeClaim(ClaimType.Outpatient, null, "PRC-101",
            DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport);

        var result = ClaimRules.Validate(claim);

        Assert.False(result.IsComplete);
        Assert.Equal("DiagnosisCode", string.Join(",", result.MissingFields));
    }

    [Fact]
    public void Validate_PharmacyClaimDoesNotNeedDiagnosisOrProcedure()
    {
        var claim = MakeClaim(ClaimType.Pharmacy, null, null,
            DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.Prescription);

        Assert.True(ClaimRules.Validate(claim).IsComplete);
    }

    [Fact]
    public void Validate_WhitespaceOnlyDiagnosisCountsAsMissing()
    {
        var claim = MakeClaim(ClaimType.Inpatient, "   ", "PRC-310",
            DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport, DocumentType.DischargeSummary);

        Assert.Contains("DiagnosisCode", ClaimRules.Validate(claim).MissingFields);
    }
}

public class ClaimWorkflowTests
{
    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.InReview)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.NeedsInformation)]
    [InlineData(ClaimStatus.InReview, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.InReview, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.InReview, ClaimStatus.NeedsInformation)]
    [InlineData(ClaimStatus.NeedsInformation, ClaimStatus.InReview)]
    public void AllowedTransitions_AreAllowed(ClaimStatus from, ClaimStatus to)
    {
        Assert.True(ClaimWorkflow.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Approved)]   // a reviewer must look at it first
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.NeedsInformation, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.InReview)]    // final states cannot be reopened
    [InlineData(ClaimStatus.Rejected, ClaimStatus.InReview)]
    public void DisallowedTransitions_AreRejected(ClaimStatus from, ClaimStatus to)
    {
        Assert.False(ClaimWorkflow.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ClaimStatus.Approved, true)]
    [InlineData(ClaimStatus.Rejected, true)]
    [InlineData(ClaimStatus.Submitted, false)]
    [InlineData(ClaimStatus.InReview, false)]
    [InlineData(ClaimStatus.NeedsInformation, false)]
    public void IsFinal_OnlyForApprovedAndRejected(ClaimStatus status, bool expected)
    {
        Assert.Equal(expected, ClaimWorkflow.IsFinal(status));
    }

    [Theory]
    [InlineData(ClaimStatus.Approved, true)]
    [InlineData(ClaimStatus.Rejected, true)]
    [InlineData(ClaimStatus.NeedsInformation, true)]
    [InlineData(ClaimStatus.InReview, false)]
    public void RequiresNote_ForDecisionsAndInformationRequests(ClaimStatus target, bool expected)
    {
        Assert.Equal(expected, ClaimWorkflow.RequiresNote(target));
    }
}
