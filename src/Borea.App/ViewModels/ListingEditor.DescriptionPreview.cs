using System;
using System.Collections.Generic;
using System.Linq;
using Borea.Core.Index;
using Borea.Core.Listings;
using CommunityToolkit.Mvvm.ComponentModel;
using PreviewImages = Borea.App.ViewModels.DescriptionImages;

namespace Borea.App.ViewModels;

/// <summary>The description as the mod page shows it, drawn from the draft and the measured description image rows.</summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ListingImageRecord> _previewRecords = [];

    private string? _shownDescription;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionPreview))]
    private bool _isDescriptionPreviewOn;

    /// <summary>
    /// The description as the listing file holds it. While the preview is off it keeps the text it showed last, because emptying
    /// the view removes every control of it, which is slow, and the next show then draws only what changed.
    /// </summary>
    public string? DescriptionPreview => _shownDescription;

    /// <summary>The measured rows as image records. A row that is not measured yet has no record, so the preview shows its placeholder.</summary>
    [ObservableProperty]
    private PreviewImages _descriptionPreviewImages = PreviewImages.None;

    partial void OnIsDescriptionPreviewOnChanged(bool value)
    {
        if (value)
            _shownDescription = Draft.Description;
        RefreshDescriptionPreview();
    }

    partial void OnDraftChanged(ListingDraft value)
    {
        if (IsDescriptionPreviewOn)
            _shownDescription = value.Description;
    }

    /// <summary>Makes new images only when a record changed, so typing in the description does not load them again.</summary>
    private void RefreshDescriptionPreview()
    {
        if (!IsDescriptionPreviewOn)
            return;

        var records = Draft.DescriptionImages;
        if (records.SequenceEqual(_previewRecords))
            return;

        _previewRecords = records;
        var images = records.Select(Measured).OfType<DescriptionImage>().DistinctBy(image => image.Id, StringComparer.Ordinal).Take(ContentImages.MaxDescriptionImages).ToList();
        DescriptionPreviewImages = images.Count == 0 ? PreviewImages.None : new PreviewImages(_owner, new ContentImages(icon: null, images));
    }

    /// <summary>The images of a description go with their page, so a page that is left keeps no bitmaps. The refresh that opening the page runs makes them again.</summary>
    private void DropDescriptionPreviewImages()
    {
        _previewRecords = [];
        _shownDescription = null;
        DescriptionPreviewImages = PreviewImages.None;
        OnPropertyChanged(nameof(DescriptionPreview));
    }

    /// <summary>The record as the index would carry it, or null when it is not measured or the checks would reject it.</summary>
    private static DescriptionImage? Measured(ListingImageRecord record)
    {
        if (record is not { Id: { } id, Sha256: { } sha256, Width: { } width, Height: { } height, Size: { } size })
            return null;

        try
        {
            return new DescriptionImage(id, record.Url, sha256, (int)Math.Min(width, int.MaxValue), (int)Math.Min(height, int.MaxValue), size, record.License, record.Attribution, record.Source);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
