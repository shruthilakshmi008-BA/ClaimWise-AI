namespace ClaimWise.Rag.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion: combines ranked lists whose raw scores are not comparable (BM25 vs cosine) by using only
/// rank positions. score(d) = Σ 1 / (k + rank_i(d)).
/// </summary>
public static class ReciprocalRankFusion
{
    public static Dictionary<int, double> Fuse(IEnumerable<IReadOnlyList<int>> rankedLists, int k = 60)
    {
        var fused = new Dictionary<int, double>();
        foreach (var list in rankedLists)
            for (int rank = 0; rank < list.Count; rank++)
                fused[list[rank]] = fused.GetValueOrDefault(list[rank]) + 1.0 / (k + rank + 1);
        return fused;
    }
}
