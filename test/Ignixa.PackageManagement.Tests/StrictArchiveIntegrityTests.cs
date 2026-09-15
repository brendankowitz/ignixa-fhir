using System.Buffers.Binary;
using System.Formats.Tar;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictArchiveIntegrityTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenInvalidUtf8Json_WhenStrictExtracting_ThenDoesNotReplaceOriginalBytes(bool manifest)
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", manifest
                ? """{"name":"x","version":"1"}"""
                : StrictPackageFixture.Manifest),
            ("package/data.json", """{"description":"x"}"""));
        int offset = manifest ? 512 : 1536;
        int marker = Array.IndexOf(tar, (byte)'x', offset);
        tar[marker] = 0xff;

        (await Fails(StrictPackageFixture.Gzip(tar))).Diagnostic.Code.ShouldBe(
            manifest ? PackageExtractionError.InvalidManifest : PackageExtractionError.InvalidJson);
    }

    [Theory]
    [InlineData("""{"resourceType":"SearchParameter","text":"\uD800"}""")]
    [InlineData("""{"resourceType":"SearchParameter","text":"\uDC00"}""")]
    public async Task GivenUnpairedSurrogate_WhenStrictExtracting_ThenReportsInvalidJson(string json)
    {
        (await Fails(StrictPackageFixture.Package(("package/a.json", json)))).Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidJson);
    }

    [Theory]
    [InlineData("12 path=x\n")]
    [InlineData("0 path=x\n")]
    [InlineData("-1 path=x\n")]
    [InlineData("999999999999 path=x\n")]
    public async Task GivenMalformedPaxRecord_WhenStrictExtracting_ThenReportsInvalidArchive(string payload)
    {
        (await Fails(StrictPackageFixture.Gzip(StrictPackageFixture.Pax(payload)))).Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
    }

    [Fact]
    public async Task GivenDuplicatePaxKeys_WhenStrictExtracting_ThenDoesNotUseLastOneWins()
    {
        string payload = StrictPackageFixture.PaxAttribute("path", "package/a.json") +
            StrictPackageFixture.PaxAttribute("path", "package/b.json");

        (await Fails(StrictPackageFixture.Gzip(StrictPackageFixture.Pax(payload)))).Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
    }

    [Theory]
    [InlineData("value\n13 size=1024")]
    [InlineData("value\r13 size=1024")]
    [InlineData("value\0hidden")]
    public async Task GivenAmbiguousPaxValue_WhenStrictExtracting_ThenRejectsRecordBeforeReader(string value)
    {
        string payload = StrictPackageFixture.PaxAttribute("comment", value);

        var failure = await Fails(StrictPackageFixture.Gzip(StrictPackageFixture.Pax(payload)));

        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
        failure.Diagnostic.EntryIndex.ShouldBe(2);
    }

    [Theory]
    [InlineData("pax")]
    [InlineData("gnu")]
    public async Task GivenDanglingMetadata_WhenStrictExtracting_ThenDoesNotAcceptIncompleteEntry(string format)
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest),
            ("PaxHeaders/entry", format == "pax" ? StrictPackageFixture.PaxAttribute("path", "package/a.json") : "package/a.json\0"));
        StrictPackageFixture.SetHeader(tar, 1024, 156, 1, format == "pax" ? "x" : "L");

        (await Fails(StrictPackageFixture.Gzip(tar))).Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
    }

    [Theory]
    [InlineData("package/../evil.json")]
    [InlineData("package\\evil.json")]
    public async Task GivenGnuLongPathAlias_WhenStrictExtracting_ThenValidatesResolvedPath(string path)
    {
        byte[] tar = StrictPackageFixture.Tar(
            ("package/package.json", StrictPackageFixture.Manifest),
            ("LongLink", path + "\0"),
            ("package/a.json", "{}"));
        StrictPackageFixture.SetHeader(tar, 1024, 156, 1, "L");

        (await Fails(StrictPackageFixture.Gzip(tar))).Diagnostic.Code.ShouldBe(PackageExtractionError.UnsafePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenHiddenGnuMetadataPayload_WhenStrictExtracting_ThenEnforcesExactEntryLimit(bool terminated)
    {
        string path = "package/" + new string('a', 300) + ".json";
        string payload = terminated ? path + "\0" : path;
        (byte[] gzip, byte[] tar) = StrictWireFixture.Load(terminated ? "gnu-nul" : "gnu-unterminated");
        int size = Encoding.UTF8.GetByteCount(payload);
        Convert.ToInt32(Encoding.ASCII.GetString(tar, 1024 + 124, 12).TrimEnd('\0'), 8).ShouldBe(size);
        Encoding.UTF8.GetString(tar, 1536, size).ShouldBe(payload);

        (await Extract(gzip, new(maxEntryBytes: size))).JsonEntries.Single().Path.ShouldBe(path);
        var failure = await Fails(gzip, new(maxEntryBytes: size - 1));
        failure.Diagnostic.Code.ShouldBe(PackageExtractionError.EntrySizeLimit);
        failure.Diagnostic.EntryIndex.ShouldBe(2);
        failure.Diagnostic.Limit.ShouldBe(size - 1);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("name")]
    [InlineData("comment")]
    [InlineData("crc")]
    [InlineData("flags")]
    [InlineData("method")]
    public async Task GivenCorruptGzipEnvelope_WhenStrictExtracting_ThenFails(string corruption)
    {
        byte[] gzip = WithOptionalHeader(StrictPackageFixture.Package());
        switch (corruption)
        {
            case "length": gzip[10] = 255; gzip[11] = 255; break;
            case "name": gzip = [.. gzip[..15], .. Enumerable.Repeat((byte)'x', 30), .. new byte[8]]; break;
            case "comment": gzip = [.. gzip[..20], .. Enumerable.Repeat((byte)'x', 30), .. new byte[8]]; break;
            case "crc": gzip[28] ^= 1; break;
            case "flags": gzip[3] |= 0x20; break;
            default: gzip[2] = 7; break;
        }

        (await Fails(gzip)).Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
    }

    [Fact]
    public async Task GivenGzipOptionalHeader_WhenStrictExtracting_ThenValidatesCrcAndPreservesAccounting()
    {
        byte[] gzip = WithOptionalHeader(StrictPackageFixture.Package());

        var result = await Extract(gzip);

        result.Manifest.Name.ShouldBe("example.fhir.search");
        result.Statistics.CompressedBytes.ShouldBe(gzip.Length);
    }

    [Fact]
    public async Task GivenZeroDeflatePayload_WhenStrictExtracting_ThenFailsArchiveValidation()
    {
        (await Fails(StrictPackageFixture.Gzip([]))).Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
    }

    [Fact]
    public async Task GivenDeflateBombInIgnoredFile_WhenStrictExtracting_ThenStopsAtExpandedLimit()
    {
        byte[] gzip = StrictPackageFixture.Package(("package/ignored.bin", new string('x', 2 * 1024 * 1024)));

        gzip.Length.ShouldBeLessThan(4096);
        (await Fails(gzip, new(maxExpandedBytes: 16 * 1024))).Diagnostic.Code.ShouldBe(PackageExtractionError.ExpandedSizeLimit);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"metadata\"")]
    public async Task GivenNonObjectJson_WhenStrictExtracting_ThenRetainsItAsMetadata(string json)
    {
        var result = await Extract(StrictPackageFixture.Package(("package/metadata.json", json)));

        result.JsonEntries.Single().Json.ShouldBe(json);
        result.JsonEntries.Single().ResourceType.ShouldBeNull();
    }

    [Fact]
    public async Task GivenPrecancelledToken_WhenStrictExtracting_ThenPerformsNoReads()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var input = new StrictInputStream(StrictPackageFixture.Package());

        await Should.ThrowAsync<OperationCanceledException>(() => _extractor.ExtractStrictAsync(input, new(), cancellation.Token));

        input.BytesRead.ShouldBe(0);
    }

    private static byte[] WithOptionalHeader(byte[] gzip)
    {
        byte[] header = [.. gzip[..10], 3, 0, 1, 2, 3, .. Encoding.ASCII.GetBytes("name\0comment\0")];
        header[3] = 0x1f;
        var crc = new Crc32();
        crc.Update(header);
        var checksum = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(checksum, (ushort)crc.Value);
        return [.. header, .. checksum, .. gzip[10..]];
    }

    private Task<StrictPackageExtractionResult> Extract(byte[] gzip, PackageExtractionLimits? limits = null) =>
        _extractor.ExtractStrictAsync(new MemoryStream(gzip), limits ?? new(), CancellationToken.None);

    private Task<PackageExtractionException> Fails(byte[] gzip, PackageExtractionLimits? limits = null) =>
        Should.ThrowAsync<PackageExtractionException>(() => Extract(gzip, limits));
}
