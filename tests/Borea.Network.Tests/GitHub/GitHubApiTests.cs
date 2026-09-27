using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Network.GitHub;

namespace Borea.Network.Tests;

public sealed class GitHubApiTests
{
    private const string Api = "https://api.github.com";
    private const string Upstream = Api + "/repos/KSAModding/content-index";
    private const string Token = "ghu_secret";

    private readonly InstantTimeProvider _time = new();
    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public GitHubApiTests()
    {
        On("POST", "https://github.com/login/device/code", () => Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        On("POST", "https://github.com/login/oauth/access_token", () => Json($$"""{"access_token":"{{Token}}","token_type":"bearer"}"""));
        On("GET", Api + "/user", () => Json("""{"login":"octocat","id":1}"""));
    }

    [Fact]
    public async Task SendAsync_RedirectInsideTheApi_FollowsItWithTheToken()
    {
        On("GET", Api + "/repos/octocat/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json("""{"full_name":"octocat/NewName"}"""));
        var (api, _) = await SignedInAsync();

        var reply = await api.SendAsync(HttpMethod.Get, Api + "/repos/octocat/OldName", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(["GET " + Api + "/repos/octocat/OldName", "GET " + Api + "/repositories/42"], _sent.Select(sent => sent.Line));
        Assert.All(_sent, sent => Assert.Equal("Bearer " + Token, sent.Authorization));
    }

    [Theory]
    [InlineData("https://example.com/repos/octocat/MyMod")]
    [InlineData("http://api.github.com/repos/octocat/MyMod")]
    [InlineData("https://api.github.com.example.com/repos/octocat/MyMod")]
    [InlineData("https://github.com/octocat/MyMod")]
    public async Task SendAsync_RedirectOutsideTheApi_IsRefused(string location)
    {
        On("GET", Api + "/repos/octocat/MyMod", () => Redirect(location));
        var (api, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.SendAsync(HttpMethod.Get, Api + "/repos/octocat/MyMod", null, CancellationToken.None, anonymous: true));

        Assert.Equal(GitHubApiFailure.UnexpectedResponse, failure.Failure);
        Assert.Equal(["GET " + Api + "/repos/octocat/MyMod"], _sent.Select(sent => sent.Line));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "30", null, 30)]
    [InlineData(HttpStatusCode.TooManyRequests, "120", null, 120)]
    [InlineData(HttpStatusCode.Forbidden, null, "1790000000", null)]
    [InlineData(HttpStatusCode.TooManyRequests, null, null, 60)]
    public async Task SendAsync_RateLimit_GivesTheRetryTime(HttpStatusCode status, string? retryAfter, string? reset, int? seconds)
    {
        On("GET", Upstream, () =>
        {
            var response = Json("""{"message":"API rate limit exceeded."}""", status);
            if (retryAfter is not null)
                response.Headers.Add("Retry-After", retryAfter);
            if (reset is not null)
            {
                response.Headers.Add("x-ratelimit-remaining", "0");
                response.Headers.Add("x-ratelimit-reset", reset);
            }

            return response;
        });
        var (api, _) = await SignedInAsync();
        var expected = seconds is { } wait
            ? _time.GetUtcNow() + TimeSpan.FromSeconds(wait)
            : DateTimeOffset.FromUnixTimeSeconds(long.Parse(reset!, CultureInfo.InvariantCulture));

        var reply = await api.SendAsync(HttpMethod.Get, Upstream, null, CancellationToken.None);
        var failure = Assert.Throws<GitHubApiException>(() => GitHubApi.Ensure(reply));

        Assert.Equal(expected, reply.RetryAt);
        Assert.Equal(GitHubApiFailure.RateLimited, failure.Failure);
        Assert.Equal(expected, failure.RetryAt);
        Assert.Equal("API rate limit exceeded.", failure.Detail);
    }

    [Fact]
    public async Task SendAsync_ForbiddenWithoutARateLimit_HasNoRetryTimeAndKeepsGitHubsMessage()
    {
        On("PUT", Api + "/repos/octocat/content-index/contents/listings/MyMod.toml", () => Json("""{"message":"Validation Failed","errors":[{"message":"sha wasn't supplied"},"branch is protected"]}""", HttpStatusCode.Forbidden));
        var (api, _) = await SignedInAsync();

        var reply = await api.SendAsync(HttpMethod.Put, Api + "/repos/octocat/content-index/contents/listings/MyMod.toml", new { branch = "patch-2" }, CancellationToken.None);
        var failure = Assert.Throws<GitHubApiException>(() => GitHubApi.Ensure(reply));

        Assert.Null(reply.RetryAt);
        Assert.Equal(GitHubApiFailure.Forbidden, failure.Failure);
        Assert.Equal("Validation Failed. sha wasn't supplied. branch is protected", failure.Detail);
        Assert.Equal(HttpStatusCode.Forbidden, failure.Status);
    }

    [Fact]
    public async Task GetPublicAsync_TokenRefusedWithoutARateLimit_ReadsAgainWithoutTheToken()
    {
        var (api, session) = await SignedInAsync();
        _routes["GET " + Upstream + "/commits/abc/status"] = () => _sent[^1].Authorization is null
            ? Json("""{"state":"success"}""")
            : Json("""{"message":"Resource not accessible by integration"}""", HttpStatusCode.Forbidden);

        var status = await api.GetPublicAsync<StatusDto>(Upstream + "/commits/abc/status", CancellationToken.None);

        Assert.Equal("success", status.State);
        Assert.Equal(["Bearer " + Token, null], _sent.Select(sent => sent.Authorization));
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task GetPublicAsync_RateLimited_DoesNotReadAgain()
    {
        On("GET", Upstream + "/commits/abc/status", () =>
        {
            var response = Json("""{"message":"You have exceeded a secondary rate limit."}""", HttpStatusCode.Forbidden);
            response.Headers.Add("Retry-After", "30");
            return response;
        });
        var (api, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.GetPublicAsync<StatusDto>(Upstream + "/commits/abc/status", CancellationToken.None));

        Assert.Equal(GitHubApiFailure.RateLimited, failure.Failure);
        Assert.Single(_sent);
    }

    [Fact]
    public async Task SendAsync_UnauthorizedToTheToken_IsASignedOutFailure()
    {
        On("GET", Upstream, () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));
        var (api, session) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.SendAsync(HttpMethod.Get, Upstream, null, CancellationToken.None));

        Assert.Equal(GitHubApiFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
        Assert.DoesNotContain(Token, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_UnauthorizedWithoutTheToken_IsAnAnswerAndKeepsTheSession()
    {
        On("GET", Upstream, () => Json("""{"message":"Requires authentication"}""", HttpStatusCode.Unauthorized));
        var (api, session) = await SignedInAsync();

        var reply = await api.SendAsync(HttpMethod.Get, Upstream, null, CancellationToken.None, anonymous: true);

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task SendAsync_SignedOut_IsASignedOutFailureWithNothingSent()
    {
        var (api, session) = await SignedInAsync();
        session.SignOut();

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.SendAsync(HttpMethod.Get, Upstream, null, CancellationToken.None));

        Assert.Equal(GitHubApiFailure.SignedOut, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task GetPagesAsync_FullPage_ReadsTheNextPageAndStopsBelowAHundredItems()
    {
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=1", () => Json(Numbers(1, 100)));
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=2", () => Json(Numbers(101, 99)));
        On("GET", Upstream + "/pulls?state=open&per_page=100&page=3", () => Json(Numbers(200, 1)));
        var (api, _) = await SignedInAsync();

        var pulls = new List<PullDto>();
        await foreach (var pull in api.GetPagesAsync<PullDto>(Upstream + "/pulls?state=open", CancellationToken.None))
            pulls.Add(pull);

        Assert.Equal(Enumerable.Range(1, 199), pulls.Select(pull => pull.Number));
        Assert.Equal(["GET " + Upstream + "/pulls?state=open&per_page=100&page=1", "GET " + Upstream + "/pulls?state=open&per_page=100&page=2"], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task GetPagesAsync_WrappedPage_OpensTheArrayAndStartsTheQuery()
    {
        On("GET", Api + "/user/installations?per_page=100&page=1", () => Json("""{"total_count":1,"installations":[{"id":7}]}"""));
        var (api, _) = await SignedInAsync();

        var ids = new List<long>();
        await foreach (var installation in api.GetPagesAsync<InstallationsDto, InstallationDto>(Api + "/user/installations", page => page.Installations, CancellationToken.None))
            ids.Add(installation.Id);

        Assert.Equal([7L], ids);
        Assert.Single(_sent);
    }

    [Theory]
    [InlineData("PUT", Upstream + "/contents/listings/MyMod.toml", """{"message":"m","content":"eA==","branch":"main"}""")]
    [InlineData("PUT", Upstream + "/contents/listings/MyMod.toml", """{"message":"m","content":"eA=="}""")]
    [InlineData("PUT", Upstream + "/contents/listings/MyMod.toml", """{"message":"m","content":"eA==","branch":"feature/x"}""")]
    [InlineData("PUT", Upstream + "/contents/index-status.toml", """{"message":"m","content":"eA==","branch":"Steward/x"}""")]
    [InlineData("DELETE", Upstream + "/contents/listings/MyMod.toml", """{"message":"m","sha":"s","branch":"main"}""")]
    [InlineData("PUT", Api + "/repos/ksamodding/Content-Index-Releases/contents/releases/MyMod/1.0.0.json", """{"message":"m","content":"eA==","branch":"Main"}""")]
    [InlineData("PUT", Api + "/repositories/42/contents/listings/MyMod.toml", """{"message":"m","content":"eA==","branch":"main"}""")]
    [InlineData("PATCH", Upstream + "/git/refs/heads/main", """{"sha":"abc","force":true}""")]
    [InlineData("DELETE", Upstream + "/git/refs/heads/main", null)]
    [InlineData("POST", Upstream + "/git/refs", """{"ref":"refs/heads/main","sha":"abc"}""")]
    [InlineData("PATCH", Upstream + "/git/refs/heads/hand-test/base", """{"sha":"abc"}""")]
    [InlineData("POST", Upstream + "/merges", """{"base":"main","head":"steward/x"}""")]
    [InlineData("POST", Upstream + "/merge-upstream", """{"branch":"main"}""")]
    [InlineData("POST", Upstream + "/branches/main/rename", """{"new_name":"old"}""")]
    [InlineData("DELETE", Upstream + "/branches/hand-test/base/protection", null)]
    [InlineData("DELETE", Upstream + "/branches/hand-test%2Fbase/protection", null)]
    [InlineData("POST", Upstream + "/branches/Hand-Test%2Fbase/rename", """{"new_name":"old"}""")]
    public async Task SendAsync_WriteToAProtectedBranch_IsRefusedWithNothingSent(string method, string url, string? body)
    {
        var (api, _) = await SignedInAsync(baseBranch: "hand-test/base");

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.SendAsync(new HttpMethod(method), url, Body(body), CancellationToken.None));

        Assert.Equal(GitHubApiFailure.ProtectedBranch, failure.Failure);
        Assert.NotNull(failure.Detail);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task SendAsync_CommitToABaseBranchWithACommitPrefix_IsRefusedWithNothingSent()
    {
        var (api, _) = await SignedInAsync(baseBranch: "steward/hand-test");
        var body = Body("""{"message":"m","content":"eA==","branch":"steward/hand-test"}""");

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.SendAsync(HttpMethod.Put, Upstream + "/contents/listings/MyMod.toml", body, CancellationToken.None));

        Assert.Equal(GitHubApiFailure.ProtectedBranch, failure.Failure);
        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData("PUT", Upstream + "/contents/index-status.toml", """{"message":"m","content":"eA==","branch":"steward/delist-mymod"}""")]
    [InlineData("PUT", Upstream + "/contents/listings/MyMod.toml", """{"message":"m","content":"eA==","branch":"listing-mymod"}""")]
    [InlineData("POST", Upstream + "/git/refs", """{"ref":"refs/heads/steward/delist-mymod","sha":"abc"}""")]
    [InlineData("PATCH", Upstream + "/git/refs/heads/steward/delist-mymod", """{"sha":"abc"}""")]
    [InlineData("POST", Upstream + "/branches/mainline/rename", """{"new_name":"old"}""")]
    [InlineData("PUT", Upstream + "/pulls/90/merge", """{"sha":"abc","merge_method":"squash"}""")]
    [InlineData("POST", Upstream + "/pulls", """{"head":"octocat:listing-mymod","base":"main"}""")]
    [InlineData("PUT", Api + "/repos/octocat/content-index/contents/listings/MyMod.toml", """{"message":"m","content":"eA==","branch":"patch-2"}""")]
    [InlineData("PUT", Api + "/repos/octocat/content-index/contents/listings/MyMod.toml", """{"message":"m","content":"eA==","branch":"main"}""")]
    [InlineData("GET", Upstream + "/contents/listings/MyMod.toml?ref=main", null)]
    public async Task SendAsync_OtherRequest_IsSent(string method, string url, string? body)
    {
        var (api, _) = await SignedInAsync(baseBranch: "hand-test/base");

        await api.SendAsync(new HttpMethod(method), url, Body(body), CancellationToken.None);

        Assert.Equal([method + " " + url], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task SendAsync_WriteRedirectedToARepositoryById_IsCheckedAgainBeforeTheRedirect()
    {
        On("PUT", Api + "/repos/octocat/content-index/contents/a.txt", () => Redirect(Api + "/repositories/42/contents/a.txt", HttpStatusCode.TemporaryRedirect));
        var (api, _) = await SignedInAsync();

        var failure = await Assert.ThrowsAsync<GitHubApiException>(() => api.SendAsync(HttpMethod.Put, Api + "/repos/octocat/content-index/contents/a.txt", Body("""{"branch":"main"}"""), CancellationToken.None));

        Assert.Equal(GitHubApiFailure.ProtectedBranch, failure.Failure);
        Assert.Equal(["PUT " + Api + "/repos/octocat/content-index/contents/a.txt"], _sent.Select(sent => sent.Line));
    }

    [Fact]
    public async Task QueryAsync_SendsTheQueryAndItsVariablesWithTheToken_AndGivesTheData()
    {
        On("POST", Api + "/graphql", () => Json("""{"data":{"repository":{"pullRequest":{"number":5}}}}"""));
        var (api, _) = await SignedInAsync();

        var data = await api.QueryAsync<JsonElement>("query($number: Int!) { repository { pullRequest(number: $number) { number } } }", new Dictionary<string, object> { ["number"] = 5 }, CancellationToken.None);

        Assert.Equal(5, data.GetProperty("repository").GetProperty("pullRequest").GetProperty("number").GetInt32());
        var sent = Assert.Single(_sent);
        Assert.Equal(("POST " + Api + "/graphql", "Bearer " + Token), (sent.Line, sent.Authorization));
        Assert.Equal("""{"query":"query($number: Int!) { repository { pullRequest(number: $number) { number } } }","variables":{"number":5}}""", sent.Body);
    }

    [Theory]
    [InlineData("""{"data":null,"errors":[{"type":"NOT_FOUND","message":"Could not resolve to a Repository."}]}""", "NotFound", "Could not resolve to a Repository.")]
    [InlineData("""{"data":{"repository":null},"errors":[{"type":"FORBIDDEN","message":"Resource not accessible by integration"}]}""", "Forbidden", "Resource not accessible by integration")]
    [InlineData("""{"errors":[{"type":"RATE_LIMITED","message":"API rate limit exceeded"}]}""", "RateLimited", "API rate limit exceeded")]
    [InlineData("""{"errors":[{"message":"Parse error on \"}\""}]}""", "UnexpectedResponse", "Parse error on \"}\"")]
    [InlineData("""{"data":null}""", "UnexpectedResponse", null)]
    public async Task QueryAsync_ErrorInASuccess_Throws(string answer, string failure, string? detail)
    {
        On("POST", Api + "/graphql", () => Json(answer));
        var (api, _) = await SignedInAsync();

        var exception = await Assert.ThrowsAsync<GitHubApiException>(() => api.QueryAsync<Dictionary<string, JsonElement>>("query { viewer { login } }", new Dictionary<string, object>(), CancellationToken.None));

        Assert.Equal((failure, detail), (exception.Failure.ToString(), exception.Detail));
        Assert.Equal(exception.Failure == GitHubApiFailure.RateLimited, exception.RetryAt is not null);
    }

    [Theory]
    [InlineData("mutation { mergePullRequest(input: { pullRequestId: \"x\" }) { clientMutationId } }")]
    [InlineData("{ viewer { login } }")]
    public async Task QueryAsync_NoQuery_SendsNothing(string document)
    {
        var (api, _) = await SignedInAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => api.QueryAsync<JsonElement>(document, new Dictionary<string, object>(), CancellationToken.None));

        Assert.Empty(_sent);
    }

    [Fact]
    public void Parse_NotTheExpectedJson_IsAnUnexpectedResponse()
    {
        var failure = Assert.Throws<GitHubApiException>(() => GitHubApi.Parse<StatusDto>("<html>"));

        Assert.Equal(GitHubApiFailure.UnexpectedResponse, failure.Failure);
        Assert.IsType<JsonException>(failure.InnerException, exactMatch: false);
    }

    private async Task<(GitHubApi Api, GitHubSession Session)> SignedInAsync(string? baseBranch = null)
    {
        var http = new HttpClient(new FakeHttpMessageHandler(RespondAsync));
        var session = new GitHubSession(http, "Iv1.testclient", "borea-test", _time);
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();
        return (new GitHubApi(session, http, _time, baseBranch), session);
    }

    private void On(string method, string url, Func<HttpResponseMessage> answer) => _routes[method + " " + url] = answer;

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        _sent.Add(new Sent(request.Method.Method, url, request.Content is null ? null : await request.Content.ReadAsStringAsync(), request.Headers.Authorization?.ToString()));
        return _routes.TryGetValue(request.Method.Method + " " + url, out var answer) ? answer() : Json("{}");
    }

    private static JsonElement? Body(string? json) => json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);

    private static string Numbers(int first, int count) =>
        JsonSerializer.Serialize(Enumerable.Range(first, count).Select(number => new { number }));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.MovedPermanently)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("""{"message":"Moved Permanently"}""") };
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed record Sent(string Method, string Url, string? Body, string? Authorization)
    {
        public string Line => Method + " " + Url;
    }

    private sealed class StatusDto
    {
        public string State { get; set; } = string.Empty;
    }

    private sealed class PullDto
    {
        public int Number { get; set; }
    }

    private sealed class InstallationsDto
    {
        public List<InstallationDto> Installations { get; set; } = [];
    }

    private sealed class InstallationDto
    {
        public long Id { get; set; }
    }

    /// <summary>A fixed clock that fires every delay at once, so the sign-in does not wait for its poll interval.</summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return TimeProvider.System.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
