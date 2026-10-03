using System.Text;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Borea.Core.Listings;

/// <summary>
/// Reads the Markdown of descriptions, release notes and posts as CommonMark with GFM tables and autolinks.
/// The App draws this parse and <see cref="MarkdownImages"/> finds the images in it, so both agree on what a text holds.
/// </summary>
public static class MarkdownSyntax
{
    /// <summary>
    /// The deepest nesting of blocks and inlines that a parse keeps. The App draws a parse by recursion, and the index
    /// accepts any text, so a deeper text reads as one paragraph of its source instead of overflowing the stack.
    /// </summary>
    public const int MaximumDepth = 32;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks(new AutoLinkOptions { UseHttpsForWWWLinks = true })
        .Build();

    /// <summary>Markdig throws an <see cref="ArgumentException"/> when blocks nest deeper than its own limit, so that text reads as its source too.</summary>
    public static MarkdownDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        MarkdownDocument document;
        try
        {
            document = Markdown.Parse(markdown, Pipeline);
        }
        catch (ArgumentException)
        {
            return Source(markdown);
        }

        return Nodes(document).Any(node => node.Depth > MaximumDepth) ? Source(markdown) : document;
    }

    /// <summary>Every block and inline under <paramref name="node"/>, in the order of the text.</summary>
    public static IEnumerable<MarkdownObject> Walk(MarkdownObject node) => Nodes(node).Select(item => item.Node);

    /// <summary>The walk keeps its own stack, so it also measures a parse that is too deep to walk by recursion.</summary>
    private static IEnumerable<(MarkdownObject Node, int Depth)> Nodes(MarkdownObject root)
    {
        var open = new Stack<IEnumerator<MarkdownObject>>();
        open.Push(Children(root));
        try
        {
            while (open.TryPeek(out var current))
            {
                if (!current.MoveNext())
                {
                    open.Pop().Dispose();
                    continue;
                }

                var child = current.Current;
                yield return (child, open.Count);
                open.Push(Children(child));
            }
        }
        finally
        {
            while (open.TryPop(out var rest))
                rest.Dispose();
        }
    }

    private static IEnumerator<MarkdownObject> Children(MarkdownObject node)
    {
        IEnumerable<MarkdownObject> children = node switch
        {
            ContainerBlock container => container,
            LeafBlock { Inline: { } inline } => [inline],
            ContainerInline container => container,
            _ => [],
        };
        return children.GetEnumerator();
    }

    private static MarkdownDocument Source(string markdown)
    {
        var inlines = new ContainerInline();
        inlines.AppendChild(new LiteralInline(markdown));
        return [new ParagraphBlock { Inline = inlines }];
    }

    /// <summary>The text of <paramref name="inline"/> without markup, as CommonMark reads the alternative text of an image. Raw HTML gives no text.</summary>
    public static string PlainText(Inline? inline)
    {
        var builder = new StringBuilder();
        Append(builder, inline);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, Inline? inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.AsSpan());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.AsSpan());
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case ContainerInline container:
                foreach (var child in container)
                    Append(builder, child);
                break;
        }
    }
}
