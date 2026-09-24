using System.Formats.Tar;
using System.Text;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class StrictContractBoundaryTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Fact]
    public void GivenDefaultLimits_WhenConstructing_ThenUsesAlignedPolicyValues()
    {
        var limits = new PackageExtractionLimits();

        limits.MaxCompressedBytes.ShouldBe(33_554_432);
        limits.MaxExpandedBytes.ShouldBe(268_435_456);
        limits.MaxEntryBytes.ShouldBe(16_777_216);
        limits.MaxArchiveEntries.ShouldBe(10_000);
        limits.MaxJsonDepth.ShouldBe(64);
    }

    [Theory]
    [InlineData(16_777_216, false)]
    [InlineData(16_777_217, true)]
    public async Task GivenEntryAtDefaultBoundary_WhenStrictExtracting_ThenEnforcesInclusiveEntryLimit(
        int payloadBytes, bool exceedsLimit)
    {
        byte[] gzip = StrictPackageFixture.Gzip(StrictPackageFixture.TarEntries(
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest),
            new UstarTarEntry(TarEntryType.RegularFile, "package/ignored.bin")
            {
                DataStream = new MemoryStream(new byte[payloadBytes])
            }));
        using var input = new MemoryStream(gzip);

        if (exceedsLimit)
        {
            var exception = await Should.ThrowAsync<PackageExtractionException>(() =>
                _extractor.ExtractStrictAsync(input, new(), CancellationToken.None));

            exception.Diagnostic.Code.ShouldBe(PackageExtractionError.EntrySizeLimit);
            exception.Diagnostic.Limit.ShouldBe(16_777_216);
            exception.Diagnostic.EntryIndex.ShouldBe(2);
        }
        else
        {
            var result = await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None);

            result.Statistics.PhysicalEntries.ShouldBe(2);
            result.Statistics.PayloadBytes.ShouldBe(16_777_216 + Encoding.UTF8.GetByteCount(StrictPackageFixture.Manifest));
        }
    }

    [Theory]
    [InlineData(10_000, false)]
    [InlineData(10_001, true)]
    public async Task GivenArchiveAtDefaultBoundary_WhenStrictExtracting_ThenEnforcesInclusivePhysicalCount(
        int physicalEntries, bool exceedsLimit)
    {
        TarEntry[] entries = [
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest),
            .. Enumerable.Range(1, physicalEntries - 1)
                .Select(index => StrictPackageFixture.File($"package/ignored-{index}.bin", string.Empty))
        ];
        byte[] gzip = StrictPackageFixture.Gzip(StrictPackageFixture.TarEntries(entries));
        using var input = new MemoryStream(gzip);

        if (exceedsLimit)
        {
            var exception = await Should.ThrowAsync<PackageExtractionException>(() =>
                _extractor.ExtractStrictAsync(input, new(), CancellationToken.None));

            exception.Diagnostic.Code.ShouldBe(PackageExtractionError.EntryCountLimit);
            exception.Diagnostic.Limit.ShouldBe(10_000);
            exception.Diagnostic.EntryIndex.ShouldBe(10_001);
        }
        else
        {
            var result = await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None);

            result.Statistics.PhysicalEntries.ShouldBe(10_000);
            result.Statistics.LogicalEntries.ShouldBe(10_000);
        }
    }

    [Theory]
    [InlineData("""{"resourceType":"SearchParameter","extra":{"nested":[]}}""")]
    [InlineData("""{"files":[{}]}""")]
    public async Task GivenDeepEntryAndShallowManifest_WhenStrictExtracting_ThenJsonDepthLimitIsIndependent(string json)
    {
        byte[] gzip = StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
            ("package/package.json", """{"name":"test","version":"1"}"""),
            ("package/deep.json", json)));
        using var atLimit = new MemoryStream(gzip);
        using var overLimit = new MemoryStream(gzip);

        (await _extractor.ExtractStrictAsync(atLimit, new(maxJsonDepth: 3), CancellationToken.None))
            .JsonEntries.Single().Json.ShouldBe(json);
        var exception = await Should.ThrowAsync<PackageExtractionException>(() =>
            _extractor.ExtractStrictAsync(overLimit, new(maxJsonDepth: 2), CancellationToken.None));

        exception.Diagnostic.Code.ShouldBe(PackageExtractionError.JsonDepthLimit);
        exception.Diagnostic.EntryIndex.ShouldBe(2);
        exception.Diagnostic.Limit.ShouldBe(2);
    }

    [Fact]
    public async Task GivenDirectoryAndIgnoredEntries_WhenStrictExtracting_ThenCountLimitIncludesBoth()
    {
        byte[] gzip = StrictPackageFixture.Gzip(StrictPackageFixture.TarEntries(
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest),
            new UstarTarEntry(TarEntryType.Directory, "package/sub/"),
            StrictPackageFixture.File("package/sub/data.bin", "ignored")));
        using var atLimit = new MemoryStream(gzip);
        using var overLimit = new MemoryStream(gzip);

        (await _extractor.ExtractStrictAsync(atLimit, new(maxArchiveEntries: 3), CancellationToken.None))
            .Statistics.PhysicalEntries.ShouldBe(3);
        var exception = await Should.ThrowAsync<PackageExtractionException>(() =>
            _extractor.ExtractStrictAsync(overLimit, new(maxArchiveEntries: 2), CancellationToken.None));

        exception.Diagnostic.Code.ShouldBe(PackageExtractionError.EntryCountLimit);
        exception.Diagnostic.EntryIndex.ShouldBe(3);
    }

    [Fact]
    public async Task GivenInvalidInputContract_WhenStrictExtracting_ThenRejectsProgrammerErrors()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _extractor.ExtractStrictAsync(null!, new(), CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => _extractor.ExtractStrictAsync(Stream.Null, null!, CancellationToken.None));
        var closed = new MemoryStream();
        await closed.DisposeAsync();
        await Should.ThrowAsync<ArgumentException>(() => _extractor.ExtractStrictAsync(closed, new(), CancellationToken.None));
    }

    [Fact]
    public async Task GivenDeeplyNestedPath_WhenStrictExtracting_ThenDoesNotMaterializeEveryAncestorPrefix()
    {
        string path = "package/" + string.Concat(Enumerable.Repeat("a/", 2000)) + "data.json";
        byte[] gzip = StrictPackageFixture.Gzip(StrictPackageFixture.TarEntries(
            StrictPackageFixture.File("package/package.json", StrictPackageFixture.Manifest),
            new GnuTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream("{}"u8.ToArray()) }));
        using var input = new MemoryStream(gzip);
        int threadId = Environment.CurrentManagedThreadId;
        long before = GC.GetAllocatedBytesForCurrentThread();

        // All I/O uses MemoryStream and completes synchronously, isolating this allocation guard.
        var result = await _extractor.ExtractStrictAsync(input, new(), CancellationToken.None);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Environment.CurrentManagedThreadId.ShouldBe(threadId, "the allocation guard requires same-thread extraction");
        result.JsonEntries.Single().Path.ShouldBe(path);
        allocated.ShouldBeLessThan(3 * 1024 * 1024);
    }
}
