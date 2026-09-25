using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.Stewardship;
using Json.Schema;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Tests.Stewardship;

/// <summary>Every file that <see cref="IndexStatusDocument"/> writes is TOML that matches a copy of schemas/index-status.schema.json of content-index.</summary>
public sealed class IndexStatusSchemaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 18, 30, 5, TimeSpan.Zero);

    private static readonly IndexContents Contents = IndexContents.FromPaths(["listings/MyMod.toml", "listings/Other.toml", "packs/my-pack/1.0.0.toml", "packs/my-pack/1.1.0.toml"]);

    private static readonly JsonSchema Schema = JsonSchema.FromText(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "index-status.schema.json")),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    [Fact]
    public void EveryWrittenFile_ParsesAndMatchesTheSchema()
    {
        var current = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "index-status.toml"));
        var written = new List<IndexStatusDocument>();
        var document = IndexStatusDocument.Parse(current);
        foreach (var change in new[]
        {
            IndexStatusChange.Dispute("mymod", "Two authors claim the id \"MyMod\"."),
            IndexStatusChange.Retract("my-pack", "1.0.0", "It pins a release with a broken archive, C:\\ is fine."),
            IndexStatusChange.Retract("my-pack", "1.1.0", "Ein kaputtes Archiv, \u00e4rgerlich."),
            IndexStatusChange.Delist("Other", "Taken down on request."),
        })
        {
            document = document.Apply(change, Contents, Now);
            written.Add(document);
        }

        while (document.Entries.Count > 0)
        {
            document = document.Apply(IndexStatusChange.Lift(document.Entries[0], "Resolved."), Contents, Now);
            written.Add(document);
        }

        Assert.Equal(current, document.Text);
        Assert.All(written.Prepend(IndexStatusDocument.Parse(current)), Matches);
        Assert.Equal(["MyMod", "my-pack", "my-pack", "Other"], Json(written[3].Text)["entries"]!.AsArray().Select(entry => (string?)entry!["id"]));
    }

    private static void Matches(IndexStatusDocument document)
    {
        var json = Json(document.Text);
        var result = Schema.Evaluate(JsonSerializer.SerializeToElement(json), new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, document.Text);
        Assert.Equal(
            document.Entries.Select(entry => (entry.Id, entry.State, entry.Version, entry.Since, entry.Reason)),
            json["entries"]!.AsArray().Select(entry => ((string)entry!["id"]!, (string)entry["state"]!, (string?)entry["version"], (string?)entry["since"], (string?)entry["reason"])));
    }

    private static JsonNode Json(string toml) => Node(TomlSerializer.Deserialize<TomlTable>(toml)!)!;

    private static JsonNode? Node(object? value) => value switch
    {
        TomlTable table => new JsonObject(table.Select(pair => KeyValuePair.Create(pair.Key, Node(pair.Value)))),
        TomlTableArray tables => new JsonArray(tables.Select(Node).ToArray()),
        TomlArray array => new JsonArray(array.Select(Node).ToArray()),
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        long number => JsonValue.Create(number),
        _ => throw new InvalidOperationException($"index-status.toml holds a {value?.GetType().Name}, which no key of the schema takes."),
    };
}
