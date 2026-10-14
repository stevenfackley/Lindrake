// ============================================================
// OmniSift.Api — Reciprocal Rank Fusion (RRF)
// Pure, side-effect-free function — fully unit-testable.
// Given two ranked lists (vector arm + keyword arm), fuses them
// into a single ranking using the standard RRF formula:
//   score(d) = Σ  1 / (k + rank_i(d))
// where k = 60 (Cormack et al. 2009).
// ============================================================

namespace OmniSift.Api.Services;

/// <summary>A key with its fused RRF score.</summary>
public readonly record struct FusedRank<TKey>(TKey Key, double Score);

/// <summary>Reciprocal Rank Fusion of two rankings.</summary>
public static class ReciprocalRankFusion
{
    /// <summary>
    /// Default k constant from Cormack et al. 2009. Controls how much early
    /// ranks are penalised. Larger k → softer penalty for lower ranks.
    /// </summary>
    public const int DefaultK = 60;

    /// <summary>
    /// Fuses two rankings (best first) into one, ordered by RRF score descending.
    /// Only rank positions matter, never the arms' own scores. A key found in only
    /// one ranking still receives that ranking's contribution; a key repeated
    /// within a ranking counts at its first position. Ties are broken by
    /// <see cref="Comparer{T}.Default"/> on the key, so the result is deterministic.
    /// </summary>
    public static IReadOnlyList<FusedRank<TKey>> Fuse<TKey>(
        IReadOnlyList<TKey> first,
        IReadOnlyList<TKey> second,
        int k = DefaultK)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        var scores = new Dictionary<TKey, double>();

        Accumulate(first, k, scores);
        Accumulate(second, k, scores);

        return [.. scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, Comparer<TKey>.Default)
            .Select(kv => new FusedRank<TKey>(kv.Key, kv.Value))];
    }

    private static void Accumulate<TKey>(IReadOnlyList<TKey> ranking, int k, Dictionary<TKey, double> scores)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>();
        for (var i = 0; i < ranking.Count; i++)
        {
            var key = ranking[i];
            if (!seen.Add(key))
                continue;

            var contribution = 1.0 / (k + i + 1); // 1-based rank
            scores[key] = scores.GetValueOrDefault(key) + contribution;
        }
    }
}
