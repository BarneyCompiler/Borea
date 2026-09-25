using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// A pack version: its own version, and the members it pins. Members come only from the snapshot, so a new pin
/// always names a listed mod at a release that no client refuses to install.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ContentIndexListing> _memberCandidates = [];

    [ObservableProperty]
    private string _packVersion = string.Empty;

    [ObservableProperty]
    private string _releasedAt = string.Empty;

    [ObservableProperty]
    private string _changelog = string.Empty;

    public ObservableCollection<ListingPackMemberRow> Members { get; } = [];

    [ObservableProperty]
    private string _memberQuery = string.Empty;

    /// <summary>The listed mods that are not pinned yet and whose id, name or authors contain the query.</summary>
    public ObservableCollection<ListedListing> MemberMatches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    private ListedListing? _selectedMemberMatch;

    public bool HasNoMemberMatch => MemberMatches.Count == 0 && _memberCandidates.Count > 0;

    /// <summary>The game_min that the pinned releases need, while the form has a lower or no game_min.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameMinProposalText))]
    private string? _gameMinProposal;

    public string? GameMinProposalText => GameMinProposal is { } gameMin ? Localization.FormatListingGameMinProposal(gameMin) : null;

    [RelayCommand]
    private void StartPack()
    {
        _source = null;
        _archive = null;
        ArchiveText = null;
        Load(new ListingDraft
        {
            Type = ListingDraft.ModPackType,
            Version = "1.0.0",
            ReleasedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Pins the chosen mod at its newest stable release, or at its newest release when none is stable.</summary>
    [RelayCommand(CanExecute = nameof(CanAddMember))]
    private void AddMember()
    {
        if (SelectedMemberMatch?.Id is not { } id || _snapshot is not { } snapshot || ListingPackMembers.Listing(snapshot, id) is not { } listing)
            return;

        var offered = ListingPackMembers.Offered(listing);
        if ((offered.FirstOrDefault(release => release.ReleaseStatus == ReleaseStatus.Stable) ?? offered.FirstOrDefault()) is not { } release)
            return;

        Members.Add(MemberRow(new ListingPackMember(listing.Id, release.Version.ToString())));
        FindMembers();
        Refresh();
    }

    private bool CanAddMember() => SelectedMemberMatch is not null;

    [RelayCommand]
    private void UseGameMinProposal()
    {
        if (GameMinProposal is { } gameMin)
            GameMin = gameMin;
    }

    internal void Remove(ListingPackMemberRow row)
    {
        if (!Members.Remove(row))
            return;

        FindMembers();
        Refresh();
    }

    /// <summary>Why no client can install the pin, or null when it can, or when there is no snapshot to tell.</summary>
    internal string? MemberNote(ListingPackMember member)
    {
        if (_snapshot is not { } snapshot)
            return null;
        if (ListingPackMembers.Listing(snapshot, member.Id) is null)
            return Localization.FormatListingMemberNotListed(member.Id);

        return ListingPackMembers.Release(snapshot, member) is null ? Localization.FormatListingMemberNotOffered(member.Id, member.Version) : null;
    }

    /// <summary>A note for each pin no client can install, and the game_min the pins need when the form has a lower one.</summary>
    private List<ListingIssue> PackIssues(IReadOnlyList<ListingPackMember> mods)
    {
        var issues = new List<ListingIssue>();
        for (var index = 0; index < mods.Count; index++)
        {
            if (MemberNote(mods[index]) is { } note)
                issues.Add(new ListingIssue(ListingIssueSeverity.Note, $"mods[{index}]", note));
        }

        var needed = _snapshot is { } snapshot ? ListingPackMembers.HighestGameMin(snapshot, mods) : null;
        GameMinProposal = needed is not null && !(GameVersion.TryParse(GameMin.Trim(), out var gameMin) && gameMin.Revision >= needed.GameMinRevision)
            ? needed.GameMin
            : null;
        if (GameMinProposalText is { } proposal)
            issues.Add(new ListingIssue(ListingIssueSeverity.Note, "compatibility", proposal));
        return issues;
    }

    /// <summary>Builds the member rows again, for a new draft, a new snapshot or a new display language.</summary>
    private void FillMembers(IReadOnlyList<ListingPackMember> members)
    {
        Members.Clear();
        foreach (var member in members)
            Members.Add(MemberRow(member));

        _memberCandidates = _snapshot is { } snapshot ? ListingPackMembers.Candidates(snapshot) : [];
        FindMembers();
    }

    /// <summary>A row whose release list holds the releases a pin can name, and first the pinned one when it is not among them.</summary>
    private ListingPackMemberRow MemberRow(ListingPackMember member)
    {
        var listing = _snapshot is { } snapshot ? ListingPackMembers.Listing(snapshot, member.Id) : null;
        var releases = (listing is null ? [] : ListingPackMembers.Offered(listing))
            .Select(release => new ListingReleaseChoice(release.Version.ToString(), _owner.ReleaseStatusText(release.ReleaseStatus)))
            .ToList();
        if (!releases.Any(release => release.Version == member.Version))
            releases.Insert(0, new ListingReleaseChoice(member.Version, string.Empty));

        return new ListingPackMemberRow(this, member, listing?.Authored?.Name ?? member.Id, releases);
    }

    private void FindMembers()
    {
        var query = MemberQuery.Trim();
        var matches = _memberCandidates
            .Where(listing => !Members.Any(row => ModIds.Equals(row.Id, listing.Id)) && Matches(listing, query))
            .Select(listing => new ListedListing(listing.Id, listing.Authored!.Name, AuthorsText(listing.Authored), IsOwn: false))
            .OrderBy(listing => listing.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MainViewModel.Arrange(MemberMatches, matches);
        SelectedMemberMatch = matches.FirstOrDefault(listing => listing.Id == SelectedMemberMatch?.Id) ?? (query.Length > 0 ? matches.FirstOrDefault() : null);
        OnPropertyChanged(nameof(HasNoMemberMatch));
    }

    partial void OnMemberQueryChanged(string value) => FindMembers();

    partial void OnPackVersionChanged(string value) => Refresh();

    partial void OnReleasedAtChanged(string value) => Refresh();

    partial void OnChangelogChanged(string value) => Refresh();
}
