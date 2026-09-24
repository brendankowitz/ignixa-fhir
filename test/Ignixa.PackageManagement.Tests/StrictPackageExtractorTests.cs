using System.Formats.Tar;
using System.Text;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictPackageExtractorTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Fact]
    public async Task GivenNonSeekablePackage_WhenStrictExtracting_ThenPreservesAllJsonAndLeavesInputOpen()
    {
        const string missingIdentity = """{ "resourceType": "SearchParameter", "code": "missing-identity" }""";
        byte[] package = StrictPackageFixture.Package(
            ("package/search.json", missingIdentity),
            ("package/patient.json", """{"resourceType":"Patient","id":"p"}"""),
            ("package/.index.json", """{"files":[]}"""),
            ("package/readme.txt", "ignored"));
        using var input = new StrictInputStream(package);

        StrictPackageExtractionResult result = await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None);

        result.Manifest.Name.ShouldBe("example.fhir.search");
        result.Manifest.Version.ShouldBe("1.2.0");
        result.Manifest.FhirVersion.ShouldBeNull();
        result.Manifest.FhirVersions.ShouldBe(["4.0.1", "3.0.2"]);
        result.JsonEntries.Count.ShouldBe(3);
        result.JsonEntries[0].Path.ShouldBe("package/search.json");
        result.JsonEntries[0].Json.ShouldBe(missingIdentity);
        result.JsonEntries.Select(e => e.ResourceType).ShouldBe(["SearchParameter", "Patient", null]);
        result.Statistics.PhysicalEntries.ShouldBe(5);
        result.Statistics.LogicalEntries.ShouldBe(5);
        result.Statistics.CompressedBytes.ShouldBe(package.Length);
        input.BytesRead.ShouldBe(package.Length);
        input.Disposed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAllJsonKinds_WhenManifestOrderChanges_ThenPreservesRawAcquiredPackage(bool manifestLast)
    {
        const string manifest = " {\n \"name\":\"example.fhir.search\", \"version\":\"1.2.0\", " +
            "\"fhirVersions\":[\"4.0.1\",\"3.0.2\"], \"dependencies\":{\"example.other\":\"2.0.0\"}\n} ";
        StrictPackageEntry[] expected =
        [
            new("package/search.json", """{ "resourceType": "SearchParameter", "code": "missing-id" }""", "SearchParameter"),
            new("package/Patient-p.json", """ {"resourceType":"Patient", "id":"p"} """, "Patient"),
            new("package/future.JSON", """{"resourceType":"FutureResource","detail":"kept"}""", "FutureResource"),
            new("package/.index.json", """{"files":[]}""", null),
            new("package/scalar.json", "42", null)
        ];
        (string Path, string Json)[] files = [.. expected.Select(entry => (entry.Path, entry.Json)), ("package/readme.txt", "ignored")];
        byte[] tar = StrictPackageFixture.Tar(manifestLast
            ? [.. files, ("package/package.json", manifest)]
            : [("package/package.json", manifest), .. files]);

        var result = await Extract(StrictPackageFixture.Gzip(tar));

        result.Manifest.Json.ShouldBe(manifest);
        result.Manifest.Name.ShouldBe("example.fhir.search");
        result.Manifest.Version.ShouldBe("1.2.0");
        result.Manifest.FhirVersion.ShouldBeNull();
        result.Manifest.FhirVersions.ShouldBe(["4.0.1", "3.0.2"]);
        result.JsonEntries.ShouldBe(expected);
        result.Statistics.PhysicalEntries.ShouldBe(7);
        result.Statistics.LogicalEntries.ShouldBe(7);
    }

    [Fact]
    public async Task GivenInputAtCurrentPosition_WhenStrictExtracting_ThenDoesNotRewind()
    {
        byte[] package = StrictPackageFixture.Package();
        using var input = new MemoryStream([1, 2, 3, .. package]);
        input.Position = 3;

        var result = await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None);

        result.Manifest.Name.ShouldBe("example.fhir.search");
        result.Statistics.CompressedBytes.ShouldBe(package.Length);
        input.CanRead.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"name":"test","version":"1","fhirVersion":"3.0.2"}""", "3.0.2", new string[0])]
    [InlineData("""{"name":"test","version":"1"}""", null, new string[0])]
    [InlineData("""{"name":"test","version":"1","fhirVersions":["4.0.1","3.0.2"]}""", null, new[] { "4.0.1", "3.0.2" })]
    [InlineData("""{"name":"test","version":"1","fhirVersions":[]}""", null, new string[0])]
    [InlineData("""{"name":"test","version":"1","fhirVersion":"4.0.1","fhirVersions":["3.0.2"]}""", "4.0.1", new[] { "3.0.2" })]
    public async Task GivenManifestOrder_WhenStrictExtracting_ThenMetadataIsTruthfulAndOrderIndependent(
        string manifest, string? singular, string[] plural)
    {
        var first = await Extract(StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
            ("package/package.json", manifest), ("package/sp.json", StrictPackageFixture.SearchParameter))));
        var last = await Extract(StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
            ("package/sp.json", StrictPackageFixture.SearchParameter), ("package/package.json", manifest))));

        first.Manifest.Json.ShouldBe(manifest);
        last.Manifest.Json.ShouldBe(manifest);
        first.Manifest.Name.ShouldBe("test");
        last.Manifest.Name.ShouldBe("test");
        first.Manifest.Version.ShouldBe("1");
        last.Manifest.Version.ShouldBe("1");
        first.Manifest.FhirVersion.ShouldBe(singular);
        last.Manifest.FhirVersion.ShouldBe(singular);
        first.Manifest.FhirVersions.ShouldBe(plural);
        last.Manifest.FhirVersions.ShouldBe(plural);
        first.JsonEntries.Single().Json.ShouldBe(StrictPackageFixture.SearchParameter);
        first.JsonEntries.Single().Path.ShouldBe("package/sp.json");
        first.JsonEntries.Single().ResourceType.ShouldBe("SearchParameter");
        first.JsonEntries.ShouldBe(last.JsonEntries);
        first.Statistics.ShouldBe(last.Statistics with { CompressedBytes = first.Statistics.CompressedBytes });
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"version":"1"}""")]
    [InlineData("""{"name":"","version":"1"}""")]
    [InlineData("""{"name":"test","version":" "}""")]
    [InlineData("""{"name":1,"version":"1"}""")]
    [InlineData("""{"name":"test","version":null}""")]
    [InlineData("""{"name":"test","version":"1","fhirVersion":null}""")]
    [InlineData("""{"name":"test","version":"1","fhirVersions":"4.0.1"}""")]
    [InlineData("""{"name":"test","version":"1","fhirVersions":[null]}""")]
    [InlineData("""{"name":"test","version":"1","name":"other"}""")]
    public async Task GivenInvalidManifest_WhenStrictExtracting_ThenFailsExplicitly(string manifest)
    {
        await Fails(StrictPackageFixture.Gzip(StrictPackageFixture.Tar(("package/package.json", manifest))),
            PackageExtractionError.InvalidManifest);
    }

    [Fact]
    public async Task GivenMissingManifest_WhenStrictExtracting_ThenFails()
    {
        await Fails(StrictPackageFixture.Gzip(StrictPackageFixture.Tar(("package/sp.json", StrictPackageFixture.SearchParameter))),
            PackageExtractionError.MissingManifest);
    }

    [Fact]
    public async Task GivenDuplicateManifest_WhenStrictExtracting_ThenFails()
    {
        await Fails(StrictPackageFixture.Package(("package/package.json", StrictPackageFixture.Manifest)),
            PackageExtractionError.DuplicateManifest);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("other/package.json")]
    [InlineData("package/nested/package.json")]
    [InlineData("package/Package.json")]
    [InlineData("PACKAGE/package.json")]
    public async Task GivenMisplacedOrAliasedManifest_WhenStrictExtracting_ThenFails(string path)
    {
        await Fails(StrictPackageFixture.Gzip(StrictPackageFixture.Tar((path, StrictPackageFixture.Manifest))),
            PackageExtractionError.InvalidManifest);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("""{"resourceType":null}""")]
    [InlineData("""{"resourceType":12}""")]
    [InlineData("""{"resourceType":{}}""")]
    [InlineData("""{"resourceType":""}""")]
    [InlineData("""{"resourceType":" "}""")]
    [InlineData("""{"resourceType":"Patient","resourceType":"SearchParameter"}""")]
    public async Task GivenInvalidResourceJson_WhenStrictExtracting_ThenFailsWithoutContentLeak(string json)
    {
        var exception = await Fails(StrictPackageFixture.Package(("package/private-name.json", json)), PackageExtractionError.InvalidJson);
        exception.Diagnostic.EntryIndex.ShouldBe(2);
        exception.ToString().ShouldNotContain("private-name");
        exception.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData("/package/evil.json")]
    [InlineData("C:/package/evil.json")]
    [InlineData("//server/package/evil.json")]
    [InlineData("package\\evil.json")]
    [InlineData("package/../evil.json")]
    [InlineData("package/./evil.json")]
    [InlineData("./package/evil.json")]
    [InlineData("package//evil.json")]
    [InlineData("package/evil.json.")]
    [InlineData("package/evil.json ")]
    [InlineData("package/evil:stream")]
    [InlineData("package/evil\u0001.json")]
    [InlineData("other/evil.json")]
    public async Task GivenUnsafePath_WhenStrictExtracting_ThenFails(string path)
    {
        await Fails(StrictPackageFixture.Package((path, "{}")), PackageExtractionError.UnsafePath);
    }

    [Theory]
    [InlineData("package/a.json", "package/a.json")]
    [InlineData("package/a.json", "package/A.json")]
    [InlineData("package/folder", "package/folder/a.json")]
    [InlineData("package/folder/a.json", "package/folder")]
    public async Task GivenCollidingPaths_WhenStrictExtracting_ThenFails(string first, string second)
    {
        await Fails(StrictPackageFixture.Package((first, "{}"), (second, "{}")), PackageExtractionError.DuplicatePath);
    }

    [Fact]
    public async Task GivenBenignDirectories_WhenStrictExtracting_ThenCountsThem()
    {
        byte[] tar = StrictPackageFixture.TarEntries(
            new UstarTarEntry(TarEntryType.Directory, "package/"),
            new UstarTarEntry(TarEntryType.Directory, "package/sub/"),
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest),
            StrictPackageFixture.File("package/sub/data.json", "{}"));

        var result = await Extract(StrictPackageFixture.Gzip(tar));

        result.Statistics.PhysicalEntries.ShouldBe(4);
        result.Statistics.LogicalEntries.ShouldBe(4);
        result.JsonEntries.Single().ResourceType.ShouldBeNull();
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    [InlineData(TarEntryType.Fifo)]
    [InlineData(TarEntryType.BlockDevice)]
    [InlineData(TarEntryType.CharacterDevice)]
    public async Task GivenSpecialEntry_WhenStrictExtracting_ThenRejects(TarEntryType type)
    {
        var entry = new UstarTarEntry(type, "package/link");
        if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink)
        {
            entry.LinkName = "package/package.json";
        }
        byte[] tar = StrictPackageFixture.TarEntries(
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest), entry);

        await Fails(StrictPackageFixture.Gzip(tar), PackageExtractionError.UnsupportedEntryType);
    }

    [Theory]
    [InlineData("compressed")]
    [InlineData("expanded")]
    [InlineData("entry")]
    [InlineData("count")]
    [InlineData("depth")]
    public async Task GivenExactAndOneOverLimit_WhenStrictExtracting_ThenBoundaryIsInclusive(string bound)
    {
        string ignored = new('x', 512);
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest),
            ("package/ignored.bin", ignored),
            ("package/metadata.json", """{"a":{"b":1}}"""));
        byte[] gzip = StrictPackageFixture.Gzip(tar);
        int limit = bound switch
        {
            "compressed" => gzip.Length,
            "expanded" => tar.Length,
            "entry" => 512,
            "count" => 3,
            _ => 2
        };
        PackageExtractionLimits Limits(int maximum) => bound switch
        {
            "compressed" => new(maxCompressedBytes: maximum),
            "expanded" => new(maxExpandedBytes: maximum),
            "entry" => new(maxEntryBytes: maximum),
            "count" => new(maxArchiveEntries: maximum),
            _ => new(maxJsonDepth: maximum)
        };
        PackageExtractionError expected = bound switch
        {
            "compressed" => PackageExtractionError.CompressedSizeLimit,
            "expanded" => PackageExtractionError.ExpandedSizeLimit,
            "entry" => PackageExtractionError.EntrySizeLimit,
            "count" => PackageExtractionError.EntryCountLimit,
            _ => PackageExtractionError.JsonDepthLimit
        };

        var result = await Extract(gzip, Limits(limit));
        result.Statistics.ExpandedBytes.ShouldBe(tar.Length);
        result.Statistics.PayloadBytes.ShouldBe(Encoding.UTF8.GetByteCount(StrictPackageFixture.Manifest) + 512 + 13);
        (await Fails(gzip, expected, Limits(limit - 1))).Diagnostic.Limit.ShouldBe(limit - 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveLimits_WhenConstructing_ThenRejectsEachIndependentBound(int value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PackageExtractionLimits(maxCompressedBytes: value));
        Should.Throw<ArgumentOutOfRangeException>(() => new PackageExtractionLimits(maxExpandedBytes: value));
        Should.Throw<ArgumentOutOfRangeException>(() => new PackageExtractionLimits(maxEntryBytes: value));
        Should.Throw<ArgumentOutOfRangeException>(() => new PackageExtractionLimits(maxArchiveEntries: value));
        Should.Throw<ArgumentOutOfRangeException>(() => new PackageExtractionLimits(maxJsonDepth: value));
    }

    [Fact]
    public async Task GivenCancellationDuringByteReads_WhenStrictExtracting_ThenPropagatesAndLeavesInputOpen()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new StrictInputStream(StrictPackageFixture.Package(), 1, count =>
        {
            if (count == 20)
            {
                cancellation.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _extractor.ExtractStrictAsync(input, new(), cancellation.Token));

        input.BytesRead.ShouldBe(20);
        input.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenCancellationAtFinalRead_WhenStrictExtracting_ThenDoesNotReturnSuccess()
    {
        byte[] package = StrictPackageFixture.Package();
        using var cancellation = new CancellationTokenSource();
        using var input = new StrictInputStream(package, package.Length, _ => cancellation.Cancel());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _extractor.ExtractStrictAsync(input, new(), cancellation.Token));
    }

    [Theory]
    [InlineData("gzip-header")]
    [InlineData("gzip-deflate")]
    [InlineData("gzip-footer")]
    [InlineData("gzip-crc")]
    [InlineData("gzip-size")]
    [InlineData("gzip-trailing")]
    [InlineData("gzip-concatenated")]
    [InlineData("tar-header")]
    [InlineData("tar-payload")]
    [InlineData("tar-padding")]
    [InlineData("tar-one-end-block")]
    [InlineData("tar-no-end-block")]
    [InlineData("tar-trailing")]
    [InlineData("tar-concatenated")]
    public async Task GivenCorruptOrIncompleteArchive_WhenStrictExtracting_ThenNeverReturnsPartialSuccess(string corruption)
    {
        byte[] tar = StrictPackageFixture.Tar(("package/package.json", StrictPackageFixture.Manifest));
        byte[] gzip = StrictPackageFixture.Gzip(tar);
        switch (corruption)
        {
            case "gzip-header": gzip = gzip[..5]; break;
            case "gzip-deflate": gzip = gzip[..(gzip.Length / 2)]; break;
            case "gzip-footer": gzip = gzip[..^1]; break;
            case "gzip-crc": gzip[^8] ^= 1; break;
            case "gzip-size": gzip[^4] ^= 1; break;
            case "gzip-trailing": gzip = [.. gzip, 0]; break;
            case "gzip-concatenated": gzip = [.. gzip, .. gzip]; break;
            case "tar-header": tar[0] ^= 1; gzip = StrictPackageFixture.Gzip(tar); break;
            case "tar-payload": gzip = StrictPackageFixture.Gzip(tar[..550]); break;
            case "tar-padding": tar[900] = 1; gzip = StrictPackageFixture.Gzip(tar); break;
            case "tar-one-end-block": gzip = StrictPackageFixture.Gzip(tar[..^512]); break;
            case "tar-no-end-block": gzip = StrictPackageFixture.Gzip(tar[..^1024]); break;
            case "tar-trailing": gzip = StrictPackageFixture.Gzip([.. tar, 1]); break;
            default: gzip = StrictPackageFixture.Gzip([.. tar, .. tar]); break;
        }

        await Fails(gzip, PackageExtractionError.InvalidArchive);
    }

    [Fact]
    public async Task GivenCompressedLimit_WhenReadingUnboundedStream_ThenReadsOnlyOneByteBeyondLimit()
    {
        using var input = new StrictInputStream(new byte[10_000], 10_000);

        var exception = await Should.ThrowAsync<PackageExtractionException>(() =>
            _extractor.ExtractStrictAsync(input, new(maxCompressedBytes: 100), CancellationToken.None));

        exception.Diagnostic.Code.ShouldBe(PackageExtractionError.CompressedSizeLimit);
        input.BytesRead.ShouldBe(101);
        input.Disposed.ShouldBeFalse();
    }

    private Task<StrictPackageExtractionResult> Extract(byte[] gzip, PackageExtractionLimits? limits = null) =>
        _extractor.ExtractStrictAsync(new MemoryStream(gzip), limits ?? new(), CancellationToken.None);

    private async Task<PackageExtractionException> Fails(
        byte[] gzip, PackageExtractionError code, PackageExtractionLimits? limits = null)
    {
        var exception = await Should.ThrowAsync<PackageExtractionException>(() => Extract(gzip, limits));
        exception.Diagnostic.Code.ShouldBe(code);
        return exception;
    }
}
