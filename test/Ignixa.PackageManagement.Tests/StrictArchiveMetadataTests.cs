using System.Formats.Tar;
using System.Text;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictArchiveMetadataTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Theory]
    [InlineData("pax")]
    [InlineData("gnu")]
    public async Task GivenHiddenMetadata_WhenStrictExtracting_ThenCountsPhysicalHeadersAndResolvesPath(string format)
    {
        string path = "package/" + new string('a', 110) + ".json";
        TarEntry entry = format == "pax"
            ? new PaxTarEntry(TarEntryType.RegularFile, path)
            : new GnuTarEntry(TarEntryType.RegularFile, path);
        entry.DataStream = new MemoryStream("{}"u8.ToArray());
        byte[] tar = StrictPackageFixture.TarEntries(
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest), entry);
        byte[] gzip = StrictPackageFixture.Gzip(tar);

        var result = await Extract(gzip, new(maxArchiveEntries: 3));

        result.JsonEntries.Single().Path.ShouldBe(path);
        result.Statistics.PhysicalEntries.ShouldBe(3);
        result.Statistics.LogicalEntries.ShouldBe(2);
        (await Fails(gzip, new(maxArchiveEntries: 2))).Diagnostic.Code.ShouldBe(PackageExtractionError.EntryCountLimit);
    }

    [Fact]
    public async Task GivenLargePaxMetadata_WhenStrictExtracting_ThenPerEntryLimitIncludesHiddenPayload()
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "package/a.json",
            new Dictionary<string, string> { ["comment"] = new('x', 4096) })
        {
            DataStream = new MemoryStream("{}"u8.ToArray())
        };
        byte[] tar = StrictPackageFixture.TarEntries(
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest), entry);
        int metadataSize = Convert.ToInt32(Encoding.ASCII.GetString(tar, 1024 + 124, 12).Trim('\0', ' '), 8);
        byte[] gzip = StrictPackageFixture.Gzip(tar);

        var result = await Extract(gzip, new(maxEntryBytes: metadataSize));
        result.Statistics.PayloadBytes.ShouldBe(metadataSize + StrictPackageFixture.Manifest.Length + 2);
        var exception = await Fails(gzip, new(maxEntryBytes: metadataSize - 1));
        exception.Diagnostic.Code.ShouldBe(PackageExtractionError.EntrySizeLimit);
        exception.Diagnostic.EntryIndex.ShouldBe(2);
    }

    [Theory]
    [InlineData("path", "../escape.json", PackageExtractionError.UnsafePath)]
    [InlineData("path", "package/package.json", PackageExtractionError.DuplicateManifest)]
    [InlineData("size", "1000000", PackageExtractionError.UnsupportedEntryType)]
    [InlineData("linkpath", "package/target.json", PackageExtractionError.UnsupportedEntryType)]
    [InlineData("hdrcharset", "BINARY", PackageExtractionError.UnsupportedEntryType)]
    [InlineData("SCHILY.filetype", "sparse", PackageExtractionError.UnsupportedEntryType)]
    [InlineData("GNU.sparse.map", "0,1000000", PackageExtractionError.UnsupportedEntryType)]
    public async Task GivenStructuralPaxOverride_WhenStrictExtracting_ThenCannotBypassValidation(
        string key, string value, PackageExtractionError code)
    {
        // TarWriter replaces its own path/size attributes. Construct the actual wire override.
        byte[] tar = StrictPackageFixture.Pax(StrictPackageFixture.PaxAttribute(key, value));

        (await Fails(StrictPackageFixture.Gzip(tar), new())).Diagnostic.Code.ShouldBe(code);
    }

    [Fact]
    public async Task GivenGlobalPaxMetadata_WhenStrictExtracting_ThenRejectsUnsupportedScope()
    {
        byte[] tar = StrictPackageFixture.TarEntries(
            new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string> { ["comment"] = "global" }),
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest));

        (await Fails(StrictPackageFixture.Gzip(tar), new())).Diagnostic.Code.ShouldBe(PackageExtractionError.UnsupportedEntryType);
    }

    [Fact]
    public async Task GivenDeclaredOversizeBeforePayload_WhenStrictExtracting_ThenRejectsWithoutTrustingHeader()
    {
        byte[] tar = StrictPackageFixture.Tar(("package/package.json", StrictPackageFixture.Manifest));
        StrictPackageFixture.SetHeader(tar, 0, 124, 12, "00040000000");

        (await Fails(StrictPackageFixture.Gzip(tar), new(maxEntryBytes: 1024))).Diagnostic.Code.ShouldBe(PackageExtractionError.EntrySizeLimit);
    }

    [Fact]
    public async Task GivenTarTrailingZeroBlocks_WhenStrictExtracting_ThenCountsAndAcceptsRecordPadding()
    {
        byte[] tar = [.. StrictPackageFixture.Tar(("package/package.json", StrictPackageFixture.Manifest)), .. new byte[1024]];

        (await Extract(StrictPackageFixture.Gzip(tar), new())).Statistics.ExpandedBytes.ShouldBe(tar.Length);
    }

    private Task<StrictPackageExtractionResult> Extract(byte[] gzip, PackageExtractionLimits limits) =>
        _extractor.ExtractStrictAsync(new MemoryStream(gzip), limits, CancellationToken.None);

    private Task<PackageExtractionException> Fails(byte[] gzip, PackageExtractionLimits limits) =>
        Should.ThrowAsync<PackageExtractionException>(() => Extract(gzip, limits));
}
