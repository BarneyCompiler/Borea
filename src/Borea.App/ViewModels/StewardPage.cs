using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Steward page, opened from the GitHub account in Settings. Its Status tab lists the states of index-status.toml on the base branch
/// with Lift on each, which is the only way back for a delisted listing, because it has no content page.
/// </summary>
public sealed partial class StewardPage : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;

    public StewardPage(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<StewardStatusEntry> StatusEntries { get; } = [];

    /// <summary>The open pull requests that change index-status.toml, and whether GitHub still merges each one.</summary>
    public ObservableCollection<StewardPullRequest> StatusPullRequests { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusEmpty))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusEmpty))]
    private bool _isLoaded;

    public bool IsStatusEmpty => IsLoaded && !IsLoading && Error is null && StatusEntries.Count == 0;

    public bool HasStatusPullRequests => StatusPullRequests.Count > 0;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Reads the file again. A second call during a read joins it.</summary>
    [RelayCommand]
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            var overview = await services.IndexStatusEditor.ReadAsync();
            StatusEntries.Clear();
            foreach (var entry in overview.Entries.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
                StatusEntries.Add(new StewardStatusEntry(_owner, entry));

            StatusPullRequests.Clear();
            foreach (var pull in overview.OpenPullRequests)
                StatusPullRequests.Add(new StewardPullRequest(_owner, pull));

            IsLoaded = true;
        }
        catch (StewardException exception)
        {
            StatusEntries.Clear();
            StatusPullRequests.Clear();
            Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasStatusPullRequests));
        }
    }
}

/// <summary>One entry of index-status.toml on the Status tab.</summary>
public sealed partial class StewardStatusEntry(MainViewModel owner, IndexStatusEntry entry)
{
    public IndexStatusEntry Entry { get; } = entry;

    public string Id => Entry.Id;

    public string StateText => owner.IndexStatusStateText(Entry.State);

    public string? VersionText => Entry.Version is { } version ? owner.Localization.FormatStewardVersion(version) : null;

    public string? SinceText =>
        DateTimeOffset.TryParse(Entry.Since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since)
            ? owner.Localization.FormatStewardSince(MainViewModel.DateText(since))
            : null;

    public string? Reason => Entry.Reason;

    [RelayCommand]
    private void Lift() => owner.BeginIndexStatusChange(IndexStatusChange.Lift(Entry, string.Empty));
}

/// <summary>An open pull request that changes index-status.toml.</summary>
public sealed partial class StewardPullRequest(MainViewModel owner, IndexStatusPullRequest pullRequest)
{
    public IndexStatusPullRequest PullRequest { get; } = pullRequest;

    public string Text => owner.Localization.FormatStewardPullRequestBy(
        PullRequest.Number.ToString(CultureInfo.InvariantCulture),
        PullRequest.Title,
        PullRequest.Author ?? string.Empty);

    public bool Conflicts => PullRequest.Conflicts == true;

    [RelayCommand]
    private void Open() => owner.StewardPage.Error = owner.TryOpenWithSystem(PullRequest.Url.AbsoluteUri) ?? owner.StewardPage.Error;
}
