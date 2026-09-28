using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class ViewModelHarnessTests
{
    [Fact]
    public async Task Dispose_DeletesTheFolderOnlyAfterAPreferenceSaveOfAnotherViewModel()
    {
        var harness = await ViewModelHarness.CreateAsync();
        var store = new SlowPreferencesStore(harness.Services.AppPreferences, harness.Root);
        var viewModel = harness.NewViewModel(store);

        // the load records the first start, which saves the preferences
        await viewModel.LoadAsync();
        await store.Started.Task;
        harness.Dispose();

        Assert.True(store.Saved);
        Assert.True(store.FolderWasThere);
        Assert.False(Directory.Exists(harness.Root));
    }

    /// <summary>Waits before each save, and records whether the folder of the harness was still there.</summary>
    private sealed class SlowPreferencesStore(IAppPreferencesRepository inner, string root) : IAppPreferencesRepository
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FolderWasThere { get; private set; }

        public bool Saved { get; private set; }

        public Task<AppPreferencesLoadResult> GetAsync(IReadOnlyCollection<string> bundledThemeNames, CancellationToken cancellationToken = default) =>
            inner.GetAsync(bundledThemeNames, cancellationToken);

        public async Task SaveAsync(AppPreferences preferences, IReadOnlyCollection<string> bundledThemeNames, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            FolderWasThere = Directory.Exists(root);
            await inner.SaveAsync(preferences, bundledThemeNames, cancellationToken);
            Saved = true;
        }
    }
}
