using System.Formats.Tar;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictFramingBoundaryTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Theory]
    [InlineData("ustar", "package/evil.json ")]
    [InlineData("ustar", " package/evil.json")]
    [InlineData("prefix", "package/evil.json ")]
    [InlineData("prefix", "package /evil.json")]
    [InlineData("pax", "package/evil.json ")]
    [InlineData("pax", " package/evil.json")]
    [InlineData("gnu", "package/evil.json ")]
    [InlineData("gnu", " package/evil.json")]
    public async Task GivenUnsafeWirePath_WhenStrictExtracting_ThenValidatesBeforeReaderTrimming(string format, string path)
    {
        byte[] tar = WirePath(format, path, "{}");

        var failure = await Fails(tar);

        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.UnsafePath);
        failure.Diagnostic.EntryIndex.ShouldBe(format is "pax" or "gnu" ? 3 : 2);
    }

    [Theory]
    [InlineData("ustar")]
    [InlineData("prefix")]
    [InlineData("pax")]
    [InlineData("gnu")]
    public async Task GivenSafeWirePath_WhenStrictExtracting_ThenRetainsExactIdentity(string format)
    {
        const string path = "package/caf\u00e9.json";
        var result = await Extract(WirePath(format, path, StrictPackageFixture.SearchParameter));

        result.JsonEntries.Single().Path.ShouldBe(path);
        result.JsonEntries.Single().Json.ShouldBe(StrictPackageFixture.SearchParameter);
        result.Statistics.LogicalEntries.ShouldBe(2);
        result.Statistics.PhysicalEntries.ShouldBe(format is "pax" or "gnu" ? 3 : 2);
    }

    [Theory]
    [InlineData("package/SearchParameter-package.json", false)]
    [InlineData("package/SearchParameter-package.json", true)]
    [InlineData("package/notpackage.json", false)]
    [InlineData("package/notpackage.json", true)]
    public async Task GivenLongerManifestSuffix_WhenStrictExtracting_ThenPreservesResourceInEitherOrder(string path, bool manifestLast)
    {
        (string, string) manifest = ("package/package.json", StrictPackageFixture.Manifest);
        (string, string) resource = (path, StrictPackageFixture.SearchParameter);
        byte[] tar = manifestLast ? StrictPackageFixture.Tar(resource, manifest) : StrictPackageFixture.Tar(manifest, resource);

        var result = await Extract(tar);

        result.Manifest.Json.ShouldBe(StrictPackageFixture.Manifest);
        result.JsonEntries.Single().Path.ShouldBe(path);
        result.JsonEntries.Single().Json.ShouldBe(StrictPackageFixture.SearchParameter);
        result.JsonEntries.Single().ResourceType.ShouldBe("SearchParameter");
    }

    [Theory]
    [InlineData("pax")]
    [InlineData("gnu")]
    public async Task GivenMalformedJsonAfterMetadata_WhenStrictExtracting_ThenReportsPhysicalHeaderIndex(string format)
    {
        var failure = await Fails(WirePath(format, "package/data.json", "{"));

        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidJson);
        failure.Diagnostic.EntryIndex.ShouldBe(3);
    }

    [Theory]
    [InlineData("x", "x")]
    [InlineData("x", "L")]
    [InlineData("L", "x")]
    [InlineData("L", "L")]
    public async Task GivenConsecutiveMetadata_WhenStrictExtracting_ThenRejectsAmbiguousScope(string first, string second)
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest),
            ("metadata1", first == "x" ? StrictPackageFixture.PaxAttribute("comment", "first") : "package/first.json\0"),
            ("metadata2", second == "x" ? StrictPackageFixture.PaxAttribute("comment", "second") : "package/second.json\0"),
            ("package/a.json", "{}"));
        StrictPackageFixture.SetHeader(tar, 1024, 156, 1, first);
        StrictPackageFixture.SetHeader(tar, 2048, 156, 1, second);

        var failure = await Fails(tar);

        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
        failure.Diagnostic.EntryIndex.ShouldBe(3);
    }

    [Fact]
    public async Task GivenGnuLongLink_WhenStrictExtracting_ThenRejectsHiddenLinkMetadata()
    {
        byte[] tar = WirePath("gnu", "package/target.json", "{}");
        StrictPackageFixture.SetHeader(tar, 1024, 156, 1, "K");

        var failure = await Fails(tar);

        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.UnsupportedEntryType);
        failure.Diagnostic.EntryIndex.ShouldBe(2);
    }

    [Fact]
    public async Task GivenV7WireArchive_WhenStrictExtracting_ThenRetainsManifestAndResource()
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest),
            ("package/resource.json", StrictPackageFixture.SearchParameter));
        foreach (int offset in new[] { 0, 1024 })
        {
            StrictPackageFixture.SetHeader(tar, offset, 257, 255, string.Empty);
            StrictPackageFixture.SetHeader(tar, offset, 156, 1, "\0");
        }
        using (var reader = new TarReader(new MemoryStream(tar)))
        {
            (await reader.GetNextEntryAsync())!.Format.ShouldBe(TarEntryFormat.V7);
        }

        var result = await Extract(tar);

        result.Manifest.Json.ShouldBe(StrictPackageFixture.Manifest);
        result.JsonEntries.Single().Json.ShouldBe(StrictPackageFixture.SearchParameter);
        result.Statistics.PhysicalEntries.ShouldBe(2);
        result.Statistics.LogicalEntries.ShouldBe(2);
    }

    [Fact]
    public void GivenNullDiagnostic_WhenConstructingException_ThenRejectsArgument()
    {
        Should.Throw<ArgumentNullException>(() => new PackageExtractionException(null!)).ParamName.ShouldBe("diagnostic");
    }

    [Theory]
    [InlineData("leading-spaces")]
    [InlineData("full-octal")]
    [InlineData("base256")]
    public async Task GivenSupportedSizeEncoding_WhenStrictExtracting_ThenPreservesPayload(string encoding)
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest), ("package/data.json", "{}"));
        if (encoding == "base256")
        {
            SetBinarySize(tar, 0x80, false);
        }
        else
        {
            StrictPackageFixture.SetHeader(tar, 1024, 124, 12,
                encoding == "leading-spaces" ? "          2\0" : "000000000002");
        }

        (await Extract(tar)).JsonEntries.Single().Json.ShouldBe("{}");
    }

    [Theory]
    [InlineData("leading-nul")]
    [InlineData("embedded-nul")]
    [InlineData("negative-base256")]
    [InlineData("overflow-base256")]
    public async Task GivenAmbiguousOrUnsupportedSizeEncoding_WhenStrictExtracting_ThenRejectsArchive(string encoding)
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest), ("package/data.json", "{}"));
        if (encoding.EndsWith("base256", StringComparison.Ordinal))
        {
            SetBinarySize(tar, encoding == "negative-base256" ? (byte)0xff : (byte)0x80, true);
        }
        else
        {
            StrictPackageFixture.SetHeader(tar, 1024, 124, 12,
                encoding == "leading-nul" ? "\0" + "00000000002" : "000\00000002");
        }

        var failure = await Fails(tar);

        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
        failure.Diagnostic.EntryIndex.ShouldBe(2);
    }

    private static void SetBinarySize(byte[] tar, byte marker, bool overflow)
    {
        tar.AsSpan(1024 + 124, 12).Clear();
        tar[1024 + 124] = marker;
        tar[1024 + 135] = 2;
        if (overflow)
        {
            tar[1024 + 125] = 1;
        }
        StrictPackageFixture.SetHeader(tar, 1024, 156, 1, "0");
    }

    private static byte[] WirePath(string format, string path, string content)
    {
        if (format is "pax" or "gnu")
        {
            byte[] tar = StrictPackageFixture.Tar(
                ("package/package.json", StrictPackageFixture.Manifest),
                ("metadata", format == "pax" ? StrictPackageFixture.PaxAttribute("path", path) : path + "\0"),
                ("package/a.json", content));
            StrictPackageFixture.SetHeader(tar, 1024, 156, 1, format == "pax" ? "x" : "L");
            return tar;
        }
        byte[] plain = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest),
            (path, content));
        if (format == "prefix")
        {
            int separator = path.LastIndexOf('/');
            StrictPackageFixture.SetHeader(plain, 1024, 0, 100, path[(separator + 1)..]);
            StrictPackageFixture.SetHeader(plain, 1024, 345, 155, path[..separator]);
        }
        return plain;
    }

    private Task<StrictPackageExtractionResult> Extract(byte[] tar) =>
        _extractor.ExtractStrictAsync(new MemoryStream(StrictPackageFixture.Gzip(tar)), new(), CancellationToken.None);

    private Task<PackageExtractionException> Fails(byte[] tar) =>
        Should.ThrowAsync<PackageExtractionException>(() => Extract(tar));
}
