using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Borea.App.Localization;

/// <summary>
/// The Resources.&lt;culture&gt;.resx files of the Languages folder. Their texts show over the built texts of
/// their culture, so a translator sees a translation without a build.
/// </summary>
/// <remarks>
/// A file comes from outside, so Borea reads it as plain XML and takes only the name and the value of each
/// data element. It never uses ResXResourceReader, which can deserialize types. A file with a typed entry is
/// refused as a whole. A text whose key the English texts do not have, or whose placeholders differ from its
/// English text, is refused alone.
/// </remarks>
public sealed class TranslationFiles
{
    private const string FilePrefix = "Resources.";

    private const string FileExtension = ".resx";

    public static TranslationFiles None { get; } = new([]);

    private readonly IReadOnlyList<TranslationFile> _files;

    private TranslationFiles(IReadOnlyList<TranslationFile> files)
    {
        _files = files;
        Paths = files.Select(file => file.Path).ToList();
    }

    /// <summary>The files Borea took, in the order of their names.</summary>
    public IReadOnlyList<string> Paths { get; }

    internal IEnumerable<CultureInfo> Cultures => _files.Select(file => file.Culture);

    internal bool TryGetText(string cultureName, string key, [NotNullWhen(true)] out string? text)
    {
        foreach (var file in _files)
        {
            if (file.Culture.Name == cultureName)
                return file.Texts.TryGetValue(key, out text);
        }

        text = null;
        return false;
    }

    /// <summary>Reads the files of <paramref name="folder"/> and checks them against the built English texts.</summary>
    /// <param name="log">Takes one line for each file and for each text that Borea refuses.</param>
    public static TranslationFiles Load(string folder, Action<string> log)
        => Load(folder, EnglishTexts(), log);

    internal static TranslationFiles Load(string folder, IReadOnlyDictionary<string, string> english, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(log);

        string[] paths;
        try
        {
            if (!Directory.Exists(folder))
                return None;

            paths = Directory.GetFiles(folder, "*" + FileExtension);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log($"Borea cannot read the translation files in {folder}: {exception.Message}");
            return None;
        }

        var files = new List<TranslationFile>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            if (CultureOf(Path.GetFileName(path)) is not { } culture)
            {
                log($"Borea ignores {path}, because its name is not {FilePrefix}<culture>{FileExtension} with a culture that .NET knows.");
                continue;
            }

            if (files.Any(file => file.Culture.Name == culture.Name))
            {
                log($"Borea ignores {path}, because another translation file is for {culture.Name} already.");
                continue;
            }

            if (Read(path, english, log) is { } texts)
            {
                files.Add(new TranslationFile(path, culture, texts));
                log($"Borea shows {texts.Count} texts of the translation file {path}.");
            }
        }

        return files.Count == 0 ? None : new TranslationFiles(files);
    }

    private static CultureInfo? CultureOf(string fileName)
    {
        if (fileName.Length <= FilePrefix.Length + FileExtension.Length
            || !fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        var cultureName = fileName[FilePrefix.Length..^FileExtension.Length];
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase) ? culture : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <returns>The accepted texts by key, or null when the file is refused as a whole.</returns>
    private static Dictionary<string, string>? Read(string path, IReadOnlyDictionary<string, string> english, Action<string> log)
    {
        XElement root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(path, settings);
            root = XDocument.Load(reader).Root!;
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            log($"Borea does not read the translation file {path}: {exception.Message}");
            return null;
        }

        if (root.Name != "root")
        {
            log($"Borea does not read the translation file {path}, because it is not a .resx file.");
            return null;
        }

        var entries = root.Elements("data").ToList();
        if (entries.Find(entry => entry.Attribute("type") is not null || entry.Attribute("mimetype") is not null) is { } typed)
        {
            log($"Borea does not read the translation file {path}, because its entry {(string?)typed.Attribute("name")} has a type or a mimetype, and a translation file holds only text.");
            return null;
        }

        var fileName = Path.GetFileName(path);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = (string?)entry.Attribute("name");
            var text = (string?)entry.Element("value");

            // an empty value is a text that the file does not translate yet, so the built text of the culture or the English text shows
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text))
                continue;

            if (!english.TryGetValue(key, out var englishText))
                log($"{fileName}: Borea does not use the text of {key}, because the English texts have no such key.");
            else if (!ResourcePlaceholders.Match(englishText, text))
                log($"{fileName}: Borea does not use the text of {key}, because its placeholders differ from the English text \"{englishText}\".");
            else if (!texts.TryAdd(key, text))
                log($"{fileName}: Borea uses only the first text of {key}, because the file has the key twice.");
        }

        return texts;
    }

    private static Dictionary<string, string> EnglishTexts()
    {
        var neutral = Resources.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException("The English texts are not in the Borea.App assembly.");

        return neutral.Cast<DictionaryEntry>()
            .Where(entry => entry.Value is string)
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
    }

    private sealed record TranslationFile(string Path, CultureInfo Culture, IReadOnlyDictionary<string, string> Texts);
}
