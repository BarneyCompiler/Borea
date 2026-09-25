using System.ComponentModel;
using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Network.GitHub;

namespace Borea.App.Tests.ViewModels;

public sealed class GitHubAccountViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeGitHubSession _session = new();

    [Theory]
    [InlineData("", "")]
    [InlineData("Iv23.testclient", "")]
    [InlineData("", "borea-test")]
    public async Task WithoutClientIdOrSlug_HidesTheAccountAndDoesNotSignIn(string clientId, string slug)
    {
        var session = new GitHubSession(new HttpClient(), clientId, slug);
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session);
        var viewModel = harness.ViewModel;

        viewModel.SignInToGitHubCommand.Execute(null);

        Assert.False(viewModel.IsGitHubAccountAvailable);
        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.False(await viewModel.SignInToGitHubAsync());
        Assert.Equal(GitHubSessionStatus.SignedOut, session.State.Status);
    }

    [Fact]
    public async Task BuiltInApp_ShowsTheAccountOnlyWithClientIdAndSlug()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        Assert.Equal(BoreaGitHubApp.ClientId.Length > 0 && BoreaGitHubApp.Slug.Length > 0, harness.ViewModel.IsGitHubAccountAvailable);
    }

    [Fact]
    public async Task SignedOut_ShowsTheSignIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsGitHubAccountAvailable);
        Assert.False(viewModel.IsGitHubSignedIn);
        Assert.Null(viewModel.GitHubLogin);
        Assert.False(viewModel.IsGitHubSignInOpen);
    }

    [Fact]
    public async Task SignIn_ShowsTheCodeOpensGitHubAndEndsSignedIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var window = new FakeWindowServices();
        var opened = new List<string>();
        viewModel.WindowServices = window;
        viewModel.OpenWithSystem = opened.Add;

        viewModel.SignInToGitHubCommand.Execute(null);

        Assert.True(viewModel.IsGitHubSignInOpen);
        Assert.True(viewModel.IsGettingGitHubCode);
        _session.ReportCode();
        await WaitUntilAsync(() => viewModel.HasGitHubCode);
        Assert.Equal("WDJB-MJHT", viewModel.GitHubUserCode);
        Assert.False(viewModel.IsGettingGitHubCode);

        await viewModel.CopyGitHubCodeAndOpenCommand.ExecuteAsync(null);

        Assert.Equal(["WDJB-MJHT"], window.Copied);
        Assert.Equal(["https://github.com/login/device"], opened);

        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.False(viewModel.IsGitHubSignInRunning);
        Assert.Null(viewModel.GitHubUserCode);
        Assert.True(viewModel.IsGitHubSignedIn);
        Assert.Equal("octocat", viewModel.GitHubLogin);
        Assert.Equal("Signed in as octocat", viewModel.GitHubSignedInText);
    }

    [Fact]
    public async Task SignInToGitHubAsync_CompletesWhenTheModalCloses()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        var signIn = viewModel.SignInToGitHubAsync();
        Assert.True(viewModel.IsGitHubSignInOpen);
        Assert.Same(signIn, viewModel.SignInToGitHubAsync());

        _session.Finish(GitHubSignInOutcome.SignedIn);

        Assert.True(await signIn.WaitAsync(Timeout));
        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.True(await viewModel.SignInToGitHubAsync());
    }

    [Fact]
    public async Task SignInToGitHubAsync_RetryAfterAFailure_KeepsWaitingUntilTheModalCloses()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var signIn = viewModel.SignInToGitHubAsync();
        _session.Finish(GitHubSignInOutcome.Expired);
        await viewModel.WhenGitHubSignInDoneAsync();
        Assert.False(signIn.IsCompleted);

        viewModel.SignInToGitHubCommand.Execute(null);
        viewModel.CancelGitHubSignInCommand.Execute(null);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.False(await signIn.WaitAsync(Timeout));
        Assert.False(viewModel.IsGitHubSignInOpen);
    }

    [Fact]
    public async Task SignInToGitHubAsync_RightAfterACancel_OpensTheModalWhenTheCancelledRunEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        _session.HoldCancel = true;
        viewModel.SignInToGitHubCommand.Execute(null);
        viewModel.CancelGitHubSignInCommand.Execute(null);
        Assert.True(viewModel.IsGitHubSignInRunning);

        var signIn = viewModel.SignInToGitHubAsync();
        Assert.False(viewModel.IsGitHubSignInOpen);
        _session.HoldCancel = false;
        _session.EndCancel();
        await WaitUntilAsync(() => _session.SignIns == 2);

        Assert.True(viewModel.IsGitHubSignInOpen);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        Assert.True(await signIn.WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(GitHubSignInOutcome.Expired, "The code expired. Try again for a new one.")]
    [InlineData(GitHubSignInOutcome.AccessDenied, "The sign-in was refused on GitHub.")]
    [InlineData(GitHubSignInOutcome.DeviceFlowDisabled, "Sign-in with a code is turned off for Borea on GitHub. Report the problem.")]
    [InlineData(GitHubSignInOutcome.NetworkError, "Cannot reach GitHub. Check your connection and try again.")]
    public async Task FailedSignIn_KeepsTheModalWithTheReason(GitHubSignInOutcome outcome, string message)
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(outcome);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.True(viewModel.IsGitHubSignInOpen);
        Assert.Equal(message, viewModel.GitHubSignInError);
        Assert.True(viewModel.CanRetryGitHubSignIn);
        Assert.False(viewModel.IsGitHubSignedIn);
    }

    [Fact]
    public async Task Cancel_ClosesTheModalAndStopsTheSignIn()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        viewModel.SignInToGitHubCommand.Execute(null);

        viewModel.CancelGitHubSignInCommand.Execute(null);
        await viewModel.WhenGitHubSignInDoneAsync();

        Assert.True(_session.WasCancelled);
        Assert.False(viewModel.IsGitHubSignInOpen);
        Assert.False(viewModel.IsGitHubSignInRunning);
        Assert.Null(viewModel.GitHubSignInError);
        Assert.False(viewModel.IsGitHubSignedIn);
    }

    [Fact]
    public async Task SignedIn_SignsOutAndOpensTheAccessPage()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        Assert.True(viewModel.IsGitHubSignedIn);

        viewModel.OpenGitHubAccessCommand.Execute(null);
        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.Equal([FakeGitHubSession.AccessUrl], opened);
        Assert.False(viewModel.IsGitHubSignedIn);
        Assert.Null(viewModel.GitHubLogin);
    }

    [Fact]
    public async Task SignOutByTheSession_AfterARebuild_UpdatesTheAccount()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        viewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsGitHubSignedIn);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) =>
        {
            lock (changes)
                changes.Add(e.PropertyName);
        };

        _session.SignOut();

        await WaitUntilAsync(() =>
        {
            lock (changes)
                return changes.Contains(nameof(MainViewModel.IsGitHubSignedIn));
        });
        Assert.False(viewModel.IsGitHubSignedIn);
        lock (changes)
            Assert.Single(changes, name => name == nameof(MainViewModel.IsGitHubSignedIn));
    }

    [Theory]
    [InlineData("always", "always", true)]
    [InlineData("never", "pull_requests_only", true)]
    [InlineData("never", "never", false)]
    [InlineData(null, null, false)]
    public async Task SignedIn_ShowsTheStewardRoleOfEitherIndexRepository(string? index, string? releases, bool steward)
    {
        _session.Bypass["KSAModding/content-index"] = index;
        _session.Bypass["KSAModding/content-index-releases"] = releases;
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;

        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.Equal(steward, viewModel.IsGitHubSteward);
        Assert.Equal("Steward of the content index", harness.Localization.SettingsGitHubSteward);
        Assert.Contains("https://api.github.com/repos/KSAModding/content-index/rulesets/1", _session.Sent);
        Assert.Contains("https://api.github.com/repos/KSAModding/content-index-releases/rulesets/1", _session.Sent);
    }

    [Fact]
    public async Task SignIn_ShowsTheStewardRoleWhenItsCheckEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        var shown = new List<bool>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGitHubSteward))
            {
                lock (shown)
                    shown.Add(viewModel.IsGitHubSteward);
            }
        };
        var hold = _session.Hold = new TaskCompletionSource();

        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();
        Assert.False(viewModel.IsGitHubSteward);
        hold.SetResult();
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.True(viewModel.IsGitHubSteward);
        await WaitUntilAsync(() =>
        {
            lock (shown)
                return shown[^1];
        });
    }

    [Fact]
    public async Task SignOut_HidesTheStewardRoleAndTheNextSignInChecksAgain()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.WhenStewardRoleCheckedAsync();
        Assert.True(viewModel.IsGitHubSteward);

        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.False(viewModel.IsGitHubSteward);
        Assert.Null(harness.Services.StewardRole.Current);

        _session.Bypass["KSAModding/content-index"] = "never";
        _session.Bypass["KSAModding/content-index-releases"] = "never";
        _session.Login = "alice";
        viewModel.SignInToGitHubCommand.Execute(null);
        _session.Finish(GitHubSignInOutcome.SignedIn);
        await viewModel.WhenGitHubSignInDoneAsync();
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.Equal("alice", viewModel.GitHubLogin);
        Assert.Equal("alice", harness.Services.StewardRole.Current?.Login);
        Assert.False(viewModel.IsGitHubSteward);
    }

    [Fact]
    public async Task Rebuild_ChecksTheStewardRoleAgain()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: _session);
        var viewModel = harness.ViewModel;
        await viewModel.WhenStewardRoleCheckedAsync();
        var before = harness.Services;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        viewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        await viewModel.WhenStewardRoleCheckedAsync();

        Assert.NotSame(before, harness.Services);
        Assert.True(viewModel.IsGitHubSteward);
        Assert.Null(before.StewardRole.Current);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeWindowServices : IWindowServices
    {
        public List<string> Copied { get; } = [];

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(null);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult<PickedBinaryFile?>(null);

        public Task CopyTextAsync(string text)
        {
            Copied.Add(text);
            return Task.CompletedTask;
        }
    }

    /// <summary>A session whose sign-in waits until the test reports the code and finishes it. It answers the rulesets of the index repositories.</summary>
    private sealed class FakeGitHubSession : IGitHubSession
    {
        public const string AccessUrl = "https://github.com/settings/apps/authorizations";

        private readonly GitHubDeviceCode _code = new("WDJB-MJHT", "https://github.com/login/device");
        private TaskCompletionSource<GitHubSignInResult>? _signIn;
        private IProgress<GitHubDeviceCode>? _progress;
        private TaskCompletionSource<GitHubSignInResult>? _heldCancel;

        public bool IsAvailable => true;

        public string ManageAccessUrl => AccessUrl;

        public string InstallUrl => "https://github.com/apps/borea-test/installations/new";

        public string InstallUrlFor(long repositoryId) => InstallUrl + "?repository=" + repositoryId;

        public GitHubSessionState State { get; private set; } = GitHubSessionState.SignedOut;

        public bool WasCancelled { get; private set; }

        public int SignIns { get; private set; }

        /// <summary>True keeps a cancelled sign-in running until <see cref="EndCancel"/>.</summary>
        public bool HoldCancel { get; set; }

        public string Login { get; set; } = "octocat";

        /// <summary>current_user_can_bypass of the main ruleset per repository, which the ruleset leaves out for null.</summary>
        public Dictionary<string, string?> Bypass { get; } = new()
        {
            ["KSAModding/content-index"] = "always",
            ["KSAModding/content-index-releases"] = "always",
        };

        public System.Collections.Concurrent.ConcurrentQueue<string> Sent { get; } = new();

        /// <summary>Holds every answer until it is set.</summary>
        public TaskCompletionSource? Hold { get; set; }

        public event EventHandler? StateChanged;

        public async Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default)
        {
            _progress = progress;
            var signIn = _signIn = new TaskCompletionSource<GitHubSignInResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            SignIns++;
            using var registration = cancellationToken.Register(() =>
            {
                WasCancelled = true;
                if (HoldCancel)
                    _heldCancel = signIn;
                else
                    signIn.TrySetCanceled(cancellationToken);
            });

            var result = await signIn.Task;
            if (result.SignedIn)
                SignInDirectly();

            return result;
        }

        public void ReportCode()
        {
            State = GitHubSessionState.WaitingFor(_code);
            _progress?.Report(_code);
        }

        public void EndCancel() => _heldCancel?.TrySetCanceled();

        public void Finish(GitHubSignInOutcome outcome) =>
            _signIn!.SetResult(new GitHubSignInResult(outcome, outcome == GitHubSignInOutcome.SignedIn ? Login : null));

        public void SignInDirectly()
        {
            State = GitHubSessionState.SignedInAs(Login);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SignOut()
        {
            State = GitHubSessionState.SignedOut;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            if (Hold is { } hold)
                await hold.Task.WaitAsync(cancellationToken);

            var url = request.RequestUri!.AbsoluteUri;
            Sent.Enqueue(url);
            var repository = Bypass.Keys.FirstOrDefault(name => url.StartsWith($"https://api.github.com/repos/{name}/rulesets", StringComparison.Ordinal));
            var body = repository is null ? null
                : url.EndsWith("/rulesets?per_page=100&page=1", StringComparison.Ordinal) ? """[{"id":1,"target":"branch","enforcement":"active"}]"""
                : url.EndsWith("/rulesets/1", StringComparison.Ordinal) ? """{"id":1,"target":"branch","enforcement":"active","conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}}"""
                    + (Bypass[repository] is { } bypass ? $$""","current_user_can_bypass":"{{bypass}}"}""" : "}")
                : null;
            return new HttpResponseMessage(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? """{"message":"Not Found"}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
