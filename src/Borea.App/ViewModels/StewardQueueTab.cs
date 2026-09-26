using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The Queue tab of the Steward page: the open pull requests of both index repositories that wait for a steward, oldest first.</summary>
public sealed partial class StewardQueueTab : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private StewardQueue _shown = new([], []);
    private bool _shownAllOpen;

    public StewardQueueTab(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<StewardQueueEntry> Items { get; } = [];

    /// <summary>One line for each repository that could not be read, while the other one still shows.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(Count))]
    private bool _isLoaded;

    /// <summary>Lists every open pull request instead of only the ones that wait for a steward.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _showAllOpen;

    /// <summary>Which pull requests show by what they change. It filters what was read, so a change reads nothing again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeText))]
    private StewardQueueScope _scope = StewardQueueScope.Content;

    public string ScopeText => Scope switch
    {
        StewardQueueScope.Content => _owner.Localization.StewardQueueScopeContent,
        StewardQueueScope.Other => _owner.Localization.StewardQueueScopeOther,
        _ => _owner.Localization.StewardQueueScopeAll,
    };

    public bool IsEmpty => IsLoaded && !IsLoading && Error is null && Items.Count == 0 && Failures.Count == 0;

    public string EmptyText => ShowAllOpen ? _owner.Localization.StewardQueueEmptyAllOpen : _owner.Localization.StewardQueueEmpty;

    /// <summary>
    /// How many pull requests of the selected scope wait for a steward, also while all open ones show. It is null before the first
    /// read and when no repository could be read, because the number is not known then.
    /// </summary>
    public int? Count => IsLoaded && _shown.Failures.Count < StewardQueue.Repositories.Count ? _shown.Items.Count(item => item.NeedsSteward && item.IsIn(Scope)) : null;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Reads both repositories again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    partial void OnShowAllOpenChanged(bool value) => _ = RefreshAsync();

    partial void OnScopeChanged(StewardQueueScope value)
    {
        Show(_shown, _shownAllOpen);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Count));
        _owner.StewardPage.OnQueueCountChanged();
    }

    [RelayCommand]
    private void SelectScope(StewardQueueScope scope) => Scope = scope;

    /// <summary>Builds the texts of the queue again in the language that is now selected.</summary>
    internal void RefreshText()
    {
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ScopeText));
        Show(_shown, _shownAllOpen);
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            // a filter that changes during a read joins it, so the read repeats until it matches the filter
            StewardQueue queue;
            StewardQueueFilter filter;
            do
            {
                filter = Filter;
                queue = await services.StewardQueue.ListAsync(filter);
            }
            while (filter != Filter);

            Show(queue, filter == StewardQueueFilter.AllOpen);
            IsLoaded = true;
        }
        catch (StewardException exception)
        {
            Show(new StewardQueue([], []), allOpen: false);
            IsLoaded = false;
            Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(Count));
            _owner.StewardPage.OnQueueCountChanged();
        }
    }

    private StewardQueueFilter Filter => ShowAllOpen ? StewardQueueFilter.AllOpen : StewardQueueFilter.NeedsSteward;

    private void Show(StewardQueue queue, bool allOpen)
    {
        _shown = queue;
        _shownAllOpen = allOpen;
        Items.Clear();
        foreach (var item in queue.Items.Where(item => item.IsIn(Scope)))
            Items.Add(new StewardQueueEntry(_owner, item, allOpen));

        Failures.Clear();
        foreach (var failure in queue.Failures)
            Failures.Add(_owner.StewardQueueFailureText(failure));
    }
}

/// <summary>One pull request of the queue. It opens on GitHub, where the steward reviews it.</summary>
/// <param name="allOpen">The queue lists every open pull request, so the entry says whether it waits for a steward.</param>
public sealed partial class StewardQueueEntry(MainViewModel owner, StewardQueueItem item, bool allOpen)
{
    public StewardQueueItem Item { get; } = item;

    public bool ShowsWaiting => allOpen && Item.NeedsSteward;

    public string NumberText => MainViewModel.RepositoryNumberText(Item.Repository, Item.Number);

    public string Title => Item.Title;

    public string ByText => owner.Localization.FormatStewardQueueBy(Item.Author ?? string.Empty, owner.AgeText(Item.Opened));

    public IReadOnlyList<string> KindTexts => Item.Kinds.Select(owner.StewardQueueKindText).ToList();

    public string VerdictText => Item.Verdict ?? owner.Localization.StewardQueueNoVerdict;

    public bool HasVerdict => Item.Verdict is not null;

    [RelayCommand]
    private void Open() => owner.StewardPage.Queue.Error = owner.TryOpenWithSystem(Item.Url.AbsoluteUri) ?? owner.StewardPage.Queue.Error;
}
