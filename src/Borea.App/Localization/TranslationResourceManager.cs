using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Borea.App.Localization;

/// <summary>
/// The resource manager behind <see cref="Resources"/>. It shows the texts of <see cref="Files"/> over the
/// built texts of their culture, and with <see cref="MarkUntranslatedTexts"/> it marks every text that falls
/// back to English.
/// </summary>
internal sealed class TranslationResourceManager : ResourceManager
{
    // The neutral resources are the English source, so English texts never fall back.
    private const string EnglishName = "en";

    private static readonly Lazy<TranslationResourceManager> Installed = new(Install);

    // A culture without a satellite assembly would be searched for again on every lookup.
    private readonly ConcurrentDictionary<string, ResourceSet?> _builtSets = new(StringComparer.Ordinal);

    private TranslationResourceManager(string baseName, Assembly assembly)
        : base(baseName, assembly)
    {
    }

    public static TranslationResourceManager Current => Installed.Value;

    public TranslationFiles Files { get; set; } = TranslationFiles.None;

    public bool MarkUntranslatedTexts { get; set; }

    public override string? GetString(string name, CultureInfo? culture)
    {
        ArgumentNullException.ThrowIfNull(name);
        culture ??= CultureInfo.CurrentUICulture;

        for (var candidate = culture; !candidate.Equals(CultureInfo.InvariantCulture); candidate = candidate.Parent)
        {
            if (Files.TryGetText(candidate.Name, name, out var text))
                return text;

            if (BuiltSet(candidate)?.GetString(name) is { } built)
                return built;
        }

        var english = BuiltSet(CultureInfo.InvariantCulture)?.GetString(name);
        return english is not null && MarkUntranslatedTexts && culture.Name != EnglishName
            ? $"** {english} **"
            : english;
    }

    private ResourceSet? BuiltSet(CultureInfo culture)
        => _builtSets.GetOrAdd(culture.Name, static (_, state) => state.Manager.GetResourceSet(state.Culture, createIfNotExists: true, tryParents: false), (Manager: this, Culture: culture));

    // The generated Resources class creates its manager itself and has no setter for it, so this one goes into its cache field.
    private static TranslationResourceManager Install()
    {
        var field = typeof(Resources).GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The generated Resources class has no resourceMan field, so Borea cannot show translation files.");

        var manager = new TranslationResourceManager(Resources.ResourceManager.BaseName, typeof(Resources).Assembly);
        field.SetValue(null, manager);
        return manager;
    }
}
