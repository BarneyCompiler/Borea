using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>
/// The tags of a list row as chips on one line. It shows at most <see cref="MaxShown"/> of them, only as many as fit whole,
/// and when some are left out a last chip says how many and names them in its tooltip.
/// </summary>
public sealed class TagChipRow : Panel
{
    internal const int MaxShown = 6;

    private const double Spacing = 4;

    public static readonly StyledProperty<IReadOnlyList<string>?> TagsProperty =
        AvaloniaProperty.Register<TagChipRow, IReadOnlyList<string>?>(nameof(Tags));

    private readonly TextBlock _moreText = new();
    private readonly Border _more;
    private int _shown;

    static TagChipRow() => AffectsMeasure<TagChipRow>(TagsProperty);

    public TagChipRow()
    {
        ClipToBounds = true;
        _more = Chip(_moreText);
        Children.Add(_more);
    }

    public IReadOnlyList<string>? Tags
    {
        get => GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    /// <summary>How many tags the last layout shows as chips.</summary>
    internal int ShownCount => _shown;

    /// <summary>The chip that names the left out tags, which the layout shows only when there are some.</summary>
    internal Border MoreChip => _more;

    internal bool ShowsMore { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TagsProperty)
            return;

        Children.Clear();
        foreach (var tag in (Tags ?? []).Take(MaxShown))
            Children.Add(Chip(new TextBlock { Text = tag }));
        Children.Add(_more);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var tags = Tags ?? [];
        var chips = Children.Where(child => child != _more).ToList();
        foreach (var chip in chips)
            chip.Measure(Size.Infinity);

        var shown = chips.Count;
        var width = RowWidth(chips, shown);
        var fitsAll = tags.Count <= MaxShown && width <= availableSize.Width;
        if (!fitsAll)
        {
            // give up tags from the end until the ones left and the chip that counts the others fit
            for (shown = chips.Count; shown >= 0; shown--)
            {
                ShowMore(tags, shown);
                width = RowWidth(chips, shown) + (shown > 0 ? Spacing : 0) + _more.DesiredSize.Width;
                if (width <= availableSize.Width || shown == 0)
                    break;
            }
        }
        else
        {
            _more.Measure(Size.Infinity);
        }

        _shown = shown;
        ShowsMore = !fitsAll;
        var height = chips.Take(shown).Append(ShowsMore ? _more : null).Max(child => child?.DesiredSize.Height ?? 0);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var index = 0;
        foreach (var child in Children)
        {
            var visible = child == _more ? ShowsMore : index++ < _shown;
            if (!visible)
            {
                // a chip that is left out gets no room, and its own clip draws nothing
                child.Arrange(default);
                continue;
            }

            child.Arrange(new Rect(x, (finalSize.Height - child.DesiredSize.Height) / 2, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        return finalSize;
    }

    private static double RowWidth(IReadOnlyList<Control> chips, int count) =>
        chips.Take(count).Sum(chip => chip.DesiredSize.Width) + Spacing * Math.Max(0, count - 1);

    private void ShowMore(IReadOnlyList<string> tags, int shown)
    {
        var left = tags.Skip(shown).ToList();
        var text = $"+{left.Count}";
        if (_moreText.Text != text)
            _moreText.Text = text;
        ToolTip.SetTip(_more, string.Join(", ", left));
        _more.Measure(Size.Infinity);
    }

    private static Border Chip(TextBlock text)
    {
        var chip = new Border { Child = text, ClipToBounds = true };
        chip.Classes.Add("chip");
        return chip;
    }
}
