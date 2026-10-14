// ============================================================
// Unit Tests — ReciprocalRankFusion
// Verifies ordering correctness, tie handling, and edge cases.
// ============================================================

using FluentAssertions;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class ReciprocalRankFusionTests
{
    // ── Basic ordering ───────────────────────────────────────

    [Fact]
    public void Fuse_ItemInBothLists_ScoresHigherThanItemInOneList()
    {
        // "a" appears at rank 1 in both lists → high combined score
        // "b" appears in vector only, "c" in keyword only
        var fused = Keys(ReciprocalRankFusion.Fuse(["a", "b"], ["a", "c"]));

        fused[0].Should().Be("a", because: "'a' appears top-1 in both arms");
    }

    [Fact]
    public void Fuse_ThreeItems_CorrectDescendingOrder()
    {
        // x: rank 1 vector + rank 1 keyword   → 1/61 + 1/61 ≈ 0.0328
        // y: rank 2 vector only               → 1/62
        // z: rank 2 keyword only              → 1/62 (tie with y, broken by key)
        var fused = Keys(ReciprocalRankFusion.Fuse(["x", "y"], ["x", "z"]));

        fused[0].Should().Be("x");
        fused.Should().ContainInConsecutiveOrder("y", "z");
    }

    [Fact]
    public void Fuse_KeywordOnlyItem_IsIncluded()
    {
        // The keyword arm adds recall: a hit the vector arm never saw still ranks.
        var fused = Keys(ReciprocalRankFusion.Fuse(["v1", "v2"], ["k1"]));

        fused.Should().BeEquivalentTo(["v1", "v2", "k1"]);
    }

    [Fact]
    public void Fuse_EmptyVectorArm_ReturnsKeywordOrder()
    {
        Keys(ReciprocalRankFusion.Fuse([], ["alpha", "beta"])).Should().Equal("alpha", "beta");
    }

    [Fact]
    public void Fuse_EmptyKeywordArm_ReturnsVectorOrder()
    {
        Keys(ReciprocalRankFusion.Fuse(["alpha", "beta"], [])).Should().Equal("alpha", "beta");
    }

    [Fact]
    public void Fuse_BothEmpty_ReturnsEmpty()
    {
        ReciprocalRankFusion.Fuse<string>([], []).Should().BeEmpty();
    }

    // ── Tie handling ─────────────────────────────────────────

    [Fact]
    public void Fuse_TieItems_BrokenByKey()
    {
        // equal RRF scores → tie-break by key → "aa" first
        Keys(ReciprocalRankFusion.Fuse(["bb"], ["aa"])).Should().Equal("aa", "bb");
    }

    [Fact]
    public void Fuse_GuidKeys_AreDeterministic()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");

        Keys(ReciprocalRankFusion.Fuse([b], [a])).Should().Equal(a, b);
    }

    // ── Formula correctness ──────────────────────────────────

    [Fact]
    public void Fuse_SingleItemInBothArms_ScoreEqualsDoubleContribution()
    {
        // rank 1 with k = 60 contributes 1/61 per list → 2/61
        var results = ReciprocalRankFusion.Fuse(["x"], ["x"], k: 60);

        results.Should().ContainSingle();
        results[0].Score.Should().BeApproximately(2.0 / 61.0, 1e-10);
    }

    [Fact]
    public void Fuse_DifferentRanks_HigherRankGetsHigherScore()
    {
        var results = ReciprocalRankFusion.Fuse(["top", "mid", "bottom"], []);

        results.First(r => r.Key == "top").Score.Should().BeGreaterThan(results.First(r => r.Key == "bottom").Score);
    }

    [Fact]
    public void Fuse_KeyRepeatedWithinARanking_CountsOnce()
    {
        var results = ReciprocalRankFusion.Fuse(["x", "x"], []);

        results.Should().ContainSingle().Which.Score.Should().BeApproximately(1.0 / 61.0, 1e-10);
    }

    // ── Guards ───────────────────────────────────────────────

    [Fact]
    public void Fuse_NullFirstArg_Throws()
    {
        var act = () => ReciprocalRankFusion.Fuse(null!, Array.Empty<string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Fuse_NullSecondArg_Throws()
    {
        var act = () => ReciprocalRankFusion.Fuse(Array.Empty<string>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static List<TKey> Keys<TKey>(IReadOnlyList<FusedRank<TKey>> fused) => [.. fused.Select(f => f.Key)];
}
