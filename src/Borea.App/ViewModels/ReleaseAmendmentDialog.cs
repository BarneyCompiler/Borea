using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum ReleaseAmendmentScope
{
    Checked,
    UpTo,
    All,
}

/// <summary>
/// The steward amendment of the releases of one listing: the releases, the change and the reason. It shows the change per release file
/// before anything is written, and opens the pull request of the preview once. Any edit drops the preview, so what is sent is what was shown.
/// </summary>
public sealed partial class ReleaseAmendmentDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _run = Task.CompletedTask;

    public ReleaseAmendmentDialog(MainViewModel owner, string listingId)
    {
        _owner = owner;
        ListingId = listingId;
    }

    public string ListingId { get; }

    public LocalizationService Localization => _owner.Localization;

    public string Title => _owner.Localization.FormatStewardAmendTitle(ListingId);

    /// <summary>The stamped releases on the base branch, newest first.</summary>
    public ObservableCollection<ReleaseAmendmentVersion> Versions { get; } = [];

    public ObservableCollection<ReleaseAmendmentDependencyRow> Dependencies { get; } = [];

    /// <summary>The changed files of the preview, and the ones that already say this.</summary>
    public ObservableCollection<ReleaseAmendmentFile> Files { get; } = [];

    public IReadOnlyList<string> VersionNames => [.. Versions.Select(version => version.Version)];

    public bool HasVersions => Versions.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScopeChecked))]
    [NotifyPropertyChangedFor(nameof(IsScopeUpTo))]
    [NotifyPropertyChangedFor(nameof(IsScopeAll))]
    private ReleaseAmendmentScope _scope;

    [ObservableProperty]
    private string? _upTo;

    [ObservableProperty]
    private string _gameMin = string.Empty;

    [ObservableProperty]
    private string _gameMax = string.Empty;

    [ObservableProperty]
    private string _loaderMin = string.Empty;

    [ObservableProperty]
    private string _loaderMax = string.Empty;

    [ObservableProperty]
    private bool _yank;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InvalidReasonText))]
    private string _reason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isPreviewing;

    /// <summary>While the pull request is being opened, the dialog cannot close, so the steward sees how it ended.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(NothingText))]
    [NotifyPropertyChangedFor(nameof(MentionText))]
    private ReleaseAmendmentPreview? _preview;

    /// <summary>Why tools/amend.py or the checks would refuse the amendment.</summary>
    [ObservableProperty]
    private string? _refusal;

    /// <summary>The words of the checks behind <see cref="Refusal"/>, in English as the checks write them.</summary>
    [ObservableProperty]
    private string? _refusalDetails;

    /// <summary>Says that the files changed on the base branch since the preview, which now shows them.</summary>
    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpened))]
    [NotifyPropertyChangedFor(nameof(OpenedText))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private ReleaseAmendmentPullRequest? _opened;

    public bool IsScopeChecked
    {
        get => Scope == ReleaseAmendmentScope.Checked;
        set => SetScope(value, ReleaseAmendmentScope.Checked);
    }

    public bool IsScopeUpTo
    {
        get => Scope == ReleaseAmendmentScope.UpTo;
        set => SetScope(value, ReleaseAmendmentScope.UpTo);
    }

    public bool IsScopeAll
    {
        get => Scope == ReleaseAmendmentScope.All;
        set => SetScope(value, ReleaseAmendmentScope.All);
    }

    public bool HasPreview => Preview is not null;

    public bool CanEdit => !IsPreviewing && !IsOpening && Opened is null;

    /// <summary>A preview needs the releases, a selection, a change and a reason that fits on one line.</summary>
    public bool CanPreview => CanEdit && !IsLoading && Versions.Count > 0 && Preview is null && HasSelection && HasChange && IndexStatusChange.IsValidReason(Reason);

    public bool CanOpen => CanEdit && Preview is { Changed.Count: > 0 };

    public bool CanClose => !IsOpening;

    /// <summary>Reading the releases failed and can run again.</summary>
    public bool CanRetry => Error is not null && Versions.Count == 0 && !IsLoading;

    public bool IsOpened => Opened is not null;

    public string? InvalidReasonText =>
        Reason.Trim().Length > 0 && !IndexStatusChange.IsValidReason(Reason) ? _owner.Localization.StewardRefusedInvalidReason : null;

    public string? NothingText => Preview is { Changed.Count: 0 } ? _owner.Localization.StewardAmendNothing : null;

    public string? MentionText => Preview is { Owners.Count: > 0 } preview
        ? _owner.Localization.FormatStewardMention(string.Join(", ", preview.Owners.Select(login => "@" + login)))
        : null;

    public string? OpenedText => Opened is { } pull ? _owner.Localization.FormatStewardOpened(pull.Number.ToString(CultureInfo.InvariantCulture)) : null;

    internal ReleaseAmendmentRequest Request => new(ListingId, Selection, Change, Reason);

    private bool HasSelection => Scope switch
    {
        ReleaseAmendmentScope.Checked => Versions.Any(version => version.IsSelected),
        ReleaseAmendmentScope.UpTo => UpTo is not null,
        _ => true,
    };

    private bool HasChange =>
        Yank || Dependencies.Count > 0 || new[] { GameMin, GameMax, LoaderMin, LoaderMax }.Any(value => !string.IsNullOrWhiteSpace(value));

    private ReleaseSelection Selection => Scope switch
    {
        ReleaseAmendmentScope.UpTo => ReleaseSelection.UpTo(UpTo ?? string.Empty),
        ReleaseAmendmentScope.All => ReleaseSelection.All,
        _ => ReleaseSelection.Of(Versions.Where(version => version.IsSelected).Select(version => version.Version)),
    };

    private ReleaseChange Change => new()
    {
        GameMin = Typed(GameMin),
        GameMax = Typed(GameMax),
        Yank = Yank,
        LoaderMin = Typed(LoaderMin),
        LoaderMax = Typed(LoaderMax),
        AddedDependencies = [.. Dependencies.Where(row => row.IsMissing).Select(row => new ReleaseDependencyAddition(row.Id, row.Kind))],
        DependencyBounds = [.. Dependencies.Where(row => Typed(row.Min) is not null || Typed(row.Max) is not null).Select(row => new ReleaseDependencyBounds(row.Id, Typed(row.Min), Typed(row.Max)))],
    };

    internal Task WhenDoneAsync() => _run;

    internal void Start() => _run = LoadAsync();

    /// <summary>Drops the preview after any edit, because it no longer shows what would be sent.</summary>
    internal void Edited()
    {
        Preview = null;
        Files.Clear();
        Refusal = null;
        RefusalDetails = null;
        Notice = null;
        RefreshCommands();
    }

    partial void OnScopeChanged(ReleaseAmendmentScope value) => Edited();

    partial void OnUpToChanged(string? value) => Edited();

    partial void OnGameMinChanged(string value) => Edited();

    partial void OnGameMaxChanged(string value) => Edited();

    partial void OnLoaderMinChanged(string value) => Edited();

    partial void OnLoaderMaxChanged(string value) => Edited();

    partial void OnYankChanged(bool value) => Edited();

    partial void OnReasonChanged(string value) => Edited();

    partial void OnIsLoadingChanged(bool value) => RefreshCommands();

    partial void OnIsPreviewingChanged(bool value) => RefreshCommands();

    partial void OnIsOpeningChanged(bool value) => RefreshCommands();

    partial void OnOpenedChanged(ReleaseAmendmentPullRequest? value) => RefreshCommands();

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanOpen));
        PreviewCommand.NotifyCanExecuteChanged();
        OpenPullRequestCommand.NotifyCanExecuteChanged();
    }

    private void SetScope(bool selected, ReleaseAmendmentScope scope)
    {
        if (selected)
            Scope = scope;
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            foreach (var version in await services.ReleaseAmendments.ReleasesAsync(ListingId))
                Versions.Add(new ReleaseAmendmentVersion(this, version));
            OnPropertyChanged(nameof(VersionNames));
            OnPropertyChanged(nameof(HasVersions));
        }
        catch (ReleaseAmendmentRefusedException exception)
        {
            // A refusal stays the same when the same files are read again, so it sets no error and offers no retry.
            Refuse(exception);
            if (exception.Refusal == ReleaseAmendmentRefusal.UnknownRelease)
                Refusal = _owner.Localization.StewardAmendNoReleases;
        }
        catch (StewardException exception)
        {
            Error = _owner.ReleaseAmendmentErrorText(exception);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => CanRetry ? _run = LoadAsync() : _run;

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task PreviewAsync()
    {
        // a second click while the first one runs sends nothing
        if (!CanPreview)
            return _run;

        return _run = RunPreviewAsync();
    }

    private async Task RunPreviewAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsPreviewing = true;
        Error = null;
        try
        {
            Show(await services.ReleaseAmendments.PreviewAsync(Request));
        }
        catch (ReleaseAmendmentRefusedException exception)
        {
            Refuse(exception);
        }
        catch (StewardException exception)
        {
            Error = _owner.ReleaseAmendmentErrorText(exception);
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenPullRequestAsync()
    {
        if (!CanOpen)
            return _run;

        return _run = OpenAsync(Preview!);
    }

    private async Task OpenAsync(ReleaseAmendmentPreview preview)
    {
        if (_owner.Services is not { } services)
            return;

        IsOpening = true;
        Error = null;
        try
        {
            Opened = await services.ReleaseAmendments.OpenAsync(preview);
            _owner.OnReleaseAmendmentOpened();
        }
        catch (ReleaseAmendmentChangedException exception)
        {
            Show(exception.Current);
            Notice = _owner.Localization.StewardAmendChanged;
        }
        catch (ReleaseAmendmentRefusedException exception)
        {
            Edited();
            Refuse(exception);
        }
        catch (StewardException exception)
        {
            Error = _owner.ReleaseAmendmentErrorText(exception);
        }
        finally
        {
            IsOpening = false;
        }
    }

    private void Show(ReleaseAmendmentPreview preview)
    {
        Files.Clear();
        foreach (var file in preview.Files)
            Files.Add(new ReleaseAmendmentFile(_owner, file));
        Preview = preview;
        RefreshCommands();
    }

    private void Refuse(ReleaseAmendmentRefusedException exception)
    {
        Refusal = _owner.ReleaseAmendmentRefusalText(exception.Refusal);
        RefusalDetails = string.Join(" ", exception.Details);
    }

    [RelayCommand]
    private void AddMissingDependency()
    {
        Dependencies.Add(new ReleaseAmendmentDependencyRow(this, isMissing: true));
        Edited();
    }

    [RelayCommand]
    private void BoundDependency()
    {
        Dependencies.Add(new ReleaseAmendmentDependencyRow(this, isMissing: false));
        Edited();
    }

    internal void Remove(ReleaseAmendmentDependencyRow row)
    {
        if (Dependencies.Remove(row))
            Edited();
    }

    [RelayCommand]
    private void ShowPullRequest()
    {
        if (Opened is { } pull)
            Error = _owner.TryOpenWithSystem(pull.Url.AbsoluteUri);
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (CanClose)
            _owner.CloseReleaseAmendment(this);
    }

    private static string? Typed(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One stamped release, which the steward checks to amend it.</summary>
public sealed partial class ReleaseAmendmentVersion(ReleaseAmendmentDialog dialog, string version) : ObservableObject
{
    public string Version { get; } = version;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => dialog.Edited();
}

/// <summary>A dependency that was missing, with its kind, or a dependency that the releases state, with tighter bounds.</summary>
public sealed partial class ReleaseAmendmentDependencyRow : ObservableObject
{
    private readonly ReleaseAmendmentDialog _dialog;

    public ReleaseAmendmentDependencyRow(ReleaseAmendmentDialog dialog, bool isMissing)
    {
        _dialog = dialog;
        IsMissing = isMissing;
    }

    public static IReadOnlyList<string> Kinds => ListingEditor.DependencyKinds;

    /// <summary>Whether the row adds an entry that the releases do not state, which needs a kind.</summary>
    public bool IsMissing { get; }

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _kind = ListingEditor.DependencyKinds[^1];

    [ObservableProperty]
    private string _min = string.Empty;

    [ObservableProperty]
    private string _max = string.Empty;

    partial void OnIdChanged(string value) => _dialog.Edited();

    partial void OnKindChanged(string value) => _dialog.Edited();

    partial void OnMinChanged(string value) => _dialog.Edited();

    partial void OnMaxChanged(string value) => _dialog.Edited();

    [RelayCommand]
    private void Remove() => _dialog.Remove(this);
}

/// <summary>One selected release file of the preview, with its diff, or the note that it already says this.</summary>
public sealed class ReleaseAmendmentFile(MainViewModel owner, ReleaseFilePreview file)
{
    public ReleaseFilePreview File { get; } = file;

    public bool IsChanged => File.After is not null;

    public string? UnchangedText => IsChanged ? null : owner.Localization.StewardAmendUnchanged;

    public IReadOnlyList<StewardPatchLine> Lines { get; } = file.Patch?.Split('\n').Select(line => new StewardPatchLine(line)).ToList() ?? [];
}
