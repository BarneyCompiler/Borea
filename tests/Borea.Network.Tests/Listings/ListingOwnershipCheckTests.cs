using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Network.GitHub;
using Borea.Network.Listings;

namespace Borea.Network.Tests.Listings;

/// <summary>The steward octocat (id 1) is signed in and checks the author alice (id 5).</summary>
public sealed class ListingOwnershipCheckTests
{
    private const string Api = "https://api.github.com";
    private const string Upstream = Api + "/repos/KSAModding/content-index";
    private const string Token = "ghu_secret";
    private const string Head = "c0ffee1";
    private const string ListingPath = "listings/MyMod.toml";
    private const string ListedForums = "https://forums.ahwoo.com/threads/my-mod.783/";
    private const string SubmittedForums = "https://forums.ahwoo.com/threads/my-new-mod.901/";

    private static readonly GitHubAccount Alice = new("alice", 5);

    private readonly List<Sent> _sent = [];
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public ListingOwnershipCheckTests()
    {
        On("POST", "https://github.com/login/device/code", () => Json("""{"device_code":"d","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}"""));
        On("POST", "https://github.com/login/oauth/access_token", () => Json($$"""{"access_token":"{{Token}}","token_type":"bearer","scope":"","expires_in":28800}"""));
        On("GET", Api + "/user", () => Json("""{"login":"octocat","id":1}"""));
    }

