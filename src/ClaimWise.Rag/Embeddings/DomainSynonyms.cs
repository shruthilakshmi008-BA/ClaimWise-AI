namespace ClaimWise.Rag.Embeddings;

/// <summary>
/// A small, hand-written synonym table for the claims domain. Terms (already tokenised and stemmed) in a group fold
/// to the group head. It is intentionally tiny and inspectable; it is used by the local embedder and the reranker.
/// </summary>
public static class DomainSynonyms
{
    private static readonly Dictionary<string, string> Map = BuildSynonyms();

    public static string Fold(string term) => Map.GetValueOrDefault(term, term);

    private static Dictionary<string, string> BuildSynonyms()
    {
        // group head => members (all already in stemmed form where it matters)
        var groups = new Dictionary<string, string[]>
        {
            ["inpatient"] = ["hospitalization", "hospitalisation", "hospitalized", "admission", "admit", "hospital", "overnight", "ward"],
            ["outpatient"] = ["clinic", "consultation", "doctor", "physician", "gp", "visit", "ambulatory"],
            ["pharmacy"] = ["medication", "medicine", "drug", "prescribed", "rx", "dispensed", "prescription"],
            ["dental"] = ["dentist", "tooth", "teeth", "orthodontic", "oral", "filling", "extraction"],
            ["excluded"] = ["exclusion", "exclude", "excludes", "ineligible", "unpaid", "noncovered"],
            ["covered"] = ["cover", "reimburse", "reimbursement", "payable", "eligible", "benefit"],
            ["costshare"] = ["copay", "co-payment", "copayment", "coinsurance", "co-insurance", "deductible", "excess"],
            ["preauth"] = ["pre-authorization", "preauthorization", "pre-approval", "preapproval", "precertification", "authorization", "authorisation"],
            ["limit"] = ["maximum", "cap", "ceiling", "capped", "sublimit", "annual"],
            ["waiting"] = ["waiting-period", "qualifying", "probation"],
            ["emergency"] = ["urgent", "accident", "trauma", "ambulance"],
            ["diagnostic"] = ["imaging", "scan", "mri", "xray", "x-ray", "laboratory", "lab", "pathology", "test"],
            ["decision"] = ["decide", "decides", "decided", "approve", "approved", "approval", "reject", "rejection", "deny", "denial", "determination"],
            ["cosmetic"] = ["aesthetic", "elective", "beautification"],
        };

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (head, members) in groups)
        {
            map[TextAnalysis.Stem(head)] = TextAnalysis.Stem(head);
            foreach (var member in members)
                foreach (var term in TextAnalysis.Terms(member, removeStopWords: false))
                    map[term] = TextAnalysis.Stem(head);
        }
        return map;
    }
}
