using System.Text.RegularExpressions;
using Borea.App.ViewModels;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed partial class ViewModelHarnessTests
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

    [Fact]
    public void EveryViewModelOnTheHarness_IsBuiltThroughNewViewModel()
    {
        var project = Path.Combine(RepositoryRoot, "tests", "Borea.App.Tests");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(project, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj" || Path.GetFileName(file) == "ViewModelHarness.cs")
                continue;

            var text = File.ReadAllText(file);
            foreach (Match construction in Construction().Matches(text))
            {
                var arguments = Arguments(text, construction.Index + construction.Length - 1);
                if (arguments.Contains("harness", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{relative}: {construction.Value}{arguments}");
            }
        }

        // Dispose waits only for the view models that NewViewModel recorded
        Assert.Empty(offenders);
    }

    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>A construction of a <see cref="MainViewModel"/>, up to its opening parenthesis.</summary>
    [GeneratedRegex(@"new\s+MainViewModel\s*\(|MainViewModel\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex Construction();

    /// <summary>The argument list that starts at the parenthesis at <paramref name="open"/>, without its outer parentheses.</summary>
    private static string Arguments(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            depth += text[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
                return text[(open + 1)..index];
        }

        return text[(open + 1)..];
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
