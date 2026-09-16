using System.Buffers.Binary;
using System.Formats.Tar;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.PackageManagement.Tests;

public class StrictNumericHeaderTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100, 8, false)]
    [InlineData(108, 8, false)]
    [InlineData(116, 8, false)]
    [InlineData(136, 12, false)]
    [InlineData(100, 8, true)]
    [InlineData(108, 8, true)]
    [InlineData(116, 8, true)]
    [InlineData(136, 12, true)]
    [InlineData(345, 12, true)]
    [InlineData(357, 12, true)]
    public async Task GivenLeadingNulThenInvalidNumericDigits_WhenExtracted_ThenRejectsIdenticalWireOnEveryRuntime(int offset, int width, bool gnu)
    {
        byte[] tar = FixedTar(gnu);
        StrictPackageFixture.SetHeader(tar, 1024, offset, width, "\0" + new string('9', width - 1));
        ObserveBcl(tar);
        byte[] gzip;
        if (offset == 108 && !gnu)
        {
            (byte[] frozenGzip, byte[] frozenTar) = StrictWireFixture.Load("nul-uid");
            frozenTar.ShouldBe(tar);
            gzip = frozenGzip;
        }
        else
        {
            gzip = StrictPackageFixture.Gzip(tar);
        }
        output.WriteLine($"gzipSHA256={StrictWireFixture.Hash(gzip)}");
        using var input = new MemoryStream(gzip);
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var error = await Should.ThrowAsync<PackageExtractionException>(() =>
            extractor.ExtractStrictAsync(input, new(), CancellationToken.None));
        error.Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
        error.Diagnostic.EntryIndex.ShouldBe(2);
        error.ToString().ShouldNotContain("9999");
    }

    [Fact]
    public async Task GivenUstarPrefixAndLeadingSpaceName_WhenExtracted_ThenPreservesUnambiguousRawPath()
    {
        byte[] tar = FixedTar(gnu: false);
        StrictPackageFixture.SetHeader(tar, 1024, 0, 100, " data.json");
        StrictPackageFixture.SetHeader(tar, 1024, 345, 155, "package");
        ObserveBcl(tar);
        using var input = new MemoryStream(StrictPackageFixture.Gzip(tar));
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        StrictPackageExtractionResult result = await extractor.ExtractStrictAsync(input, new(), CancellationToken.None);
        result.JsonEntries.Single().Path.ShouldBe("package/ data.json");
    }

    public static IEnumerable<object[]> MetadataFields()
    {
        foreach (string format in new[] { "v7", "pax", "gnu-metadata", "gnu-inherited" })
        {
            foreach (int offset in new[] { 100, 108, 116, 136 })
            {
                yield return [format, offset, offset == 136 ? 12 : 8];
            }
            if (format.StartsWith("gnu", StringComparison.Ordinal))
            {
                yield return [format, 345, 12];
                yield return [format, 357, 12];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MetadataFields))]
    public async Task GivenMalformedNumericFieldIncludingHiddenOrInheritedMetadata_WhenExtracted_ThenReportsPhysicalIndex(
        string format, int offset, int width)
    {
        byte[] tar;
        int header = 1024;
        if (format == "pax")
        {
            tar = StrictPackageFixture.Pax(StrictPackageFixture.PaxAttribute("comment", "ordinary"));
        }
        else if (format.StartsWith("gnu", StringComparison.Ordinal))
        {
            tar = StrictPackageFixture.Tar(("package/package.json", StrictPackageFixture.Manifest),
                ("././@LongLink", "package/a.json\0"), ("ignored", "{}"));
            StrictPackageFixture.SetHeader(tar, 1024, 156, 1, "L");
            StrictPackageFixture.SetHeader(tar, 1024, 257, 8, "ustar  \0");
            if (format == "gnu-inherited")
            {
                header = 2048;
            }
        }
        else
        {
            tar = FixedTar(gnu: false);
            StrictPackageFixture.SetHeader(tar, 1024, 156, 1, "\0");
            StrictPackageFixture.SetHeader(tar, 1024, 257, 8, "");
        }
        StrictPackageFixture.SetHeader(tar, header, offset, width, "\0" + new string('9', width - 1));
        ObserveBcl(tar);
        using var input = new MemoryStream(StrictPackageFixture.Gzip(tar));
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var error = await Should.ThrowAsync<PackageExtractionException>(() => extractor.ExtractStrictAsync(input, new(), CancellationToken.None));
        error.Diagnostic.Code.ShouldBe(PackageExtractionError.InvalidArchive);
        error.Diagnostic.EntryIndex.ShouldBe(header / 1024 + 1);
    }

    [Theory]
    [InlineData(100, 8, 493L)]
    [InlineData(108, 8, -1L)]
    [InlineData(108, 8, -2147483648L)]
    [InlineData(108, 8, 2147483647L)]
    [InlineData(116, 8, -2147483648L)]
    [InlineData(116, 8, 2147483647L)]
    [InlineData(136, 12, -1L)]
    [InlineData(136, 12, -62135596800L)]
    [InlineData(136, 12, 253402300799L)]
    [InlineData(345, 12, -1L)]
    [InlineData(357, 12, -1L)]
    public async Task GivenSupportedBase256NumberIncludingHistoricalTimestamps_WhenExtracted_ThenAcceptsValidRange(int offset, int width, long value)
    {
        byte[] tar = FixedTar(gnu: true);
        Span<byte> field = tar.AsSpan(1024 + offset, width);
        field.Fill(value < 0 ? (byte)0xff : (byte)0);
        BinaryPrimitives.WriteInt64BigEndian(field[^8..], value);
        if (value >= 0)
        {
            field[0] = 0x80;
        }
        StrictPackageFixture.SetHeader(tar, 1024, 148, 8, "");
        using var input = new MemoryStream(StrictPackageFixture.Gzip(tar));
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        (await extractor.ExtractStrictAsync(input, new(), CancellationToken.None)).JsonEntries.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(329, 8)]
    [InlineData(337, 8)]
    [InlineData(369, 12)]
    [InlineData(381, 4)]
    [InlineData(386, 24)]
    [InlineData(482, 1)]
    [InlineData(483, 12)]
    public async Task GivenUnconsumedAuxiliaryBytesInRegularGnuEntry_WhenExtracted_ThenDoesNotInventNumericRestrictions(int offset, int width)
    {
        byte[] tar = FixedTar(gnu: true);
        StrictPackageFixture.SetHeader(tar, 1024, offset, width, new string('9', width));
        using var input = new MemoryStream(StrictPackageFixture.Gzip(tar));
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        (await extractor.ExtractStrictAsync(input, new(), CancellationToken.None)).JsonEntries.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("4")]
    public async Task GivenDeviceWithMalformedDeviceNumbers_WhenExtracted_ThenRejectsTypeBeforeReaderConsumesNumbers(string type)
    {
        byte[] tar = FixedTar(gnu: false);
        StrictPackageFixture.SetHeader(tar, 1024, 156, 1, type);
        StrictPackageFixture.SetHeader(tar, 1024, 329, 8, "\09999999");
        StrictPackageFixture.SetHeader(tar, 1024, 337, 8, "\09999999");
        using var input = new MemoryStream(StrictPackageFixture.Gzip(tar));
        var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);
        var error = await Should.ThrowAsync<PackageExtractionException>(() => extractor.ExtractStrictAsync(input, new(), CancellationToken.None));
        error.Diagnostic.Code.ShouldBe(PackageExtractionError.UnsupportedEntryType);
        error.Diagnostic.EntryIndex.ShouldBe(2);
    }

    private static byte[] FixedTar(bool gnu)
    {
        (_, byte[] tar) = StrictWireFixture.Load("nul-checksum");
        StrictPackageFixture.SetHeader(tar, 1024, 108, 8, "0000000");
        if (gnu)
        {
            StrictPackageFixture.SetHeader(tar, 1024, 257, 8, "ustar  \0");
            StrictPackageFixture.SetHeader(tar, 1024, 345, 12, "00000000000");
            StrictPackageFixture.SetHeader(tar, 1024, 357, 12, "00000000000");
        }
        return tar;
    }

    private void ObserveBcl(byte[] tar)
    {
        output.WriteLine($"Runtime={Environment.Version}; tarSHA256={StrictWireFixture.Hash(tar)}");
        using var stream = new MemoryStream(tar);
        using var reader = new TarReader(stream);
        try
        {
            while (reader.GetNextEntry() is { } entry)
            {
                output.WriteLine($"BCL name='{entry.Name}'; uid={entry.Uid}; gid={entry.Gid}; mtime={entry.ModificationTime:O}");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentException)
        {
            output.WriteLine($"BCL diagnostic={exception.GetType().Name}");
        }
    }
}
