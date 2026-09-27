using System;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.GitHub;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The GitHub account in Settings and the sign-in modal with the device code.
/// The steward role is checked after every sign-in, and the listing page signs out only a session that it started itself.
/// </summary>
public partial class MainViewModel
{
    private SynchronizationContext? _gitHubContext;
    private CancellationTokenSource? _gitHubSignIn;
    private Task? _gitHubSignInRun;
    private TaskCompletionSource<bool>? _gitHubSignInClosed;
    private string? _gitHubVerificationUri;
    private bool _gitHubSignInForListing;
    private bool _isGitHubSessionFromListing;
    private Task? _stewardRoleCheck;

    /// <summary>False hides the GitHub account, because this build has no GitHub App to sign in with.</summary>
    public bool IsGitHubAccountAvailable => _services?.GitHub.IsAvailable == true;

    public bool IsGitHubSignedIn => _services?.GitHub.State.Status == GitHubSessionStatus.SignedIn;

    public string? GitHubLogin => _services?.GitHub.State.Login;

    public string? GitHubSignedInText => GitHubLogin is { } login ? Localization.FormatSettingsGitHubSignedInAs(login) : null;

    public string? GitHubManageAccessUrl => _services?.GitHub.ManageAccessUrl;

    /// <summary>GitHub lets the signed-in account bypass the ruleset of main in content-index or content-index-releases.</summary>
    public bool IsGitHubSteward => IsGitHubSignedIn && _services?.StewardRole.Current?.IsSteward == true;

