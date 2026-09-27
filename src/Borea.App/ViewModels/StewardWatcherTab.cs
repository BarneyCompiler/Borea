using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Watcher tab of the Steward page: the listing issues of the watcher and the watchdog issue. The watcher ticks by itself,
/// so the tab has no tick action, and a backfill runs from the workflow page on GitHub.
/// </summary>
public sealed partial class StewardWatcherTab : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private WatcherIssues _shown = new([], [], []);

    public StewardWatcherTab(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<StewardWatcherIssue> Listings { get; } = [];

    public ObservableCollection<StewardWatcherIssue> Watchdog { get; } = [];

    /// <summary>One line for each repository that could not be read, while the other one still shows.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTicking))]
    [NotifyPropertyChangedFor(nameof(IsListingsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTicking))]
    [NotifyPropertyChangedFor(nameof(IsListingsEmpty))]
    private bool _isLoaded;

    /// <summary>content-index-releases was read and has no watchdog issue.</summary>
    public bool IsTicking => IsShown(WatcherIssues.WatchdogRepository) && Watchdog.Count == 0;

    /// <summary>content-index was read and has no listing issue.</summary>
    public bool IsListingsEmpty => IsShown(WatcherIssues.ListingsRepository) && Listings.Count == 0;

    public Uri WorkflowUrl => WatcherIssues.WorkflowUrl;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Reads both repositories again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Builds the texts of the tab again in the language that is now selected.</summary>
    internal void RefreshText() => Show(_shown);

    [RelayCommand]
    private void OpenWorkflow() => Error = _owner.TryOpenWithSystem(WorkflowUrl.AbsoluteUri) ?? Error;

    private bool IsShown(string repository) => IsLoaded && !IsLoading && _shown.Failures.All(failure => failure.Repository != repository);

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            // the index names the content page of each listing
            var read = services.WatcherIssues.ListAsync();
            await Task.WhenAll(read, _owner.EnsureDiscoverLoadedAsync());
            Show(await read);
            IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(WatcherIssues issues)
    {
        _shown = issues;
        Listings.Clear();
        foreach (var issue in issues.Listings)
            Listings.Add(new StewardWatcherIssue(_owner, issue));

        Watchdog.Clear();
        foreach (var issue in issues.Watchdog)
            Watchdog.Add(new StewardWatcherIssue(_owner, issue));

        Failures.Clear();
        foreach (var failure in issues.Failures)
            Failures.Add(_owner.StewardWatcherFailureText(failure));

        OnPropertyChanged(nameof(IsTicking));
        OnPropertyChanged(nameof(IsListingsEmpty));
    }
}

/// <summary>One issue of the watcher or the watchdog. It opens on GitHub, and a listing issue also opens the content page of its listing.</summary>
public sealed partial class StewardWatcherIssue(MainViewModel owner, WatcherIssue issue)
{
    private readonly DiscoverItem? _listing = issue.ListingId is { } id ? owner.IndexListing(id) : null;

    public WatcherIssue Issue { get; } = issue;

    public string NumberText => MainViewModel.RepositoryNumberText(Issue.Repository, Issue.Number);

    public string Title => Issue.Title;

    public string UpdatedText => owner.Localization.FormatStewardWatcherUpdated(owner.AgeText(Issue.Updated));

    /// <summary>The name of the listing, which opens its content page.</summary>
    public string? ListingName => _listing?.Name;

    public bool CanOpenListing => _listing is not null;

    /// <summary>The marker names a listing that Borea does not list, for example a delisted one, so only its id shows.</summary>
    public bool IsListingUnknown => Issue.ListingId is not null && _listing is null;

    public bool HasNoListing => Issue.ListingId is null;

    [RelayCommand]
    private void Open() => owner.StewardPage.Watcher.Error = owner.TryOpenWithSystem(Issue.Url.AbsoluteUri) ?? owner.StewardPage.Watcher.Error;

    /// <summary>Looks the row up again, because an index check since the read builds new rows.</summary>
    [RelayCommand]
    private Task OpenListingAsync() =>
        _listing is null ? Task.CompletedTask : owner.OpenContentAsync(owner.IndexListing(_listing.ModId) ?? _listing);
}