    [Theory]
    [InlineData(5L, "", null, ListingOwnershipProof.Owner)]
    [InlineData(1L, "", null, null)]
    [InlineData(99L, "ksa-index-alice", null, ListingOwnershipProof.Topic)]
    [InlineData(99L, "ksa-index-octocat", null, null)]
    [InlineData(99L, "", "login = \"Alice\"\nid = \"mymod\"\n", ListingOwnershipProof.MarkerFile)]
    [InlineData(99L, "", "account = \"alice\"\n", ListingOwnershipProof.MarkerFile)]
    [InlineData(99L, "", "login = \"alice\"\nlisting = \"Other\"\n", null)]
    [InlineData(99L, "", "login = \"octocat\"\n", null)]
    public async Task CheckAsync_AnotherLogin_ProvesItByItsOwnIdTopicAndMarker(long ownerId, string topic, string? marker, ListingOwnershipProof? proof)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId)));
        On("GET", Api + "/repos/Studio/MyMod/topics", () => Json(JsonSerializer.Serialize(new { names = new[] { "ksa", topic } })));
        if (marker is not null)
            On("GET", Api + "/repos/Studio/MyMod/contents/.github/ksa-content-index.toml", () => Json(Content(marker)));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod"), null);

        Assert.Equal(
            proof is null
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod")
                : new ListingOwnership(ListingOwnershipState.Verified, proof, Repository: "Studio/MyMod"),
            ownership);
        Assert.All(_sent, sent => Assert.Null(sent.Authorization));
    }

    [Theory]
    [InlineData("alice", 7L, null)]
    [InlineData("alice-before-rename", 5L, ListingOwnershipProof.Owner)]
    public async Task CheckAsync_LoginWhoseAccountIdChanged_ProvesTheOwnerByTheIdOnly(string ownerLogin, long ownerId, ListingOwnershipProof? proof)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json(Repository("Studio/MyMod", ownerId, ownerLogin: ownerLogin)));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod"), null);

        Assert.Equal(proof, ownership.Proof);
        Assert.Equal(proof is null ? ListingOwnershipProblem.NoProof : null, ownership.Problem);
    }

    [Fact]
    public async Task CheckAsync_ForkMissingOrRenamedRepository_SaysWhichOne()
    {
        On("GET", Api + "/repos/alice/Forked", () => Json(Repository("alice/Forked", 5, fork: true)));
        On("GET", Api + "/repos/alice/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json(Repository("alice/NewName", 5)));
        var check = await SignedInAsync();

        var fork = await check.CheckAsync(Alice, Draft("alice/Forked"), null);
        var missing = await check.CheckAsync(Alice, Draft("alice/Gone"), null);
        var renamed = await check.CheckAsync(Alice, Draft("alice/OldName"), null);
        var noHost = await check.CheckAsync(Alice, new ListingDraft { Id = "MyMod" }, null);

        Assert.Equal(ListingOwnershipProblem.RepositoryFork, fork.Problem);
        Assert.Equal(ListingOwnershipProblem.RepositoryMissing, missing.Problem);
        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.RepositoryRenamed, Repository: "alice/OldName", RenamedTo: "alice/NewName"), renamed);
        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoHost), noHost);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.UnavailableForLegalReasons)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task CheckAsync_HostDoesNotAnswer_CouldNotEvaluateAndKeepsTheSession(HttpStatusCode status)
    {
        On("GET", Api + "/repos/Studio/MyMod", () => Json("""{"message":"No"}""", status));
        var session = await SignInAsync();
        var check = Check(session);

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod"), null);

        Assert.Equal(ListingOwnership.Unknown, ownership);
        Assert.Equal(GitHubSessionStatus.SignedIn, session.State.Status);
    }

    [Fact]
    public async Task CheckAsync_SpaceDockMod_ProvesItThroughItsSourceCodeLink()
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json("""{"id":4253,"game_id":22409,"source_code":"https://github.com/Studio/MyMod"}"""));
        OnProof("Studio/MyMod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/MyMod", SpaceDockMod: "4253"), ownership);
        Assert.Equal(new Uri("https://spacedock.info/mod/4253"), ownership.SpaceDockModUrl);
        Assert.Equal(new Uri("https://github.com/Studio/MyMod"), ownership.RepositoryUrl);
    }

    [Theory]
    [InlineData("""{"id":4253,"game_id":22409,"source_code":""}""", ListingOwnershipProblem.SpaceDockNoSourceLink)]
    [InlineData("""{"id":4253,"game_id":3102,"source_code":"https://github.com/Studio/MyMod"}""", ListingOwnershipProblem.SpaceDockModUnusable)]
    public async Task CheckAsync_SpaceDockModWithoutAUsableLink_NamesTheMod(string mod, ListingOwnershipProblem problem)
    {
        On("GET", "https://spacedock.info/api/mod/4253", () => Json(mod));
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft(spaceDock: 4253), null);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.NotVerified, Problem: problem, SpaceDockMod: "4253"), ownership);
    }

    [Fact]
    public async Task CheckAsync_ChangeOnTheSameHost_ChecksTheListedHost()
    {
        OnProof("studio/mymod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod") with { Name = "New name" }, Draft("studio/mymod"));

        Assert.Equal(ListingOwnershipState.Verified, ownership.State);
        Assert.DoesNotContain(_sent, sent => sent.Url == Api + "/repos/Studio/MyMod");
    }

    [Theory]
    [InlineData(true, true, ListingOwnershipState.Verified, "Studio/NewHost")]
    [InlineData(true, false, ListingOwnershipState.NotVerified, "Studio/NewHost")]
    [InlineData(false, true, ListingOwnershipState.NotVerified, "Studio/MyMod")]
    public async Task CheckAsync_ChangeThatMovesTheHost_NeedsBothProofs(bool listedProven, bool newProven, ListingOwnershipState state, string repository)
    {
        OnProof("Studio/MyMod", listedProven);
        OnProof("Studio/NewHost", newProven);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/NewHost"), Draft("Studio/MyMod"));

        Assert.Equal(state, ownership.State);
        Assert.Equal(repository, ownership.Repository);
    }

    [Fact]
    public async Task CheckAsync_ChangeToTheNewNameOfARenamedHost_ChecksTheNewName()
    {
        On("GET", Api + "/repos/Studio/OldName", () => Redirect(Api + "/repositories/42"));
        On("GET", Api + "/repositories/42", () => Json(Repository("Studio/NewName", 99)));
        OnProof("Studio/NewName", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/NewName"), Draft("Studio/OldName"));

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/NewName"), ownership);
    }

    [Theory]
    [InlineData(true, true, ListedForums)]
    [InlineData(true, false, null)]
    [InlineData(false, false, SubmittedForums)]
    public async Task CheckAsync_ForumsThread_IsTheListedOneForAnEdit(bool edit, bool listedThread, string? forums)
    {
        OnProof("Studio/MyMod", proven: false);
        var check = await SignedInAsync();
        var listed = Draft("Studio/MyMod") with { Links = listedThread ? [new ListingLink("forums", ListedForums)] : [] };
        var submitted = Draft("Studio/MyMod") with { Links = [new ListingLink("Forums", SubmittedForums)] };

        var ownership = await check.CheckAsync(Alice, submitted, edit ? listed : null);

        Assert.Equal(forums is null ? null : new Uri(forums), ownership.ForumsThread);
    }

    [Fact]
    public async Task CheckAsync_ForumsLinkThatIsNoThread_IsLeftOut()
    {
        OnProof("Studio/MyMod", proven: false);
        var check = await SignedInAsync();

        var ownership = await check.CheckAsync(Alice, Draft("Studio/MyMod") with { Links = [new ListingLink("forums", "https://example.com/threads/my-mod.783/")] }, null);

        Assert.Null(ownership.ForumsThread);
    }

    [Fact]
    public async Task CheckPullRequestAsync_ListingThatDiffersBetweenBaseAndHead_ChecksTheListedHost()
    {
        OnListing(Head, Document("alice/Mine", SubmittedForums));
        OnListing("main", Document("Studio/MyMod", ListedForums));
        On("GET", Api + "/repos/alice/Mine", () => Json(Repository("alice/Mine", 5)));
        OnProof("Studio/MyMod", proven: false);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(
            new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod", ForumsThread: new Uri(ListedForums)),
            ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url == Api + "/repos/alice/Mine");
        Assert.Equal(
            [("GET " + ListingUrl(Head), "Bearer " + Token), ("GET " + ListingUrl("main"), "Bearer " + Token)],
            _sent.Take(2).Select(sent => (sent.Line, sent.Authorization)));
        Assert.All(_sent.Skip(2), sent => Assert.Null(sent.Authorization));
    }

    [Theory]
    [InlineData(true, true, ListingOwnershipState.Verified, "Studio/NewHost")]
    [InlineData(true, false, ListingOwnershipState.NotVerified, "Studio/NewHost")]
    [InlineData(false, true, ListingOwnershipState.NotVerified, "Studio/MyMod")]
    public async Task CheckPullRequestAsync_HeadThatMovesTheHost_NeedsBothProofs(bool listedProven, bool newProven, ListingOwnershipState state, string repository)
    {
        OnListing(Head, Document("Studio/NewHost", ListedForums));
        OnListing("main", Document("Studio/MyMod", ListedForums));
        OnProof("Studio/MyMod", listedProven);
        OnProof("Studio/NewHost", newProven);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(state, ownership.State);
        Assert.Equal(repository, ownership.Repository);
    }

    [Fact]
    public async Task CheckPullRequestAsync_NewListing_ChecksTheHostOfTheHead()
    {
        OnListing(Head, Document("alice/Mine", SubmittedForums));
        On("GET", Api + "/repos/alice/Mine", () => Json(Repository("alice/Mine", 5)));
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "alice/Mine", ForumsThread: new Uri(SubmittedForums)), ownership);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task CheckPullRequestAsync_DocumentMissingOrNotParsing_CouldNotEvaluate(bool headThere, bool headParses, bool listedParses)
    {
        if (headThere)
            OnListing(Head, headParses ? Document("Studio/MyMod", ListedForums) : "[releases");
        OnListing("main", listedParses ? Document("Studio/MyMod", ListedForums) : "[releases");
        OnProof("Studio/MyMod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(ListingOwnership.Unknown, ownership);
        Assert.DoesNotContain(_sent, sent => sent.Url.StartsWith(Api + "/repos/Studio/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckPullRequestAsync_ListedDocumentThatCannotBeRead_CouldNotEvaluate(bool serverError)
    {
        OnListing(Head, Document("Studio/MyMod", ListedForums));
        On("GET", ListingUrl("main"), () => serverError
            ? Json("""{"message":"Server Error"}""", HttpStatusCode.BadGateway)
            : Json(JsonSerializer.Serialize(new { sha = "b1", encoding = "base64", content = Convert.ToBase64String([0x69, 0x64, 0x20, 0x3d, 0xe9]) })));
        OnProof("Studio/MyMod", proven: true);
        var check = await SignedInAsync();

        var ownership = await check.CheckPullRequestAsync(Alice, ListingPath, "main", Head);

        Assert.Equal(ListingOwnership.Unknown, ownership);
    }

    [Fact]
    public async Task CheckPullRequestAsync_EscapesThePathAndTheCommit()
    {
        var check = await SignedInAsync();

        await check.CheckPullRequestAsync(Alice, "listings/My Mod#1.toml", "main", "abc?x");

        Assert.Equal(Upstream + "/contents/listings/My%20Mod%231.toml?ref=abc%3Fx", _sent.Single().Url);
    }

    [Theory]
    [InlineData("packs/MyPack/pack.toml")]
    [InlineData("listings/nested/MyMod.toml")]
    [InlineData("listings/.toml")]
    [InlineData("listings/MyMod.json")]
    [InlineData("index-status.toml")]
    public async Task CheckPullRequestAsync_NoListingDocument_SendsNothing(string path)
    {
        var check = await SignedInAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => check.CheckPullRequestAsync(Alice, path, "main", Head));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task CheckPullRequestAsync_SignedOut_Throws()
    {
        var check = Check(new GitHubSession(Http(), "Iv1.testclient", "borea-test"));

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => check.CheckPullRequestAsync(Alice, ListingPath, "main", Head));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task CheckPullRequestAsync_TokenRefused_SignsOutAndThrows()
    {
        On("GET", ListingUrl(Head), () => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));
        var session = await SignInAsync();
        var check = Check(session);

        var failure = await Assert.ThrowsAsync<ListingPublishException>(() => check.CheckPullRequestAsync(Alice, ListingPath, "main", Head));

        Assert.Equal(ListingPublishFailure.SignedOut, failure.Failure);
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    private async Task<ListingOwnershipCheck> SignedInAsync() => Check(await SignInAsync());

    private async Task<GitHubSession> SignInAsync()
    {
        var session = new GitHubSession(Http(), "Iv1.testclient", "borea-test", new InstantTimeProvider());
        Assert.True((await session.SignInAsync()).SignedIn);
        _sent.Clear();
        return session;
    }

    private ListingOwnershipCheck Check(IGitHubSession session) => new(session, Http(), new TableFormat());

    private HttpClient Http() => new(new FakeHttpMessageHandler(request => Task.FromResult(Respond(request))));

    private void On(string method, string url, Func<HttpResponseMessage> answer) => _routes[method + " " + url] = answer;

    private void OnProof(string repository, bool proven)
    {
        On("GET", $"{Api}/repos/{repository}", () => Json(Repository(repository, 99)));
        On("GET", $"{Api}/repos/{repository}/topics", () => Json(proven ? """{"names":["ksa-index-alice"]}""" : """{"names":[]}"""));
    }

    private void OnListing(string reference, string text) => On("GET", ListingUrl(reference), () => Json(Content(text)));

    private static string ListingUrl(string reference) => $"{Upstream}/contents/{ListingPath}?ref={Uri.EscapeDataString(reference)}";

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url.StartsWith(Api, StringComparison.Ordinal) || url.StartsWith("https://spacedock.info/", StringComparison.Ordinal))
            _sent.Add(new Sent(request.Method.Method, url, request.Headers.Authorization?.ToString()));

        return _routes.TryGetValue(request.Method.Method + " " + url, out var answer)
            ? answer()
            : Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound);
    }

    private static ListingDraft Draft(string? github = null, long? spaceDock = null) => new()
    {
        Id = "MyMod",
        Name = "My Mod",
        Releases = new ListingReleases(github, spaceDock),
    };

    private static string Document(string github, string forums) =>
        $"id = \"MyMod\"\nname = \"My Mod\"\n[releases]\ngithub = \"{github}\"\n[links]\nforums = \"{forums}\"\n";

    private static string Repository(string fullName, long ownerId, bool fork = false, string? ownerLogin = null) =>
        JsonSerializer.Serialize(new { full_name = fullName, fork, owner = new { id = ownerId, login = ownerLogin ?? fullName.Split('/')[0] } });

    private static string Content(string text) =>
        JsonSerializer.Serialize(new { sha = "b1", encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Insert(4, "\n") });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Content = new StringContent("""{"message":"Moved Permanently"}""") };
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed record Sent(string Method, string Url, string? Authorization)
    {
        public string Line => Method + " " + Url;
    }

    /// <summary>Reads <c>key = "value"</c> and <c>key = 123</c> lines under <c>[table]</c> headers.</summary>
    private sealed class TableFormat : IListingFormat
    {
        public string Write(AuthoredTable document, string? original = null) => throw new NotSupportedException();

        public AuthoredTable Read(string text)
        {
            var root = new AuthoredTable();
            var table = root;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith('['))
                {
                    if (!line.EndsWith(']'))
                        throw new FormatException("An unclosed table header.");

                    table = new AuthoredTable();
                    root.Set(line[1..^1], table);
                    continue;
                }

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    throw new FormatException("A line without a value.");

                table.Set(parts[0], parts[1].StartsWith('"') ? parts[1].Trim('"')
                    : long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
                    : throw new FormatException("A bare word."));
            }

            return root;
        }
    }

    /// <summary>Fires every delay at once, so the sign-in does not wait for its poll interval.</summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new FiredTimer();
        }

        private sealed class FiredTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
