using System;
using System.Threading.Tasks;
using Borea.Core.Launch;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The modal that says how to install the .NET runtime a loader needs inside
/// a Wine wrapper, then checks the prefix again. Borea installs nothing itself.
/// </summary>
public partial class MainViewModel
{
    private Guid? _runtimePromptInstanceId;
    private bool _runtimeStillMissing;

    /// <summary>The refused launch the modal is about. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRuntimePromptOpen), nameof(RuntimePromptText), nameof(RuntimeDownloadText))]
    private RuntimePrompt? _promptedRuntime;

    /// <summary>Why the check or the download page did not work, shown in the modal.</summary>
    [ObservableProperty]
    private string? _runtimePromptError;

    public bool IsRuntimePromptOpen => PromptedRuntime is not null;

    public string? RuntimePromptText => PromptedRuntime is not { } prompt
        ? null
        : Localization.FormatLaunchRuntimeMissing(prompt.LoaderName, prompt.Runtime.Version.ToString(), prompt.WrapperPath, prompt.Runtime.Channel);

    public string? RuntimeDownloadText => PromptedRuntime is { } prompt ? Localization.FormatLaunchRuntimeDownload(prompt.Runtime.Channel) : null;

    private void OpenRuntimePrompt(Guid instanceId, LaunchResult result, ModMetadata loader)
    {
        // the modal stays open while it checks again, so an open one means the runtime is still not there
        RuntimePromptError = IsRuntimePromptOpen ? Localization.FormatLaunchRuntimeStillMissing(result.MissingRuntime!.Version.ToString()) : null;
        _runtimeStillMissing = true;
        _runtimePromptInstanceId = instanceId;
        LaunchMessage = null;
        PromptedRuntime = new RuntimePrompt(loader.Name, result.MissingRuntime!, result.Wine!.Wrapper!.BundlePath);
    }

    [RelayCommand]
    private async Task CheckRuntimeAgainAsync()
    {
        if (PromptedRuntime is null || _runtimePromptInstanceId is not { } instanceId || IsLaunching)
            return;

        RuntimePromptError = null;
        _runtimeStillMissing = false;
        await LaunchAsync(instanceId);

        // a launch that got past the check, or stopped before it, says so on the page
        if (!_runtimeStillMissing)
            PromptedRuntime = null;
    }

    [RelayCommand]
    private void OpenRuntimeDownload()
    {
        if (PromptedRuntime is { } prompt)
            RuntimePromptError = TryOpenWithSystem(prompt.Runtime.DownloadPage.AbsoluteUri);
    }

    [RelayCommand]
    private void CloseRuntimePrompt()
    {
        PromptedRuntime = null;
        RuntimePromptError = null;
    }
}

/// <param name="LoaderName">The loader that needs the runtime.</param>
/// <param name="Runtime">The runtime the Wine prefix lacks.</param>
/// <param name="WrapperPath">The .app folder of the Wine wrapper that the player installs the runtime into.</param>
public sealed record RuntimePrompt(string LoaderName, DotnetRuntimeNeed Runtime, string WrapperPath);
