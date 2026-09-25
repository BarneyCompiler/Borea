using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Borea.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The "List or change your mod" page, opened from the side panel of Discover. Its editor lives as long as the App,
/// so leaving the page and coming back keeps the draft.
/// </summary>
public partial class MainViewModel
{
    private ListingEditor? _listingEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    private bool _currentWindowListing;

    public ListingEditor ListingEditor => _listingEditor ??= new ListingEditor(this);

    internal BoreaServices? Services => _services;

    [RelayCommand]
    internal Task OpenListingAsync()
    {
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        IsTasksOpen = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
        CurrentWindowListing = true;
        return ListingEditor.OpenAsync();
    }

    /// <summary>Opens the listing page with a new pack of the mods of an instance, for a pack author who tested that set.</summary>
    internal async Task MakePackFromInstanceAsync(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        try
        {
            // Only a scan updates the stored manual folders, and the author can add or remove one by hand after the last scan.
            if (await services.Instances.GetByIdAsync(instanceId) is not null)
                await services.ForeignModAdopter.ScanAsync(instanceId);

            if (await services.Instances.GetByIdAsync(instanceId) is not { } instance)
            {
                await ReloadInstancesAsync();
                return;
            }

            var manifest = await services.ModState.GetEntriesAsync(instanceId);
            await OpenListingAsync();
            if (!ListingEditor.StartPackFromInstance(instance, manifest, services.Settings.LoaderInstallations))
                ShowErrorToast(() => Localization.FormatToastMakePackFailed(instance.Name), Localization.ToastMakePackNoIndex);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var instanceName = InstanceName(instanceId);
            ShowErrorToast(() => Localization.FormatToastMakePackFailed(instanceName), exception.Message);
        }
    }

    /// <summary>Opens a GitHub page, and returns why it could not, or null. The message leaves out the URL, which can carry the whole file.</summary>
    internal string? OpenListingPage(string url)
    {
        try
        {
            OpenWithSystem(url);
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return exception.Message;
        }
    }

    private void LeaveListingPage()
    {
        CurrentWindowListing = false;
        _listingEditor?.Leave();
    }
}
