using Borea.App.ViewModels;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardReviewActionsViewModelTests
{
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";

    private readonly StewardSession _session = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakePullRequestReviews _reviews = new();
    private readonly FakePullRequestActions _actions = new();

    [Fact]
    public async Task Approve_NamesTheRepositoryAndThePullRequest_AndSendsTheHeadCommitThatTheReviewShows()
    {
        using var harness = await CreateAsync(Mergeable(5));
        var review = await OpenReviewAsync(harness.ViewModel);

        review.ApproveCommand.Execute(null);
        var dialog = harness.ViewModel.StewardAction!;
        var before = (dialog.Title, dialog.PullRequestTitle, dialog.Effect, dialog.CanSend, dialog.HasText, dialog.TextHint);
        await dialog.SendCommand.ExecuteAsync(null);
        await review.WhenLoadedAsync();

        Assert.Equal(
            ("Approve KSAModding/content-index #5", "Pull 5", "Borea sends a review that approves commit 0123456.", true, true, harness.Localization.StewardActionTextOptional),
            before);
        var sent = Assert.Single(_actions.Sent);
        Assert.Equal(("Approve", Index, 5, FakePullRequestReviews.Head, null), (sent.Action, sent.PullRequest.Repository, sent.PullRequest.Number, sent.PullRequest.HeadCommit, sent.Text));
        Assert.Equal((true, "GitHub has the review.", null), (dialog.IsDone, dialog.DoneText, dialog.Error));
        Assert.Equal([(Index, 5), (Index, 5)], _reviews.Reads);

        dialog.CloseCommand.Execute(null);
        Assert.False(harness.ViewModel.IsStewardActionOpen);
    }

    [Theory]
    [InlineData(PullRequestAction.RequestChanges, "RequestChanges", "Request changes on KSAModding/content-index #5", "Request changes")]
    [InlineData(PullRequestAction.Comment, "Comment", "Comment on KSAModding/content-index #5", "Comment")]
    [InlineData(PullRequestAction.Close, "Close", "Close KSAModding/content-index #5", "Close")]
    public async Task RequestChangesCommentAndClose_NeedATextAndSendIt(PullRequestAction action, string sentAction, string title, string button)
    {
        using var harness = await CreateAsync(Mergeable(5));
        var review = await OpenReviewAsync(harness.ViewModel);

        (action switch
        {
            PullRequestAction.RequestChanges => review.RequestChangesCommand,
            PullRequestAction.Comment => review.CommentCommand,
            _ => review.ClosePullRequestCommand,
        }).Execute(null);
        var dialog = harness.ViewModel.StewardAction!;
        dialog.Text = "   ";
        var empty = (dialog.CanSend, dialog.SendCommand.CanExecute(null));
        await dialog.SendCommand.ExecuteAsync(null);
        dialog.Text = " Please fix the abstract. ";
        await dialog.SendCommand.ExecuteAsync(null);

        Assert.Equal((false, false), empty);
        Assert.Equal((title, button, harness.Localization.StewardActionTextRequired), (dialog.Title, dialog.SendText, dialog.TextHint));
        var sent = Assert.Single(_actions.Sent);
        Assert.Equal((sentAction, "Please fix the abstract.", FakePullRequestReviews.Head), (sent.Action, sent.Text, sent.PullRequest.HeadCommit));
        Assert.Equal(action == PullRequestAction.Close ? "The pull request is closed." : "GitHub has the review.", dialog.DoneText);
    }

    public static TheoryData<string, PullRequestReview, string> Blocked => new()
    {
        { "failure", Mergeable(5) with { Validate = new ValidateStatus(ValidateState.Failure) }, "Merge is off until validate passed on the head commit." },
        { "pending", Mergeable(5) with { Validate = new ValidateStatus(ValidateState.Pending) }, "Merge is off until validate passed on the head commit." },
        { "missing", Mergeable(5) with { Validate = new ValidateStatus(ValidateState.Missing) }, "Merge is off until validate passed on the head commit." },
        { "unreadable", Mergeable(5) with { Validate = null, ValidateFailure = new StewardException(StewardFailure.RateLimited) }, "Merge is off until validate passed on the head commit." },
        { "no verdict", Mergeable(5) with { Verdict = null }, "Merge is off until the checks left their verdict." },
        { "neither", Mergeable(5) with { Validate = new ValidateStatus(ValidateState.Error), Verdict = null }, "Merge is off until validate passed on the head commit. Merge is off until the checks left their verdict." },
        { "draft", Mergeable(5) with { IsDraft = true }, "Merge is off for a draft." },
    };

    [Theory]
    [MemberData(nameof(Blocked))]
    public async Task Merge_IsOffUntilValidatePassedOnTheHeadCommitAndTheVerdictIsThere_AndSaysWhichIsMissing(string name, PullRequestReview pull, string text)
    {
        using var harness = await CreateAsync(pull);
        var review = await OpenReviewAsync(harness.ViewModel);

        review.MergeCommand.Execute(null);

        Assert.True(review.CanAct, name);
        Assert.Equal((false, text), (review.CanMerge, review.MergeBlockedText));
        Assert.False(harness.ViewModel.IsStewardActionOpen);
        Assert.Empty(_actions.Sent);
    }

    [Fact]
    public async Task Merge_GitHubStillRequiresAReview_AsksASecondTime_AndThenMergesWithoutIt()
    {
        using var harness = await CreateAsync(Mergeable(5));
        var review = await OpenReviewAsync(harness.ViewModel);
        _actions.Failures.Enqueue(new PullRequestRefusedException(PullRequestRefusal.ReviewRequired));

        review.MergeCommand.Execute(null);
        var dialog = harness.ViewModel.StewardAction!;
        var first = (dialog.Title, dialog.Effect, dialog.HasText, dialog.SendText, review.CanMerge, review.MergeBlockedText);
        await dialog.SendCommand.ExecuteAsync(null);
        var asked = (dialog.IsReviewSkipAsked, dialog.SendText, dialog.IsDone, dialog.Error, dialog.CanSend);
        await dialog.SendCommand.ExecuteAsync(null);

        Assert.Equal(("Merge KSAModding/content-index #5", "Borea squash merges commit 0123456 into main. GitHub refuses the merge when the pull request got another commit since.", false, "Merge", true, null), first);
        Assert.Equal((true, "Merge without the review", false, null, true), asked);
        Assert.Equal([("Merge", false), ("Merge", true)], _actions.Sent.Select(sent => (sent.Action, sent.SkipRequiredReview)));
        Assert.Equal((true, "The pull request is merged."), (dialog.IsDone, dialog.DoneText));
    }

    [Theory]
    [InlineData(PullRequestRefusal.Changed, "The pull request has another head commit or another base branch than Borea showed. Borea reads it again, so look at it before you act.")]
    [InlineData(PullRequestRefusal.NotOpen, "The pull request is closed or merged now. Borea reads it again.")]
    [InlineData(PullRequestRefusal.Validate, "Merge is off until validate passed on the head commit.")]
    [InlineData(PullRequestRefusal.Verdict, "Merge is off until the checks left their verdict.")]
    public async Task Merge_PullRequestChangedSinceTheReview_SaysSo_ReadsItAgain_AndSendsNoMore(PullRequestRefusal refusal, string text)
    {
        using var harness = await CreateAsync(Mergeable(5));
        var review = await OpenReviewAsync(harness.ViewModel);
        _actions.Failures.Enqueue(new PullRequestRefusedException(refusal));

        review.MergeCommand.Execute(null);
        var dialog = harness.ViewModel.StewardAction!;
        await dialog.SendCommand.ExecuteAsync(null);
        await review.WhenLoadedAsync();
        await dialog.SendCommand.ExecuteAsync(null);

        Assert.Equal((text, true, false, false), (dialog.Error, dialog.IsStale, dialog.CanSend, dialog.IsDone));
        Assert.Single(_actions.Sent);
        Assert.Equal(2, _reviews.Reads.Count);
    }

    [Theory]
    [InlineData(StewardFailure.Refused, "Pull Request is not mergeable", "GitHub refused the change. Pull Request is not mergeable")]
    [InlineData(StewardFailure.Forbidden, null, "GitHub refused access, maybe because the Borea App is not installed on this repository.")]
    [InlineData(StewardFailure.SignedOut, null, "GitHub signed you out. Sign in again in Settings.")]
    public async Task ActionFails_ShowsWhy_AndCanBeSentAgain(StewardFailure failure, string? message, string text)
    {
        var pull = Mergeable(71, Releases);
        _queue.Items.Add(FakeStewardQueue.Item(71, repository: Releases));
        using var harness = await CreateAsync(pull);
        var review = await OpenReviewAsync(harness.ViewModel);
        _actions.Failures.Enqueue(new StewardException(failure, message));

        review.MergeCommand.Execute(null);
        var dialog = harness.ViewModel.StewardAction!;
        await dialog.SendCommand.ExecuteAsync(null);

        Assert.Equal((text, false, false, true), (dialog.Error, dialog.IsDone, dialog.IsStale, dialog.CanSend));
        Assert.Equal(Releases, Assert.Single(_actions.Sent).PullRequest.Repository);
    }

    [Fact]
    public async Task DoubleClick_SendsOnce_AndTheDialogStaysUntilGitHubAnswers()
    {
        using var harness = await CreateAsync(Mergeable(5));
        var review = await OpenReviewAsync(harness.ViewModel);
        _actions.Hold = new TaskCompletionSource();

        review.MergeCommand.Execute(null);
        var dialog = harness.ViewModel.StewardAction!;
        dialog.SendCommand.Execute(null);
        dialog.SendCommand.Execute(null);
        review.ApproveCommand.Execute(null);
        dialog.CloseCommand.Execute(null);
        var during = (dialog.IsSending, dialog.CanSend, dialog.CanClose, harness.ViewModel.StewardAction);
        _actions.Hold.SetResult();
        await dialog.WhenDoneAsync();

        Assert.Equal((true, false, false, dialog), during);
        Assert.Single(_actions.Sent);
        Assert.True(dialog.IsDone);
    }

    [Fact]
    public async Task OwnPullRequest_Warns_OnTheReviewAndInTheConfirmation()
    {
        using var harness = await CreateAsync(Mergeable(5) with { Author = "OctoCat" });
        var review = await OpenReviewAsync(harness.ViewModel);

        review.CommentCommand.Execute(null);

        Assert.Equal(harness.Localization.StewardActionOwnWarning, review.OwnWarning);
        Assert.Equal(harness.Localization.StewardActionOwnWarning, harness.ViewModel.StewardAction!.OwnWarning);
    }

    [Fact]
    public async Task OtherAuthor_HasNoWarning()
    {
        using var harness = await CreateAsync(Mergeable(5));
        var review = await OpenReviewAsync(harness.ViewModel);

        Assert.Null(review.OwnWarning);
    }

    [Theory]
    [InlineData(PullRequestState.Closed, "always")]
    [InlineData(PullRequestState.Merged, "always")]
    [InlineData(PullRequestState.Open, "never")]
    public async Task Actions_AreOff_ForAClosedPullRequest_OrWithoutTheBypassOfItsRepository(PullRequestState state, string bypass)
    {
        _session.Bypass[Releases] = bypass;
        _queue.Items.Add(FakeStewardQueue.Item(71, repository: Releases));
        using var harness = await CreateAsync(Mergeable(71, Releases) with { State = state });
        var review = await OpenReviewAsync(harness.ViewModel);

        review.ApproveCommand.Execute(null);
        review.MergeCommand.Execute(null);
        review.ClosePullRequestCommand.Execute(null);

        Assert.Equal((false, false, null), (review.CanAct, review.CanMerge, review.MergeBlockedText));
        Assert.False(harness.ViewModel.IsStewardActionOpen);
    }

    [Fact]
    public async Task SignOutAndSignIn_WhileAReviewIsOpen_HideAndShowTheActions()
    {
        using var harness = await CreateAsync(Mergeable(5) with { Author = "OctoCat" });
        var review = await OpenReviewAsync(harness.ViewModel);
        var changes = new List<(string? Name, bool CanAct)>();
        review.PropertyChanged += (_, e) =>
        {
            lock (changes)
                changes.Add((e.PropertyName, review.CanAct));
        };
        var before = (review.CanAct, review.CanMerge, review.OwnWarning is not null);

        _session.SignOut();
        await ViewModelHarness.WaitUntilAsync(() =>
        {
            lock (changes)
                return changes.Any(change => change.Name == nameof(StewardReview.OwnWarning));
        });
        review.MergeCommand.Execute(null);
        var signedOut = (review.CanAct, review.CanMerge, review.MergeBlockedText, review.OwnWarning, harness.ViewModel.IsStewardActionOpen);
        string?[] notified;
        lock (changes)
        {
            notified = changes.Select(change => change.Name).ToArray();
            changes.Clear();
        }

        // the steward role is checked again after the sign-in, and the actions show once it is known
        _session.SignInDirectly();
        await ViewModelHarness.WaitUntilAsync(() =>
        {
            lock (changes)
                return changes.Any(change => change.Name == nameof(StewardReview.CanAct) && change.CanAct);
        });

        Assert.Equal((true, true, true), before);
        Assert.Equal((false, false, null, null, false), signedOut);
        Assert.Superset(new HashSet<string?> { nameof(StewardReview.CanAct), nameof(StewardReview.CanMerge), nameof(StewardReview.MergeBlockedText), nameof(StewardReview.OwnWarning) }, notified.ToHashSet());
        Assert.Equal((true, true, harness.Localization.StewardActionOwnWarning), (review.CanAct, review.CanMerge, review.OwnWarning));
    }

    /// <summary>A pull request with a green validate on its head commit and the verdict of the checks.</summary>
    private static PullRequestReview Mergeable(int number, string repository = Index) =>
        FakePullRequestReviews.Review(number, repository) with { Verdict = "Validated, and ownership is not verified, so a steward decides." };

    private static async Task<StewardReview> OpenReviewAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);
        return viewModel.StewardPage.Review!;
    }

    private async Task<ViewModelHarness> CreateAsync(PullRequestReview pull)
    {
        if (_queue.Items.Count == 0)
            _queue.Items.Add(FakeStewardQueue.Item(pull.Number, repository: pull.Repository));

        _reviews.Reviews[(pull.Repository, pull.Number)] = pull;
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: new FakeIndexStatusEditor(), stewardQueue: _queue, pullRequestReviews: _reviews, pullRequestActions: _actions);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
