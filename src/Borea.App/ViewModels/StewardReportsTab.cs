using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Reports tab of the Steward page: the open takedown and id dispute reports of content-index with the sections of their forms.
/// A report sets a state with the status actions, and a steward answers, closes or reopens it on GitHub.
/// </summary>
public sealed partial class StewardReportsTab : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private Task _owners = Task.CompletedTask;
    private IReadOnlyList<IndexReport> _shown = [];
    private HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
    private string? _ownedLogin;
    private int _ownerLookup;

    public StewardReportsTab(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<StewardReport> Reports { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    public bool IsEmpty => IsLoaded && !IsLoading && Error is null && Reports.Count == 0;

    /// <summary>Waits for the read and for the owner lookup that follows it.</summary>
    internal Task WhenLoadedAsync() => _load;

    /// <summary>Waits for the owner lookup that a change of the account started.</summary>
    internal Task WhenOwnersCheckedAsync() => _owners;

    /// <summary>Reads the reports again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Builds the texts of the tab again in the language that is now selected.</summary>
    internal void RefreshText() => Show(_shown);

    /// <summary>
    /// Builds the rows again for the account that is now signed in, because each row says whether that account filed it,
    /// and asks again which ids the account owns when the account changed. A read that runs asks at its end.
    /// </summary>
    internal void RefreshRights()
    {
        Show(_shown);
        if (IsLoaded && !IsLoading && !string.Equals(_ownedLogin, OwnerLogin(), StringComparison.OrdinalIgnoreCase))
            _owners = CheckOwnersAsync();
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            // the index names the content page of each id
            var read = services.IndexReports.ListAsync();
            await Task.WhenAll(read, _owner.EnsureDiscoverLoadedAsync());
            Show(await read);
            IsLoaded = true;
        }
        catch (StewardException exception)
        {
            Show([]);
            Error = _owner.StewardIssuesFailureText(IndexReport.Repository, exception);
        }
        finally
        {
            IsLoading = false;
        }

        await (_owners = CheckOwnersAsync());
    }

    /// <summary>
    /// Asks which ids of the shown reports the signed-in steward owns, so a row warns before the steward answers it on GitHub.
    /// The confirmation of a status action checks the owner again, so a lookup that fails only leaves the warning out.
    /// </summary>
    private async Task CheckOwnersAsync()
    {
        var lookup = ++_ownerLookup;
        var login = OwnerLogin();
        if (!string.Equals(login, _ownedLogin, StringComparison.OrdinalIgnoreCase))
            _owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _ownedLogin = login;
        ShowOwners();

        var ids = Reports.Where(row => row.CanOpenContent).Select(row => row.Report.Id!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (login is null || ids.Count == 0 || _owner.Services is not { } services)
            return;

        IReadOnlyList<string> owned;
        try
        {
            owned = await services.IndexStatusEditor.OwnedAsync(ids);
        }
        catch (StewardException)
        {
            return;
        }

        if (lookup != _ownerLookup)
            return;

        _owned = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
        ShowOwners();
    }

    /// <summary>The login that the owner lookup answers for, which is null unless a steward of content-index is signed in.</summary>
    private string? OwnerLogin() => _owner.CanEditIndexStatus ? _owner.GitHubLogin : null;

    private bool IsOwned(IndexReport report) => report.Id is { } id && _owned.Contains(id);

    private void ShowOwners()
    {
        foreach (var row in Reports)
            row.IsOwner = IsOwned(row.Report);
    }

    private void Show(IReadOnlyList<IndexReport> reports)
    {
        _shown = reports;
        Reports.Clear();
        foreach (var report in reports)
            Reports.Add(new StewardReport(_owner, report) { IsOwner = IsOwned(report) });

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// One report on the Reports tab. Its actions open the confirmation of a status change that names the report, and
/// "Answer on GitHub" opens the issue, because Borea cannot write to issues.
/// </summary>
public sealed partial class StewardReport : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly DiscoverItem? _listing;
    private readonly PackItem? _pack;

    public StewardReport(MainViewModel owner, IndexReport report)
    {
        _owner = owner;
        Report = report;
        (_listing, _pack) = report.Id is { } id ? owner.FindIndexContent(id) : (null, null);
        Sections = SectionsOf(report);
        MissingTexts = report.Missing.Select(owner.Localization.FormatStewardReportMissing).ToList();
        IsReporter = owner.IsReporter(report);
    }

    public IndexReport Report { get; }

    public string NumberText => MainViewModel.RepositoryNumberText(IndexReport.Repository, Report.Number);

    public string KindText => Report.Kind == IndexReportKind.Takedown ? _owner.Localization.StewardReportTakedown : _owner.Localization.StewardReportDispute;

    public string ByText => _owner.Localization.FormatStewardReportBy(Report.Author ?? string.Empty, _owner.AgeText(Report.Created));

    /// <summary>The name of the listing or pack, which opens its content page.</summary>
    public string? ContentName => _listing?.Name ?? _pack?.Name;

    public bool CanOpenContent => ContentName is not null;

    /// <summary>The id is not in the index that Borea read, for example because it is delisted already, so only the id shows.</summary>
    public bool IsIdUnknown => Report.Id is not null && !CanOpenContent;

    public string? VersionText => Report.Version is { } version ? _owner.Localization.FormatStewardVersion(version) : null;

    /// <summary>Says that the listing id field holds a text that names no id, which then shows as it was typed.</summary>
    public string? UnreadableIdText => Report is { Id: null, ListingText: { } text } ? _owner.Localization.FormatStewardReportUnreadableId(text) : null;

    public bool HasId => Report.Id is not null;

    public bool HasVersion => Report.Version is not null;

    public IReadOnlyList<StewardReportSection> Sections { get; }

    /// <summary>One line for each section that the form requires and the body lacks, because somebody edited it by hand.</summary>
    public IReadOnlyList<string> MissingTexts { get; }

    public bool CanOpenForums => Report.ForumsUrl is not null;

    /// <summary>The signed-in steward filed the report, and POLICY.md asks a party to leave the case to another steward.</summary>
    public bool IsReporter { get; }

    /// <summary>The signed-in steward owns the listing or pack of the report, which the tab looks up after the read.</summary>
    [ObservableProperty]
    private bool _isOwner;

    [RelayCommand]
    private void Answer() => SetError(_owner.TryOpenWithSystem(Report.Url.AbsoluteUri));

    [RelayCommand]
    private void OpenForums()
    {
        if (Report.ForumsUrl is { } url)
            SetError(_owner.TryOpenWithSystem(url.AbsoluteUri));
    }

    /// <summary>Looks the row up again, because an index check since the read builds new rows.</summary>
    [RelayCommand]
    private Task OpenContentAsync()
    {
        var (listing, pack) = _owner.FindIndexContent(Report.Id ?? string.Empty);
        return (listing ?? _listing) is { } row ? _owner.OpenContentAsync(row)
            : (pack ?? _pack) is { } found ? _owner.OpenPackAsync(found)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private void Dispute()
    {
        if (Report.Id is { } id)
            _owner.BeginIndexStatusChange(IndexStatusChange.Dispute(id, string.Empty), Report);
    }

    [RelayCommand]
    private void Delist()
    {
        if (Report.Id is { } id)
            _owner.BeginIndexStatusChange(IndexStatusChange.Delist(id, string.Empty), Report);
    }

    [RelayCommand]
    private void Retract()
    {
        if (Report is { Id: { } id, Version: { } version })
            _owner.BeginIndexStatusChange(IndexStatusChange.Retract(id, version, string.Empty), Report);
    }

    private void SetError(string? error) => _owner.StewardPage.Reports.Error = error ?? _owner.StewardPage.Reports.Error;

    private List<StewardReportSection> SectionsOf(IndexReport report)
    {
        var text = _owner.Localization;
        (string Label, string? Text)[] sections = report.Kind == IndexReportKind.Takedown
            ? [(text.StewardReportGround, report.Ground), (text.StewardReportWhatIsWrong, report.WhatIsWrong), (text.StewardReportReporter, report.Reporter)]
            : [(text.StewardReportDisputed, report.Disputed), (text.StewardReportForums, report.ForumsThread), (text.StewardReportClaim, report.Claim), (text.StewardReportOtherParty, report.OtherParty)];
        var shown = new List<StewardReportSection>();
        foreach (var (label, value) in sections)
        {
            if (value is not null)
                shown.Add(new StewardReportSection(label, value));
        }

        return shown;
    }
}

/// <param name="Label">The name of the section in the language that Borea shows.</param>
/// <param name="Text">The answer as the reporter wrote it.</param>
public sealed record StewardReportSection(string Label, string Text);
