namespace ClaimWise.Api.Domain;

public sealed record ClaimValidationResult(
    IReadOnlyList<DocumentType> RequiredDocuments,
    IReadOnlyList<DocumentType> MissingDocuments,
    IReadOnlyList<string> MissingFields)
{
    public bool IsComplete => MissingDocuments.Count == 0 && MissingFields.Count == 0;
}

/// <summary>
/// Business rules for what a complete claim submission looks like.
/// These are fictional rules for a demo, not real insurer policy.
/// </summary>
public static class ClaimRules
{
    public static IReadOnlyList<DocumentType> RequiredDocumentsFor(ClaimType type) => type switch
    {
        ClaimType.Outpatient => [DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport],
        ClaimType.Inpatient => [DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.MedicalReport, DocumentType.DischargeSummary],
        ClaimType.Pharmacy => [DocumentType.ClaimForm, DocumentType.Invoice, DocumentType.Prescription],
        ClaimType.Dental => [DocumentType.ClaimForm, DocumentType.Invoice],
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static ClaimValidationResult Validate(Claim claim)
    {
        var required = RequiredDocumentsFor(claim.ClaimType);
        var present = claim.Documents.Select(d => d.DocumentType).ToHashSet();
        var missingDocs = required.Where(r => !present.Contains(r)).ToList();

        var missingFields = new List<string>();
        bool needsDiagnosis = claim.ClaimType is ClaimType.Outpatient or ClaimType.Inpatient;
        bool needsProcedure = claim.ClaimType is not ClaimType.Pharmacy;

        if (needsDiagnosis && string.IsNullOrWhiteSpace(claim.DiagnosisCode))
            missingFields.Add("DiagnosisCode");
        if (needsProcedure && string.IsNullOrWhiteSpace(claim.ProcedureCode))
            missingFields.Add("ProcedureCode");

        return new ClaimValidationResult(required, missingDocs, missingFields);
    }
}