    [ObservableProperty]
    private bool _isGitHubSignInOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGettingGitHubCode))]
    [NotifyPropertyChangedFor(nameof(CanRetryGitHubSignIn))]
    private bool _isGitHubSignInRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitHubCode))]
    [NotifyPropertyChangedFor(nameof(IsGettingGitHubCode))]
    private string? _gitHubUserCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryGitHubSignIn))]
    private string? _gitHubSignInError;

    [ObservableProperty]
    private string? _gitHubAccountError;

    public bool HasGitHubCode => GitHubUserCode is not null;

    public bool IsGettingGitHubCode => IsGitHubSignInRunning && GitHubUserCode is null;

    public bool CanRetryGitHubSignIn => !IsGitHubSignInRunning && GitHubSignInError is not null;

    internal Task WhenGitHubSignInDoneAsync() => _gitHubSignInRun ?? Task.CompletedTask;

    internal Task WhenStewardRoleCheckedAsync() => _stewardRoleCheck ?? Task.CompletedTask;

    /// <summary>Follows the session of <paramref name="services"/> instead of the one of <paramref name="previous"/>.</summary>
    private void AttachGitHubSession(BoreaServices? previous, BoreaServices? services)
    {
        _gitHubContext ??= SynchronizationContext.Current;
        if (previous is not null)
        {
            previous.GitHub.StateChanged -= OnGitHubStateChanged;
            previous.StewardRole.Changed -= OnStewardRoleChanged;
        }

        if (services is not null)
        {
            services.GitHub.StateChanged += OnGitHubStateChanged;
            services.StewardRole.Changed += OnStewardRoleChanged;
        }

        RefreshGitHubAccount();
    }

    // a request of another page can sign the session out on any thread
    private void OnGitHubStateChanged(object? sender, EventArgs e) => OnGitHubContext(RefreshGitHubAccount);

    private void OnStewardRoleChanged(object? sender, EventArgs e) => OnGitHubContext(RefreshStewardRole);

    private void OnGitHubContext(Action refresh)
    {
        if (_gitHubContext is { } context)
            context.Post(_ => refresh(), null);
        else
            refresh();
    }

    private void RefreshGitHubAccount()
    {
        OnPropertyChanged(nameof(IsGitHubAccountAvailable));
        OnPropertyChanged(nameof(IsGitHubSignedIn));
        OnPropertyChanged(nameof(GitHubLogin));
        OnPropertyChanged(nameof(GitHubSignedInText));
        OnPropertyChanged(nameof(GitHubManageAccessUrl));
        RefreshStewardRole();
    }

    /// <summary>
    /// Checks a new sign-in, because the role drops its result on every change of the session and a rebuilt graph starts without one.
    /// The role lets a call during a check share it.
    /// </summary>
    private void RefreshStewardRole()
    {
        OnPropertyChanged(nameof(IsGitHubSteward));
        RefreshIndexStatusRights();
        if (_services is { } services && IsGitHubSignedIn && services.StewardRole.Current is null)
            _stewardRoleCheck = services.StewardRole.CheckAsync();
    }

    [RelayCommand]
    private void SignInToGitHub() => _ = SignInToGitHubAsync();

    /// <summary>
    /// Opens the sign-in modal unless already signed in, and completes when the modal closes,
    /// with true when the session is signed in then.
    /// </summary>
    /// <param name="forListing">The listing page asks, so the session it starts ends with that page.</param>
    internal Task<bool> SignInToGitHubAsync(bool forListing = false)
    {
        if (_services is not { } services || !services.GitHub.IsAvailable)
            return Task.FromResult(false);

        if (IsGitHubSignedIn)
            return Task.FromResult(true);

        if (IsGitHubSignInRunning && !IsGitHubSignInOpen)
            return SignInToGitHubAfterCancelAsync(forListing);

        var closed = _gitHubSignInClosed ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!IsGitHubSignInRunning)
        {
            // A retry in the open modal keeps the side that opened it.
            if (!IsGitHubSignInOpen)
                _gitHubSignInForListing = forListing;

            GitHubAccountError = null;
            IsGitHubSignInOpen = true;
            _gitHubSignInRun = RunGitHubSignInAsync(services.GitHub, _gitHubSignInForListing);
        }

        return closed.Task;
    }

    // a cancelled run holds the session until it ends
    private async Task<bool> SignInToGitHubAfterCancelAsync(bool forListing)
    {
        await WhenGitHubSignInDoneAsync();
        return await SignInToGitHubAsync(forListing);
    }

    partial void OnIsGitHubSignInOpenChanged(bool value)
    {
        if (value || _gitHubSignInClosed is not { } closed)
            return;

        _gitHubSignInClosed = null;
        closed.TrySetResult(IsGitHubSignedIn);
    }

    private async Task RunGitHubSignInAsync(IGitHubSession session, bool forListing)
    {
        _gitHubSignIn?.Dispose();
        var cancel = _gitHubSignIn = new CancellationTokenSource();
        GitHubSignInError = null;
        GitHubUserCode = null;
        _gitHubVerificationUri = null;
        IsGitHubSignInRunning = true;
        var progress = new Progress<GitHubDeviceCode>(code =>
        {
            if (_gitHubSignIn != cancel || !IsGitHubSignInRunning)
                return;

            _gitHubVerificationUri = code.VerificationUri;
            GitHubUserCode = code.UserCode;
        });

        try
        {
            var result = await session.SignInAsync(progress, cancel.Token);
            if (result.SignedIn)
            {
                _isGitHubSessionFromListing = forListing;
                IsGitHubSignInOpen = false;
            }
            else
            {
                GitHubSignInError = GitHubSignInErrorText(result.Outcome);
            }
        }
        catch (OperationCanceledException)
        {
            // the user cancelled or signed out
        }
        finally
        {
            IsGitHubSignInRunning = false;
            GitHubUserCode = null;
            _gitHubVerificationUri = null;
            RefreshGitHubAccount();
        }
    }

    private string GitHubSignInErrorText(GitHubSignInOutcome outcome) => outcome switch
    {
        GitHubSignInOutcome.Expired => Localization.GitHubSignInExpired,
        GitHubSignInOutcome.AccessDenied => Localization.GitHubSignInDenied,
        GitHubSignInOutcome.IncorrectClientCredentials => Localization.GitHubSignInClientRejected,
        GitHubSignInOutcome.IncorrectDeviceCode => Localization.GitHubSignInCodeRejected,
        GitHubSignInOutcome.UnsupportedGrantType => Localization.GitHubSignInRequestRejected,
        GitHubSignInOutcome.DeviceFlowDisabled => Localization.GitHubSignInDeviceFlowDisabled,
        GitHubSignInOutcome.NetworkError => Localization.GitHubSignInNetworkError,
        _ => Localization.GitHubSignInUnexpected,
    };

    [RelayCommand]
    private async Task CopyGitHubCodeAndOpenAsync()
    {
        if (GitHubUserCode is not { } code || _gitHubVerificationUri is not { } page)
            return;

        if (WindowServices is { } window)
            await window.CopyTextAsync(code);

        GitHubSignInError = TryOpenWithSystem(page);
    }

    [RelayCommand]
    private void CancelGitHubSignIn()
    {
        _gitHubSignIn?.Cancel();
        IsGitHubSignInOpen = false;
    }

    /// <summary>Ends the session when the listing page started it, and keeps one started in Settings.</summary>
    internal void SignOutOfGitHubForListing()
    {
        if (_isGitHubSessionFromListing)
            _services?.GitHub.SignOut();
    }

    [RelayCommand]
    private void SignOutOfGitHub()
    {
        GitHubAccountError = null;
        _services?.GitHub.SignOut();
        RefreshGitHubAccount();
    }

    [RelayCommand]
    private void OpenGitHubAccess()
    {
        if (GitHubManageAccessUrl is { } url)
            GitHubAccountError = TryOpenWithSystem(url);
    }
}
