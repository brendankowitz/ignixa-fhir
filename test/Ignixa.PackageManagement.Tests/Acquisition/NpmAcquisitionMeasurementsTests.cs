using Ignixa.PackageManagement.Models;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionMeasurementsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GivenWireFixture_WhenAcquired_ThenReportsMeasuredCompleteArchive()
    {
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = registry.Acquirer();
        AcquiredNpmPackage result = await acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        result.Extraction.Statistics.PhysicalEntries.ShouldBe(4);
        result.Extraction.Statistics.CompressedBytes.ShouldBe(registry.Tarball.Length);
        output.WriteLine($"Runtime={Environment.Version}; metadataBytes={registry.Metadata().Length}; compressedBytes={registry.Tarball.Length}; " +
            $"expandedBytes={result.Extraction.Statistics.ExpandedBytes}; payloadBytes={result.Extraction.Statistics.PayloadBytes}; " +
            $"physicalEntries={result.Extraction.Statistics.PhysicalEntries}; jsonEntries={result.Extraction.JsonEntries.Count}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenExtractionConsumesDeadline_WhenAcquired_ThenCannotReturnExpiredResult(bool overall)
    {
        using var registry = new SyntheticNpmRegistry
        {
            Tarball = StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
                ("package/package.json", SyntheticNpmRegistry.Manifest),
                ("package/ignored.bin", new string('x', 8 * 1024 * 1024))))
        };
        var clock = new ManualAcquisitionTime();
        bool armed = false;
        bool extractionWorkObserved = false;
        long allocationStart = 0;
        long bodyAllocations = 0;
        registry.Respond = (request, _, _) =>
        {
            if (request.RequestUri!.Host == "registry.test")
            {
                return Task.FromResult(registry.Bytes(registry.Metadata()));
            }
            allocationStart = GC.GetAllocatedBytesForCurrentThread();
            armed = true;
            var stream = new StrictInputStream(registry.Tarball, 65536,
                _ => bodyAllocations = GC.GetAllocatedBytesForCurrentThread() - allocationStart);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(stream) });
        };
        clock.BeforeTimestamp = () =>
        {
            // The small compressed response/digest cannot allocate this much. A2's real 8 MiB inflation
            // crosses it before the next acquisition clock checkpoint; no fake extractor or phase hook.
            if (armed && GC.GetAllocatedBytesForCurrentThread() - allocationStart > 1024 * 1024)
            {
                extractionWorkObserved = true;
                clock.BeforeTimestamp = null;
                clock.Advance(TimeSpan.FromSeconds(overall ? 4 : 2));
            }
        };
        var retry = new PackageAcquisitionRetryPolicy(maxAttempts: 1,
            attemptTimeout: TimeSpan.FromSeconds(overall ? 2 : 1), totalTimeout: TimeSpan.FromSeconds(3),
            maxDelay: TimeSpan.FromSeconds(1));
        using var acquirer = registry.Acquirer(timeProvider: clock);
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(retry: retry), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.Timeout);
        bodyAllocations.ShouldBeLessThan(1024 * 1024);
        extractionWorkObserved.ShouldBeTrue();
        registry.Requests.Count.ShouldBe(2);
        output.WriteLine($"Extraction deadline evidence: runtime={Environment.Version}; compressed={registry.Tarball.Length}; " +
            $"bodyAllocations={bodyAllocations}; observedInflation={extractionWorkObserved}; overall={overall}");
    }
}
