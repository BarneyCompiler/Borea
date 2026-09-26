using System.Net;
using System.Text.Json;
using Borea.Core.Stewardship;

namespace Borea.Network.Tests.GitHub;

/// <summary>The steward octocat acts on the pull requests of <see cref="GitHubPullRequestReviewsTests"/>, as the review showed them.</summary>
public sealed partial class GitHubPullRequestReviewsTests
{
    private const string Older = "fedcba9876543210fedcba9876543210fedcba98";
    private const string Verdict = IndexMarker + "\nValidated, and ownership is not verified, so a steward decides.";

    [Theory]
    [InlineData(PullRequestReviewKind.Approve, null, """{"commit_id":"0123456789abcdef0123456789abcdef01234567","event":"APPROVE"}""")]
    [InlineData(PullRequestReviewKind.Approve, " Looks right. ", """{"commit_id":"0123456789abcdef0123456789abcdef01234567","event":"APPROVE","body":"Looks right."}""")]
    [InlineData(PullRequestReviewKind.RequestChanges, "Fix the abstract.", """{"commit_id":"0123456789abcdef0123456789abcdef01234567","event":"REQUEST_CHANGES","body":"Fix the abstract."}""")]
    [InlineData(PullRequestReviewKind.Comment, "Is this the right id?", """{"commit_id":"0123456789abcdef0123456789abcdef01234567","event":"COMMENT","body":"Is this the right id?"}""")]
    public async Task ReviewAsync_SendsTheReviewOfTheHeadCommitThatTheReviewShowed(PullRequestReviewKind kind, string? body, string expected)
    {
        Pull(Index, 5, First, "alice/content-index");
        _routes[$"POST {Api}/repos/{Index}/pulls/5/reviews"] = () => Json("""{"id":80,"state":"APPROVED"}""");
        var reviews = await SignedInAsync();

        await reviews.ReviewAsync(Shown(Index, 5), kind, body);

        Assert.Equal([$"GET {Api}/repos/{Index}/pulls/5", $"POST {Api}/repos/{Index}/pulls/5/reviews"], _sent.Select(sent => $"{sent.Method} {sent.Url}"));
        AssertJson(expected, _sent[^1].Body);
        Assert.All(_sent, sent => Assert.Equal("Bearer " + Token, sent.Authorization));
    }

