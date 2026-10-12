namespace ClaimWise.Api.Domain;

/// <summary>
/// The allowed status changes for a claim. Keeping this in one place means the
/// rules are easy to read, easy to change, and easy to unit test.
/// </summary>
public static class ClaimWorkflow
{
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> Transitions = new()
    {
        [ClaimStatus.Submitted] = [ClaimStatus.InReview, ClaimStatus.NeedsInformation],
        [ClaimStatus.InReview] = [ClaimStatus.NeedsInformation, ClaimStatus.Approved, ClaimStatus.Rejected],
        [ClaimStatus.NeedsInformation] = [ClaimStatus.InReview],
        [ClaimStatus.Approved] = [],
        [ClaimStatus.Rejected] = []
    };

    public static IReadOnlyList<ClaimStatus> AllowedNext(ClaimStatus from) => Transitions[from];

    public static bool CanTransition(ClaimStatus from, ClaimStatus to) => Transitions[from].Contains(to);

    public static bool IsFinal(ClaimStatus status) => Transitions[status].Length == 0;

    /// <summary>Statuses that need a written note from the reviewer.</summary>
    public static bool RequiresNote(ClaimStatus target) =>
        target is ClaimStatus.Approved or ClaimStatus.Rejected or ClaimStatus.NeedsInformation;
}
