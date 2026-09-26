using System.Globalization;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The confirmation of one steward action on the pull request that a review shows. It names the repository, the pull request and the head commit,
/// and sends once. A merge that skips a review which GitHub still requires asks a second time.
/// </summary>
public sealed partial class PullRequestActionDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly StewardReview _review;
    private Task _run = Task.CompletedTask;

    public PullRequestActionDialog(MainViewModel owner, StewardReview review, PullRequestReview pullRequest, PullRequestAction action)
    {
        _owner = owner;
        _review = review;
        PullRequest = pullRequest;
        Action = action;
    }

    /// <summary>The pull request as the review showed it when the steward clicked.</summary>
    public PullRequestReview PullRequest { get; }

    public PullRequestAction Action { get; }

    public string Title
    {
        get
        {
            var target = $"{PullRequest.Repository} #{PullRequest.Number.ToString(CultureInfo.InvariantCulture)}";
            return Action switch
            {
                PullRequestAction.Approve => _owner.Localization.FormatStewardActionApproveTitle(target),
                PullRequestAction.RequestChanges => _owner.Localization.FormatStewardActionRequestChangesTitle(target),
                PullRequestAction.Comment => _owner.Localization.FormatStewardActionCommentTitle(target),
                PullRequestAction.Merge => _owner.Localization.FormatStewardActionMergeTitle(target),
                _ => _owner.Localization.FormatStewardActionCloseTitle(target),
            };
        }
    }

    public string PullRequestTitle => PullRequest.Title;

    public string Effect
    {
        get
        {
            var commit = PullRequest.HeadCommit[..7];
            return Action switch
            {
                PullRequestAction.Approve => _owner.Localization.FormatStewardActionApproveEffect(commit),
                PullRequestAction.RequestChanges => _owner.Localization.FormatStewardActionRequestChangesEffect(commit),
                PullRequestAction.Comment => _owner.Localization.FormatStewardActionCommentEffect(commit),
                PullRequestAction.Merge => _owner.Localization.FormatStewardActionMergeEffect(commit, PullRequest.BaseBranch),
                _ => _owner.Localization.StewardActionCloseEffect,
            };
        }
    }

    /// <summary>A merge sends no text, and an approval can go without one.</summary>
    public bool HasText => Action != PullRequestAction.Merge;

    public bool NeedsText => Action is PullRequestAction.RequestChanges or PullRequestAction.Comment or PullRequestAction.Close;

    public string TextHint => NeedsText ? _owner.Localization.StewardActionTextRequired : _owner.Localization.StewardActionTextOptional;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _text = string.Empty;

    /// <summary>While the action is being sent, the dialog cannot close, so the steward sees how it ended.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isSending;

    /// <summary>GitHub still requires a review, and the next click merges without it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendText))]
    private bool _isReviewSkipAsked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(DoneText))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isDone;

    /// <summary>The pull request is no longer as the review showed it, so nothing more is sent from this dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isStale;

    [ObservableProperty]
    private string? _error;

    public string? OwnWarning => _review.OwnWarning;

    public bool CanSend => !IsSending && !IsDone && !IsStale && (!NeedsText || Text.Trim().Length > 0);

    public bool CanClose => !IsSending;

    public string SendText => Action switch
    {
        PullRequestAction.Approve => _owner.Localization.StewardActionApprove,
        PullRequestAction.RequestChanges => _owner.Localization.StewardActionRequestChanges,
        PullRequestAction.Comment => _owner.Localization.StewardActionComment,
        PullRequestAction.Merge when IsReviewSkipAsked => _owner.Localization.StewardActionMergeWithoutReview,
        PullRequestAction.Merge => _owner.Localization.StewardActionMerge,
        _ => _owner.Localization.StewardActionClose,
    };

    public string? DoneText => !IsDone ? null : Action switch
    {
        PullRequestAction.Merge => _owner.Localization.StewardActionMerged,
        PullRequestAction.Close => _owner.Localization.StewardActionClosed,
        _ => _owner.Localization.StewardActionReviewed,
    };

    internal Task WhenDoneAsync() => _run;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync()
    {
        // a second click while the first one runs sends nothing
        if (!CanSend)
            return _run;

        return _run = RunAsync();
    }

    private async Task RunAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsSending = true;
        Error = null;
        try
        {
            var actions = services.PullRequestActions;
            var text = Text.Trim();
            await (Action switch
            {
                PullRequestAction.Approve => actions.ReviewAsync(PullRequest, PullRequestReviewKind.Approve, text.Length > 0 ? text : null),
                PullRequestAction.RequestChanges => actions.ReviewAsync(PullRequest, PullRequestReviewKind.RequestChanges, text),
                PullRequestAction.Comment => actions.ReviewAsync(PullRequest, PullRequestReviewKind.Comment, text),
                PullRequestAction.Merge => actions.MergeAsync(PullRequest, skipRequiredReview: IsReviewSkipAsked),
                _ => actions.CloseAsync(PullRequest, text),
            });
            IsDone = true;
            _ = _review.RefreshAsync();
        }
        catch (PullRequestRefusedException exception) when (exception.Refusal == PullRequestRefusal.ReviewRequired && !IsReviewSkipAsked)
        {
            IsReviewSkipAsked = true;
        }
        catch (PullRequestRefusedException exception)
        {
            // the review showed something that is no longer true, so the steward decides again on the pull request as it is now
            Error = _owner.PullRequestRefusalText(exception.Refusal);
            IsStale = true;
            _ = _review.RefreshAsync();
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardPullRequestErrorText(exception);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (CanClose)
            _owner.ClosePullRequestAction(this);
    }
}

public enum PullRequestAction
{
    Approve,
    RequestChanges,
    Comment,
    Merge,
    Close,
}
