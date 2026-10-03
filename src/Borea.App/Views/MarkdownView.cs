using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Borea.App.ViewModels;
using Borea.Core.Listings;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace Borea.App.Views;

/// <summary>
/// Shows a description, release notes or a post. <see cref="MarkdownSyntax"/> parses the text,
/// this control turns its blocks into controls, and <see cref="MarkdownText"/> writes the text of each block.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<DescriptionImages?> ImagesProperty =
        AvaloniaProperty.Register<MarkdownView, DescriptionImages?>(nameof(Images));

    internal static readonly FontFamily MonoFont = new("avares://Borea.App/Assets/Fonts#IBM Plex Mono");

    private const string Bullet = "\u2022";

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>The images the description can show. Null shows every image as its alternative text, which fits a changelog.</summary>
    public DescriptionImages? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public MarkdownView()
    {
        Spacing = 12;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == ImagesProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        if (!string.IsNullOrWhiteSpace(Markdown))
            AddBlocks(Children, MarkdownSyntax.Parse(Markdown), Images);
    }

    /// <summary>A link reference definition, and any block that CommonMark has but this view does not draw, shows nothing.</summary>
    private static void AddBlocks(Controls target, ContainerBlock container, DescriptionImages? images)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    MarkdownText.Write(target, heading.Inline, HeadingClass(heading.Level), images);
                    break;
                case ParagraphBlock paragraph:
                    MarkdownText.Write(target, paragraph.Inline, textClass: null, images);
                    break;
                case CodeBlock code:
                    target.Add(Code(code.Lines.ToString()));
                    break;
                case HtmlBlock html when MarkdownText.HtmlBlockText(html) is { Length: > 0 } text:
                    var body = MarkdownText.Body();
                    body.Inlines!.Add(new Run(text));
                    target.Add(body);
                    break;
                case ThematicBreakBlock:
                    target.Add(new Border { Height = 1, Background = Resource<IBrush>("Brush.BorderStrong") });
                    break;
                case ListBlock list:
                    target.Add(List(list, images));
                    break;
                case QuoteBlock quote:
                    target.Add(new Border
                    {
                        BorderBrush = Resource<IBrush>("Brush.BorderStrong"),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(16, 0, 0, 0),
                        Child = Blocks(quote, images),
                    });
                    break;
                case Table table:
                    target.Add(Table(table, images));
                    break;
            }
        }
    }

    private static StackPanel Blocks(ContainerBlock container, DescriptionImages? images)
    {
        var panel = new StackPanel { Spacing = 12 };
        AddBlocks(panel.Children, container, images);
        return panel;
    }

    private static string HeadingClass(int level) => level switch
    {
        1 => "heading-lg",
        2 => "heading-md",
        _ => "heading-sm",
    };

    /// <summary>Each item is a row of its marker and its blocks, so a nested list sits one indent deeper than the item that holds it.</summary>
    private static StackPanel List(ListBlock list, DescriptionImages? images)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20, 0, 0, 0) };
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? (number++).ToString(CultureInfo.InvariantCulture) + list.OrderedDelimiter : Bullet;
            var content = Blocks(item, images);
            Grid.SetColumn(content, 1);
            panel.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new TextBlock { Text = marker + "  ", Classes = { "body-md" }, FontSize = 16, LineHeight = 26 },
                    content,
                },
            });
        }

        return panel;
    }

    /// <summary>
    /// The header row decides the number of columns, as GFM does. Each column gets a share of the width by the
    /// longest cell in it, so the cells wrap inside the view instead of pushing it wider.
    /// </summary>
    private static Border Table(Table table, DescriptionImages? images)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count > 0 ? rows[0].Count : 0;
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
        {
            var longest = rows.Max(row => column < row.Count ? row[column].Span.Length : 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Clamp(longest, 4, 40), GridUnitType.Star));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < columns; column++)
            {
                var cell = new Border
                {
                    BorderBrush = Resource<IBrush>("Brush.BorderStrong"),
                    BorderThickness = new Thickness(column > 0 ? 1 : 0, index > 0 ? 1 : 0, 0, 0),
                    Padding = new Thickness(12, 8),
                    Child = column < row.Count ? Blocks((TableCell)row[column], images) : new StackPanel(),
                };
                if (row.IsHeader)
                {
                    cell.Background = Resource<IBrush>("Brush.SurfaceRaised");
                    TextElement.SetFontWeight(cell, FontWeight.SemiBold);
                }

                Grid.SetRow(cell, index);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            BorderBrush = Resource<IBrush>("Brush.BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = grid,
        };
    }

    internal static Control Figure(ListingImage image, string alternativeText)
    {
        var view = new ListingImageView
        {
            Image = image,
            LayoutFromRecord = true,
            Background = Resource<IBrush>("Brush.Surface"),
            Child = new Path { Classes = { "icon", "size-32" }, Data = Resource<Geometry>("Icon.Image") },
        };
        AutomationProperties.SetName(view, alternativeText);

        var frame = new Border { Classes = { "thumbnail" }, HorizontalAlignment = HorizontalAlignment.Left, Child = view };
        if (alternativeText.Length > 0)
            ToolTip.SetTip(frame, alternativeText);

        if (!image.HasCredit)
            return frame;

        var figure = new StackPanel { Spacing = 4, Children = { frame } };
        if (image.Attribution is { } attribution)
            figure.Children.Add(new TextBlock { Text = attribution, Classes = { "body-sm", "secondary" }, TextWrapping = TextWrapping.Wrap });

        if (image.Source is { } source)
        {
            var link = new Button
            {
                Classes = { "link" },
                HorizontalAlignment = HorizontalAlignment.Left,
                Command = image.OpenSourceCommand,
                Content = new TextBlock { Text = new Uri(source).Host, Classes = { "action-md" } },
            };
            ToolTip.SetTip(link, source);
            AutomationProperties.SetName(link, source);
            figure.Children.Add(link);
        }

        return figure;
    }

    internal static Border MissingImage(string alternativeText)
    {
        var glyph = new Path { Classes = { "icon" }, Data = Resource<Geometry>("Icon.Image"), Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(glyph, Dock.Left);
        var row = new DockPanel { Children = { glyph } };
        if (alternativeText.Length > 0)
            row.Children.Add(new TextBlock { Text = alternativeText, Classes = { "body-sm", "secondary" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });

        var placeholder = new Border { Classes = { "thumbnail" }, Padding = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        AutomationProperties.SetName(placeholder, alternativeText);
        return placeholder;
    }

    /// <summary>The text classes style only a TextBlock, so the selectable code block takes the font of label-md itself.</summary>
    private static SelectableTextBlock Code(string code) => new()
    {
        Text = code,
        FontFamily = MonoFont,
        Foreground = Resource<IBrush>("Brush.TextSecondary"),
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(12),
        Background = Resource<IBrush>("Brush.SurfaceHeader"),
    };

    internal static T? Resource<T>(string key)
        where T : class
        => Application.Current?.TryFindResource(key, out var value) == true ? value as T : null;
}
