using System.Globalization;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class TranslationPreviewViewModelTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BoreaTranslationPreview_" + Guid.NewGuid());

    [Fact]
    public void About_NamesTheLoadedTranslationFile()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "Resources.fr.resx");
        File.WriteAllText(path, "<root><data name=\"NavigationHome\"><value>Accueil</value></data></root>");
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en"), TranslationFiles.Load(_folder, _ => { }));

        var viewModel = new MainViewModel(localization);

        Assert.Equal(MainViewModel.WithoutUserProfile(path), viewModel.TranslationFilesText);
        Assert.Contains("Translation file: " + MainViewModel.WithoutUserProfile(path), viewModel.DiagnosticsText);
    }

    [Fact]
    public void About_WithoutTranslationFiles_NamesNone()
    {
        var viewModel = new MainViewModel(new LocalizationService(CultureInfo.GetCultureInfo("en")));

        Assert.Null(viewModel.TranslationFilesText);
        Assert.DoesNotContain("Translation file", viewModel.DiagnosticsText);
    }

    [Fact]
    public async Task MarkUntranslatedTexts_Switch_TurnsTheMarksOnAndSavesTheChoice()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("de"));
        var viewModel = new MainViewModel(localization, new RegionalFormatService(localization), harness.Services.AppPreferences, AppPreferences.Empty);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.MarkUntranslatedTexts = true;
        await viewModel.WhenPreferencesSavedAsync();

        Assert.True(localization.MarkUntranslatedTexts);
        Assert.Contains(nameof(MainViewModel.MarkUntranslatedTexts), changed);
        var saved = (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences;
        Assert.True(saved.MarkUntranslatedTexts);
        Assert.Null(saved.UiCultureName);

        viewModel.MarkUntranslatedTexts = false;
        await viewModel.WhenPreferencesSavedAsync();

        Assert.False(localization.MarkUntranslatedTexts);
        Assert.False((await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.MarkUntranslatedTexts);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
        TranslationResourceManager.Current.Files = TranslationFiles.None;
        TranslationResourceManager.Current.MarkUntranslatedTexts = false;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
