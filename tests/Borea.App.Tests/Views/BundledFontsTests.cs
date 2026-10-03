using System.Buffers.Binary;
using System.Text;
using Avalonia.Platform;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class BundledFontsTests
{
    private const string LicenseFile = "IBMPlex-OFL.txt";
    private const int NameCopyright = 0;
    private const int NameFamily = 1;
    private const int NameLicense = 13;

    [Fact]
    public async Task License_CoversEveryBundledFontFile()
    {
        var files = await HeadlessApp.RunAsync(() =>
        {
            var folder = new Uri("avares://Borea.App/Assets/Fonts/");
            return Task.FromResult(AssetLoader.GetAssets(folder, null).ToDictionary(uri => Path.GetFileName(uri.AbsolutePath), Read));
        });

        var license = Encoding.UTF8.GetString(files[LicenseFile]);
        Assert.Contains("IBM Corp. with Reserved Font Name \"Plex\"", license);
        Assert.Contains("SIL Open Font License, Version 1.1", license);
        var fonts = files.Where(file => file.Key != LicenseFile).ToDictionary(file => file.Key, file => Names(file.Value));
        Assert.Equal(
            [
                "IBMPlexMono-Regular.ttf",
                "IBMPlexSans-Bold.ttf",
                "IBMPlexSans-BoldItalic.ttf",
                "IBMPlexSans-Italic.ttf",
                "IBMPlexSans-Regular.ttf",
                "IBMPlexSans-SemiBold.ttf",
                "IBMPlexSans-SemiBoldItalic.ttf",
            ],
            fonts.Keys.Order(StringComparer.Ordinal));
        Assert.All(fonts.Values, names =>
        {
            Assert.Contains("IBM Corp.", names[NameCopyright]);
            Assert.StartsWith("IBM Plex", names[NameFamily]);
            Assert.StartsWith("This Font Software is licensed under the SIL Open Font License, Version 1.1.", names[NameLicense]);
        });
    }

    private static byte[] Read(Uri asset)
    {
        using var stream = AssetLoader.Open(asset);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The English Windows records of the OpenType name table, which every bundled font has.</summary>
    private static Dictionary<int, string> Names(byte[] font)
    {
        var data = font.AsSpan();
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        var table = Enumerable.Range(0, tableCount)
            .Select(index => 12 + 16 * index)
            .Single(record => Encoding.ASCII.GetString(font, record, 4) == "name");
        var start = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(table + 8)..]);
        var count = BinaryPrimitives.ReadUInt16BigEndian(data[(start + 2)..]);
        var storage = start + BinaryPrimitives.ReadUInt16BigEndian(data[(start + 4)..]);
        var names = new Dictionary<int, string>();
        for (var index = 0; index < count; index++)
        {
            var record = data[(start + 6 + 12 * index)..];
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            if (platform != 3 || language != 0x409)
                continue;

            var length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var offset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            names[BinaryPrimitives.ReadUInt16BigEndian(record[6..])] = Encoding.BigEndianUnicode.GetString(font, storage + offset, length);
        }

        return names;
    }
}
