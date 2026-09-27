using System;
using System.Globalization;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Steward page and the steward actions of the content and pack pages. They show only to a steward of content-index,
/// which only decides what Borea shows, because GitHub enforces the rights.
/// </summary>
public partial class MainViewModel
{
    private StewardPage? _stewardPage;

    [ObservableProperty]
    private bool _currentWindowSteward;

    /// <summary>The open confirmation of a change of index-status.toml, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStewardChangeOpen))]
    private IndexStatusDialog? _stewardChange;

    public StewardPage StewardPage => _stewardPage ??= new StewardPage(this);

    public bool IsStewardChangeOpen => StewardChange is not null;

    /// <summary>Editing index-status.toml needs the bypass of content-index itself.</summary>
    public bool CanEditIndexStatus => IsGitHubSignedIn && _services?.StewardRole.Current is { ContentIndex: true };

    /// <summary>A listing of the content index, as opposed to one that only SpaceDock knows.</summary>
    public bool CanEditContentStatus => CanEditIndexStatus && SelectedContent?.Source == "index";

    private void RefreshIndexStatusRights()
    {
        OnPropertyChanged(nameof(CanEditIndexStatus));
        OnPropertyChanged(nameof(CanEditContentStatus));
    }

    [RelayCommand]
    private void OpenStewardPage()
    {
        if (!IsGitHubSteward)
            return;

        IsSettingsOpen = false;
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        IsTasksOpen = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
        CurrentWindowSteward = true;
        _ = StewardPage.RefreshAsync();
    }

    [RelayCommand]
    private void DisputeContent()
    {
        if (CanEditContentStatus && SelectedContent is { } item)
            BeginIndexStatusChange(IndexStatusChange.Dispute(item.ModId, string.Empty));
    }

    [RelayCommand]
    private void DelistContent()
    {
        if (CanEditContentStatus && SelectedContent is { } item)
            BeginIndexStatusChange(IndexStatusChange.Delist(item.ModId, string.Empty));
    }

    [RelayCommand]
    private void DisputePack()
    {
        if (CanEditIndexStatus && SelectedPack is { } pack)
            BeginIndexStatusChange(IndexStatusChange.Dispute(pack.PackId, string.Empty));
    }

    [RelayCommand]
    private void DelistPack()
    {
        if (CanEditIndexStatus && SelectedPack is { } pack)
            BeginIndexStatusChange(IndexStatusChange.Delist(pack.PackId, string.Empty));
    }

    /// <summary>Retracts the pack version the page shows.</summary>
    [RelayCommand]
    private void RetractPackVersion()
    {
        if (CanEditIndexStatus && SelectedPack is { } pack)
            BeginIndexStatusChange(IndexStatusChange.Retract(pack.PackId, pack.Version, string.Empty));
    }

    /// <summary>Opens the confirmation and checks the change against the base branch. Another open confirmation stays until it is closed.</summary>
    internal void BeginIndexStatusChange(IndexStatusChange change)
    {
        if (StewardChange is not null || !CanEditIndexStatus)
            return;

        var dialog = new IndexStatusDialog(this, change);
        StewardChange = dialog;
        dialog.Start();
    }

    internal void CloseIndexStatusChange(IndexStatusDialog dialog)
    {
        if (ReferenceEquals(StewardChange, dialog))
            StewardChange = null;
    }

    /// <summary>The Status tab shows the new pull request, and its list conflicts once one of them merges.</summary>
    internal void OnIndexStatusPullRequestOpened()
    {
        if (StewardPage.IsLoaded || CurrentWindowSteward)
            _ = StewardPage.RefreshAsync();
    }

    private void LeaveStewardPage() => CurrentWindowSteward = false;

    internal string IndexStatusStateText(string state) => state switch
    {
        IndexStatusEntry.Delisted => Localization.StewardStateDelisted,
        IndexStatusEntry.Disputed => Localization.StewardStateDisputed,
        IndexStatusEntry.Retracted => Localization.StewardStateRetracted,
        _ => state,
    };

    internal string IndexStatusRefusalText(IndexStatusRefusal refusal, string id, string? version) => refusal switch
    {
        IndexStatusRefusal.InvalidReason => Localization.StewardRefusedInvalidReason,
        IndexStatusRefusal.MissingVersion => Localization.StewardRefusedMissingVersion,
        IndexStatusRefusal.Duplicate => Localization.FormatStewardRefusedDuplicate(id),
        IndexStatusRefusal.UnknownId => Localization.FormatStewardRefusedUnknownId(id),
        IndexStatusRefusal.NotAPack => Localization.FormatStewardRefusedNotAPack(id),
        IndexStatusRefusal.UnknownVersion => Localization.FormatStewardRefusedUnknownVersion(id, version ?? string.Empty),
        _ => Localization.StewardRefusedNotInFile,
    };

    internal string StewardErrorText(StewardException exception) => exception.Failure switch
    {
        StewardFailure.SignedOut => Localization.StewardErrorSignedOut,
        StewardFailure.NotSteward => Localization.StewardErrorNotSteward,
        StewardFailure.RateLimited => Localization.FormatListingErrorRateLimit(
            (exception.RetryAt ?? DateTimeOffset.Now).ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
        StewardFailure.NotFound => Localization.StewardErrorNotFound,
        StewardFailure.Refused => Localization.FormatStewardErrorRefused(exception.Detail ?? string.Empty).TrimEnd(),
        StewardFailure.Forbidden => Localization.StewardErrorForbidden,
        StewardFailure.NetworkError => Localization.StewardErrorNetwork,
        StewardFailure.UnreadableFile => Localization.FormatStewardErrorUnreadable(exception.Detail ?? string.Empty).TrimEnd(),
        _ => Localization.StewardErrorUnexpected,
    };
}
