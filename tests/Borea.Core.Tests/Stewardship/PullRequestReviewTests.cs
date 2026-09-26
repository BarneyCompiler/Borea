using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class PullRequestReviewTests
{
    private const string ContentIndex = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";

    [Fact]
    public void DiffUrlOf_AnchorsTheFileByTheSha256OfItsPath()
    {
        var url = PullRequestFile.DiffUrlOf(new Uri($"https://github.com/{ContentIndex}/pull/5"), "README.md");

        Assert.Equal($"https://github.com/{ContentIndex}/pull/5/files#diff-b335630551682c19a781afebcf4d07bf978fb1f8ac04c6bf87428ed5106870f5", url.AbsoluteUri);
    }

    [Theory]
    [InlineData("success", ValidateState.Success)]
    [InlineData("failure", ValidateState.Failure)]
    [InlineData("error", ValidateState.Error)]
    [InlineData("pending", ValidateState.Pending)]
    [InlineData("SUCCESS", ValidateState.Unknown)]
    [InlineData("queued", ValidateState.Unknown)]
    [InlineData(null, ValidateState.Unknown)]
    public void StateOf_KnowsTheStatesOfGitHub(string? state, ValidateState expected)
    {
        Assert.Equal(expected, ValidateStatus.StateOf(state));
    }

    [Theory]
    [InlineData(ContentIndex, "KSAModding/content-index", false, true)]
    [InlineData(ContentIndex, "alice/content-index", true, true)]
    [InlineData(ContentIndex, null, true, true)]
    [InlineData(Releases, "ksamodding/content-index-releases", false, false)]
    public void FromForkAndRunChecks_FollowTheRepositories(string repository, string? head, bool fromFork, bool canRunChecks)
    {
        var review = new PullRequestReview(repository, 5, new Uri($"https://github.com/{repository}/pull/5"), "Pull 5", "alice", PullRequestState.Open, IsDraft: false, [], "main", "c0ffee", head, null, null, null, [], []);

        Assert.Equal((fromFork, canRunChecks), (review.IsFromFork, review.CanRunChecks));
    }

    public static TheoryData<PullRequestReview, PullRequestRefusal[]> Merges => new()
    {
        { Review(), [] },
        { Review() with { Validate = new ValidateStatus(ValidateState.Failure) }, [PullRequestRefusal.Validate] },
        { Review() with { Validate = new ValidateStatus(ValidateState.Pending) }, [PullRequestRefusal.Validate] },
        { Review() with { Validate = new ValidateStatus(ValidateState.Missing) }, [PullRequestRefusal.Validate] },
        { Review() with { Validate = null, ValidateFailure = new StewardException(StewardFailure.RateLimited) }, [PullRequestRefusal.Validate] },
        { Review() with { Verdict = null }, [PullRequestRefusal.Verdict] },
        { Review() with { State = PullRequestState.Merged, IsDraft = true, Validate = new ValidateStatus(ValidateState.Error), Verdict = null }, [PullRequestRefusal.NotOpen, PullRequestRefusal.Draft, PullRequestRefusal.Validate, PullRequestRefusal.Verdict] },
    };

    [Theory]
    [MemberData(nameof(Merges))]
    public void BlockersOf_MergeNeedsAnOpenPullRequestWithGreenValidateAndTheVerdict(PullRequestReview review, PullRequestRefusal[] blockers)
    {
        Assert.Equal(blockers, PullRequestMerge.BlockersOf(review));
    }

    [Theory]
    [InlineData(PullRequestReviewKind.Approve, false)]
    [InlineData(PullRequestReviewKind.RequestChanges, true)]
    [InlineData(PullRequestReviewKind.Comment, true)]
    public void NeedsBody_OnlyAnApprovalGoesWithoutText(PullRequestReviewKind kind, bool needsBody)
    {
        Assert.Equal(needsBody, PullRequestMerge.NeedsBody(kind));
    }

    private static PullRequestReview Review() =>
        new(ContentIndex, 5, new Uri($"https://github.com/{ContentIndex}/pull/5"), "Pull 5", "alice", PullRequestState.Open, IsDraft: false, [], "main", "c0ffee", "alice/content-index", new ValidateStatus(ValidateState.Success), null, "Validated.", [], []);
}
