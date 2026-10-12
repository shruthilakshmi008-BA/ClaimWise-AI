namespace ClaimWise.Api.Domain;

public enum ClaimType
{
    Outpatient,
    Inpatient,
    Pharmacy,
    Dental
}

public enum ClaimStatus
{
    Submitted,
    InReview,
    NeedsInformation,
    Approved,
    Rejected
}

public enum DocumentType
{
    ClaimForm,
    Invoice,
    MedicalReport,
    Prescription,
    DischargeSummary
}