    [Theory]
    [InlineData(PullRequestReviewKind.RequestChanges, null)]
    [InlineData(PullRequestReviewKind.Comment, "  ")]
    public async Task ReviewAsync_WithoutTheTextItNeeds_SendsNothing(PullRequestReviewKind kind, string? body)
    {
        var reviews = await SignedInAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => reviews.ReviewAsync(Shown(Index, 5), kind, body));
        await Assert.ThrowsAsync<ArgumentException>(() => reviews.CloseAsync(Shown(Index, 5), " "));
        await Assert.ThrowsAsync<ArgumentException>(() => reviews.MergeAsync(Shown("KSAModding/Borea", 5), skipRequiredReview: false));

        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData(Second, "main", "open", false, PullRequestRefusal.Changed)]
    [InlineData(First, "automation-test", "open", false, PullRequestRefusal.Changed)]
    [InlineData(First, "Main", "open", false, PullRequestRefusal.Changed)]
    [InlineData(First, "main", "closed", false, PullRequestRefusal.NotOpen)]
    [InlineData(First, "main", "closed", true, PullRequestRefusal.NotOpen)]
    public async Task EveryAction_PullRequestChangedSinceTheReview_RefusesAndWritesNothing(string head, string baseBranch, string state, bool merged, PullRequestRefusal refusal)
    {
        Pull(Index, 5, head, Index, state: state, merged: merged, baseBranch: baseBranch);
        Comments(Index, 5, (IndexVerdict.BotLogin, Verdict));
        Status(Index, head, ("validate", "success", "validated"));
        var reviews = await SignedInAsync();

        var review = await Assert.ThrowsAsync<PullRequestRefusedException>(() => reviews.ReviewAsync(Shown(Index, 5), PullRequestReviewKind.Approve, null));
        var merge = await Assert.ThrowsAsync<PullRequestRefusedException>(() => reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: true));
        var close = await Assert.ThrowsAsync<PullRequestRefusedException>(() => reviews.CloseAsync(Shown(Index, 5), "Not a listing."));

        Assert.Equal([refusal, refusal, refusal], [review.Refusal, merge.Refusal, close.Refusal]);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task MergeAsync_GreenValidateAndTheVerdictOfTheBot_SquashMergesTheHeadCommit()
    {
        Mergeable(Index, 5);
        var reviews = await SignedInAsync();

        await reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: false);

        var merge = Assert.Single(_sent, sent => sent.Method == "PUT");
        Assert.Equal($"{Api}/repos/{Index}/pulls/5/merge", merge.Url);
        AssertJson($$"""{"sha":"{{First}}","merge_method":"squash"}""", merge.Body);
        Assert.Equal("PUT", _sent[^1].Method);

        // validate is read again at the head commit, without the token, and the review decision through GraphQL
        Assert.Contains(_sent, sent => sent.Url == $"{Api}/repos/{Index}/commits/{First}/status?per_page=100" && sent.Authorization is null);
        var query = Assert.Single(_sent, sent => sent.Url == Api + "/graphql");
        using var sentQuery = JsonDocument.Parse(query.Body!);
        Assert.Contains("reviewDecision", sentQuery.RootElement.GetProperty("query").GetString(), StringComparison.Ordinal);
        AssertJson("""{"owner":"KSAModding","name":"content-index","number":5}""", sentQuery.RootElement.GetProperty("variables").GetRawText());
        Assert.Equal(("POST", "Bearer " + Token), (query.Method, query.Authorization));
    }

    public static TheoryData<string, PullRequestRefusal> NotMergeable => new()
    {
        { "validate failure", PullRequestRefusal.Validate },
        { "validate pending", PullRequestRefusal.Validate },
        { "validate missing", PullRequestRefusal.Validate },
        { "validate on an older commit", PullRequestRefusal.Validate },
        { "verdict missing", PullRequestRefusal.Verdict },
        { "verdict of another account", PullRequestRefusal.Verdict },
        { "verdict of the other repository", PullRequestRefusal.Verdict },
        { "draft", PullRequestRefusal.Draft },
    };

    [Theory]
    [MemberData(nameof(NotMergeable))]
    public async Task MergeAsync_WithoutGreenValidateOnTheHeadCommitOrTheVerdict_RefusesAndDoesNotMerge(string state, PullRequestRefusal refusal)
    {
        Mergeable(Index, 5);
        switch (state)
        {
            case "validate failure":
                Status(Index, First, ("validate", "failure", "does not parse"));
                break;
            case "validate pending":
                Status(Index, First, ("validate", "pending", "the checks run"));
                break;
            case "validate missing":
                Status(Index, First, ("other", "success", "another check"));
                break;
            case "validate on an older commit":
                Status(Index, First);
                Status(Index, Older, ("validate", "success", "validated"));
                break;
            case "verdict missing":
                Comments(Index, 5);
                break;
            case "verdict of another account":
                Comments(Index, 5, ("mallory", Verdict));
                break;
            case "verdict of the other repository":
                Comments(Index, 5, (IndexVerdict.BotLogin, ReleasesMarker + "\nValidated."));
                break;
            default:
                Pull(Index, 5, First, Index, draft: true);
                Comments(Index, 5, (IndexVerdict.BotLogin, Verdict));
                break;
        }

        var reviews = await SignedInAsync();

        var refused = await Assert.ThrowsAsync<PullRequestRefusedException>(() => reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: true));

        Assert.Equal(refusal, refused.Refusal);
        Assert.DoesNotContain(_sent, sent => sent.Method == "PUT");
    }

    [Fact]
    public async Task MergeAsync_GitHubStillRequiresAReview_RefusesUntilTheStewardSkipsIt()
    {
        Mergeable(Releases, 71);
        Comments(Releases, 71, (IndexVerdict.BotLogin, ReleasesMarker + "\nAn amendment by a non-owner, so a steward decides."));
        Decision("REVIEW_REQUIRED");
        var reviews = await SignedInAsync();

        var refused = await Assert.ThrowsAsync<PullRequestRefusedException>(() => reviews.MergeAsync(Shown(Releases, 71), skipRequiredReview: false));
        var mergedBefore = _sent.Any(sent => sent.Method == "PUT");
        await reviews.MergeAsync(Shown(Releases, 71), skipRequiredReview: true);

        Assert.Equal((PullRequestRefusal.ReviewRequired, false), (refused.Refusal, mergedBefore));
        Assert.Single(_sent, sent => sent.Method == "PUT" && sent.Url == $"{Api}/repos/{Releases}/pulls/71/merge");
    }

    [Fact]
    public async Task MergeAsync_ApprovedReview_MergesWithoutASecondConfirmation()
    {
        Mergeable(Index, 5);
        Decision("APPROVED");
        var reviews = await SignedInAsync();

        await reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: false);

        Assert.Single(_sent, sent => sent.Method == "PUT");
    }

    [Fact]
    public async Task MergeAsync_409_SaysThatThePullRequestChanged()
    {
        Mergeable(Index, 5);
        _routes[$"PUT {Api}/repos/{Index}/pulls/5/merge"] = () => Json("""{"message":"Head branch was modified. Review and try the merge again."}""", HttpStatusCode.Conflict);
        var reviews = await SignedInAsync();

        var refused = await Assert.ThrowsAsync<PullRequestRefusedException>(() => reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: false));

        Assert.Equal(PullRequestRefusal.Changed, refused.Refusal);
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed, """{"message":"Pull Request is not mergeable"}""", StewardFailure.Refused, "Pull Request is not mergeable")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed","errors":["Merge method squash is not allowed"]}""", StewardFailure.Refused, "Validation Failed. Merge method squash is not allowed")]
    [InlineData(HttpStatusCode.OK, """{"merged":false,"message":"Not merged"}""", StewardFailure.Refused, "Not merged")]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}""", StewardFailure.Forbidden, "Resource not accessible by integration")]
    public async Task MergeAsync_GitHubRefusesTheMerge_KeepsItsMessage(HttpStatusCode status, string answer, StewardFailure failure, string message)
    {
        Mergeable(Releases, 71);
        Comments(Releases, 71, (IndexVerdict.BotLogin, ReleasesMarker + "\nValidated."));
        _routes[$"PUT {Api}/repos/{Releases}/pulls/71/merge"] = () => Json(answer, status);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.MergeAsync(Shown(Releases, 71), skipRequiredReview: false));

        Assert.Equal((failure, message), (exception.Failure, exception.Detail));
    }

    [Fact]
    public async Task EveryAction_AppNotInstalledOnContentIndexReleases_IsForbiddenAndWritesNothing()
    {
        _routes[$"{Api}/repos/{Releases}/pulls/71"] = () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);
        var reviews = await SignedInAsync();

        var review = await Assert.ThrowsAsync<StewardException>(() => reviews.ReviewAsync(Shown(Releases, 71), PullRequestReviewKind.Approve, null));
        var merge = await Assert.ThrowsAsync<StewardException>(() => reviews.MergeAsync(Shown(Releases, 71), skipRequiredReview: false));
        var close = await Assert.ThrowsAsync<StewardException>(() => reviews.CloseAsync(Shown(Releases, 71), "Duplicate."));

        Assert.Equal([StewardFailure.Forbidden, StewardFailure.Forbidden, StewardFailure.Forbidden], [review.Failure, merge.Failure, close.Failure]);
        Assert.All(_sent, sent => Assert.Equal("GET", sent.Method));
    }

    [Fact]
    public async Task MergeAsync_ValidateCannotBeRead_SaysWhyAndDoesNotMerge()
    {
        Mergeable(Index, 5);
        _routes[$"{Api}/repos/{Index}/commits/{First}/status?per_page=100"] = () =>
        {
            var response = Json("""{"message":"API rate limit exceeded for 203.0.113.9."}""", HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", "1790000000");
            return response;
        };
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: true));

        Assert.Equal(StewardFailure.RateLimited, exception.Failure);
        Assert.DoesNotContain(_sent, sent => sent.Method == "PUT");
    }

    [Theory]
    [InlineData("""{"data":{"repository":null},"errors":[{"type":"FORBIDDEN","message":"Resource not accessible by integration"}]}""", StewardFailure.Forbidden)]
    [InlineData("""{"data":{"repository":{"pullRequest":null}}}""", StewardFailure.NotFound)]
    public async Task MergeAsync_ReviewDecisionCannotBeRead_DoesNotMerge(string answer, StewardFailure failure)
    {
        Mergeable(Index, 5);
        _routes[$"POST {Api}/graphql"] = () => Json(answer);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.MergeAsync(Shown(Index, 5), skipRequiredReview: false));

        Assert.Equal(failure, exception.Failure);
        Assert.DoesNotContain(_sent, sent => sent.Method == "PUT");
    }

    [Fact]
    public async Task CloseAsync_PostsTheCommentAndThenCloses()
    {
        Pull(Index, 5, First, "alice/content-index");
        _routes[$"POST {Api}/repos/{Index}/issues/5/comments"] = () => Json("""{"id":9}""", HttpStatusCode.Created);
        _routes[$"PATCH {Api}/repos/{Index}/pulls/5"] = () => Json("""{"number":5,"state":"closed"}""");
        var reviews = await SignedInAsync();

        await reviews.CloseAsync(Shown(Index, 5), " A listing for this mod already exists. ");

        Assert.Equal(
            [$"GET {Api}/repos/{Index}/pulls/5", $"GET {Api}/repos/{Index}/issues/5/comments?per_page=100&page=1", $"POST {Api}/repos/{Index}/issues/5/comments", $"PATCH {Api}/repos/{Index}/pulls/5"],
            _sent.Select(sent => $"{sent.Method} {sent.Url}"));
        AssertJson("""{"body":"A listing for this mod already exists."}""", _sent[2].Body);
        AssertJson("""{"state":"closed"}""", _sent[3].Body);
    }

    [Theory]
    [InlineData("octocat", "Duplicate.\nSee #4.  ", null, false)]
    [InlineData("OctoCat", "Duplicate.\r\nSee #4.", null, false)]
    [InlineData("octocat", "Duplicate.", null, true)]
    [InlineData("mallory", "Duplicate.\nSee #4.", null, true)]
    [InlineData("octocat", "Duplicate.\nSee #4.", "alice", true)]
    public async Task CloseAsync_AfterAFailedClose_PostsItsCommentOnlyWhenItIsNotTheLastComment(string login, string earlier, string? replyBy, bool posts)
    {
        Pull(Index, 5, First, "alice/content-index");
        if (replyBy is null)
            Comments(Index, 5, ("alice", "Please merge."), (login, earlier));
        else
            Comments(Index, 5, (login, earlier), (replyBy, "Why?"));

        _routes[$"POST {Api}/repos/{Index}/issues/5/comments"] = () => Json("""{"id":9}""", HttpStatusCode.Created);
        _routes[$"PATCH {Api}/repos/{Index}/pulls/5"] = () => Json("""{"number":5,"state":"closed"}""");
        var reviews = await SignedInAsync();

        await reviews.CloseAsync(Shown(Index, 5), " Duplicate.\nSee #4. ");

        Assert.Equal(posts, _sent.Any(sent => sent.Method == "POST"));
        Assert.Equal("PATCH", _sent[^1].Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Unprocessable Entity","errors":["Can not approve your own pull request"]}""", StewardFailure.Refused, "Unprocessable Entity. Can not approve your own pull request")]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}""", StewardFailure.Forbidden, "Resource not accessible by integration")]
    public async Task ReviewAsync_GitHubRefusesTheReview_KeepsItsMessage(HttpStatusCode status, string answer, StewardFailure failure, string message)
    {
        Pull(Index, 5, First, "alice/content-index");
        _routes[$"POST {Api}/repos/{Index}/pulls/5/reviews"] = () => Json(answer, status);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.ReviewAsync(Shown(Index, 5), PullRequestReviewKind.Approve, null));

        Assert.Equal((failure, message), (exception.Failure, exception.Detail));
    }

    [Fact]
    public async Task CloseAsync_CloseRefusedAfterTheComment_SaysWhy()
    {
        Pull(Index, 5, First, "alice/content-index");
        _routes[$"POST {Api}/repos/{Index}/issues/5/comments"] = () => Json("""{"id":9}""", HttpStatusCode.Created);
        _routes[$"PATCH {Api}/repos/{Index}/pulls/5"] = () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.CloseAsync(Shown(Index, 5), "Duplicate."));

        Assert.Equal((StewardFailure.Forbidden, "Resource not accessible by integration"), (exception.Failure, exception.Detail));
        Assert.Equal(["POST", "PATCH"], _sent.Where(sent => sent.Method != "GET").Select(sent => sent.Method));
    }

    [Fact]
    public async Task CloseAsync_CommentRefused_DoesNotClose()
    {
        Pull(Index, 5, First, "alice/content-index");
        _routes[$"POST {Api}/repos/{Index}/issues/5/comments"] = () => Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);
        var reviews = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<StewardException>(() => reviews.CloseAsync(Shown(Index, 5), "Duplicate."));

        Assert.Equal(StewardFailure.Forbidden, exception.Failure);
        Assert.DoesNotContain(_sent, sent => sent.Method == "PATCH");
    }

    /// <summary>The pull request as the review showed it, at the head commit <see cref="First"/>.</summary>
    private static PullRequestReview Shown(string repository, int number) =>
        new(repository, number, new Uri($"https://github.com/{repository}/pull/{number}"), $"Pull {number}", "alice", PullRequestState.Open, IsDraft: false, [], "main", First, "alice/content-index", new ValidateStatus(ValidateState.Success), null, "Validated.", [], []);

    /// <summary>An open pull request at <see cref="First"/> with a green validate, the verdict of the bot, no required review, and a merge that GitHub accepts.</summary>
    private void Mergeable(string repository, int number)
    {
        Pull(repository, number, First, "alice/content-index");
        Comments(repository, number, ("mallory", "Looks fine to me."), (IndexVerdict.BotLogin, Verdict));
        Status(repository, First, ("validate", "success", "validated, a steward decides"));
        Decision(null);
        _routes[$"PUT {Api}/repos/{repository}/pulls/{number}/merge"] = () => Json($$"""{"sha":"{{Second}}","merged":true,"message":"Pull Request successfully merged"}""");
    }

    private void Decision(string? decision) =>
        _routes[$"POST {Api}/graphql"] = () => Json(JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { reviewDecision = decision } } } }));

    private static void AssertJson(string expected, string? actual)
    {
        Assert.NotNull(actual);
        using var want = JsonDocument.Parse(expected);
        using var got = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(want.RootElement, got.RootElement), actual);
    }
}
