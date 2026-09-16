using System.Net;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionCacheTests
{
    [Fact]
    public async Task GivenVerifiedCache_WhenAcquiredAgain_ThenRevalidatesMetadataAndReturnsCompleteArtifactWithoutTarballRequest()
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = CreateAcquirer(registry, directory.Path);
        var policy = SyntheticNpmRegistry.Policy();
        AcquiredNpmPackage first = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, CancellationToken.None);
        AcquiredNpmPackage second = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, CancellationToken.None);
        registry.Requests.Select(r => r.Uri).ShouldBe([
            "https://registry.test/npm/test.pkg/1.2.3", SyntheticNpmRegistry.Artifact,
            "https://registry.test/npm/test.pkg/1.2.3"]);
        second.Extraction.Manifest.Json.ShouldBe(first.Extraction.Manifest.Json);
        second.Extraction.Manifest.FhirVersions.ShouldBe(first.Extraction.Manifest.FhirVersions);
        second.Extraction.JsonEntries.ShouldBe(first.Extraction.JsonEntries);
        Directory.GetFiles(directory.Path, "*.tgz").Length.ShouldBe(1);
        Directory.GetFiles(directory.Path, "*.partial").ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenDifferentSourceOrDigest_WhenAcquired_ThenNeverReusesAnotherCacheIdentity()
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = CreateAcquirer(registry, directory.Path);
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(source: "Source"), CancellationToken.None);
        registry.Tarball = SyntheticNpmRegistry.CreateTarball(path: "package/different-metadata.json");
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        registry.Requests.Count.ShouldBe(6);
        Directory.GetFiles(directory.Path, "*.tgz").Length.ShouldBe(3);
        Directory.GetFiles(directory.Path).ShouldAllBe(path => !System.IO.Path.GetFileName(path).Contains("test.pkg", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("compressed")]
    [InlineData("expanded")]
    [InlineData("entry")]
    [InlineData("entries")]
    [InlineData("depth")]
    public async Task GivenStricterActivePolicy_WhenCacheHit_ThenReappliesEveryExtractionLimit(string dimension)
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = CreateAcquirer(registry, directory.Path);
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        var limits = dimension switch
        {
            "compressed" => new PackageExtractionLimits(maxCompressedBytes: registry.Tarball.Length - 1),
            "expanded" => new PackageExtractionLimits(maxExpandedBytes: 512),
            "entry" => new PackageExtractionLimits(maxEntryBytes: 2),
            "entries" => new PackageExtractionLimits(maxArchiveEntries: 1),
            _ => new PackageExtractionLimits(maxJsonDepth: 1)
        };
        if (dimension == "compressed")
        {
            (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(limits: limits), CancellationToken.None)))
                .Error.ShouldBe(PackageAcquisitionError.CompressedSizeLimit);
        }
        else if (dimension == "depth")
        {
            // Metadata has dist nesting too; it must fail before cache use under this policy.
            (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(limits: limits), CancellationToken.None)))
                .Error.ShouldBe(PackageAcquisitionError.InvalidMetadata);
        }
        else
        {
            _ = await Should.ThrowAsync<PackageExtractionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(limits: limits), CancellationToken.None));
        }
        registry.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GivenCorruptedCache_WhenAcquired_ThenFailsExplicitlyWithoutRedownload()
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = CreateAcquirer(registry, directory.Path);
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        string cached = Directory.GetFiles(directory.Path, "*.tgz").Single();
        byte[] corrupted = registry.Tarball.ToArray();
        corrupted[^1] ^= 1;
        await File.WriteAllBytesAsync(cached, corrupted);
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.DigestMismatch);
        registry.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GivenCacheIoFailure_WhenAcquired_ThenFailsExplicitlyAndDoesNotRetry()
    {
        using var directory = new OwnedCacheDirectory();
        string file = System.IO.Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(file, "occupied");
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = CreateAcquirer(registry, file);
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
        error.Error.ShouldBe(PackageAcquisitionError.CacheFailure);
        error.ToString().ShouldNotContain(file);
        registry.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenUnverifiedArtifact_WhenAcquired_ThenNeverPublishesCache(bool digestMismatch)
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry { Tarball = "invalid archive"u8.ToArray() };
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test"
            ? registry.Metadata(integrity: digestMismatch ? "sha512-" + Convert.ToBase64String(new byte[64]) : null)
            : registry.Tarball));
        using var acquirer = CreateAcquirer(registry, directory.Path);
        _ = await Should.ThrowAsync<Exception>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
        Directory.GetFiles(directory.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenParallelAcquirers_WhenPublishingSameKey_ThenOnlyCompleteVerifiedBytesAreVisible()
    {
        using var directory = new OwnedCacheDirectory();
        var cache = new VerifiedPackageCache(directory.Path);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reached = 0;
        Task<AcquiredNpmPackage>[] tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var registry = new SyntheticNpmRegistry();
            registry.Respond = async (request, _, cancellationToken) =>
            {
                if (request.RequestUri!.Host != "registry.test")
                {
                    if (Interlocked.Increment(ref reached) == 8)
                    {
                        ready.TrySetResult();
                    }
                    await ready.Task.WaitAsync(cancellationToken);
                }
                return registry.Bytes(request.RequestUri.Host == "registry.test" ? registry.Metadata() : registry.Tarball);
            };
            using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance), registry, cache: cache);
            return await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        }).ToArray();
        AcquiredNpmPackage[] results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        results.ShouldAllBe(result => result.Extraction.JsonEntries.Count == 3);
        Directory.GetFiles(directory.Path, "*.partial").ShouldBeEmpty();
        string file = Directory.GetFiles(directory.Path, "*.tgz").Single();
        (await File.ReadAllBytesAsync(file)).ShouldBe(SyntheticNpmRegistry.CreateTarball());
    }

    [Fact]
    public async Task GivenCachePublicationConsumesDeadline_WhenAcquired_ThenTimesOutAndCleansOnlyOwnedStaging()
    {
        using var directory = new OwnedCacheDirectory();
        string unrelated = System.IO.Path.Combine(directory.Path, "another-owner.partial");
        await File.WriteAllTextAsync(unrelated, "preserve");
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        clock.BeforeTimestamp = () =>
        {
            if (Directory.GetFiles(directory.Path, "*.partial").Length > 1)
            {
                clock.BeforeTimestamp = null;
                clock.Advance(TimeSpan.FromSeconds(301));
            }
        };
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            registry, timeProvider: clock, cache: new VerifiedPackageCache(directory.Path));
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.Timeout);
        registry.Requests.Count.ShouldBe(2);
        Directory.GetFiles(directory.Path).ShouldBe([unrelated]);
        (await File.ReadAllTextAsync(unrelated)).ShouldBe("preserve");
    }

    [Fact]
    public async Task GivenCachedDeepJson_WhenActiveDepthDecreases_ThenStrictExtractionRejectsIt()
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry
        {
            Tarball = StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
                ("package/package.json", SyntheticNpmRegistry.Manifest),
                ("package/deep.json", """{"nested":{"deeper":{"value":1}}}""")))
        };
        using var acquirer = CreateAcquirer(registry, directory.Path);
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        (await Should.ThrowAsync<PackageExtractionException>(() => acquirer.AcquireAsync(SyntheticNpmRegistry.Identity,
            SyntheticNpmRegistry.Policy(limits: new PackageExtractionLimits(maxJsonDepth: 2)), CancellationToken.None)))
            .Diagnostic.Code.ShouldBe(PackageExtractionError.JsonDepthLimit);
        registry.Requests.Count.ShouldBe(3);
    }

    private static NpmPackageAcquirer CreateAcquirer(SyntheticNpmRegistry registry, string path) =>
        new(new PackageExtractor(NullLogger<PackageExtractor>.Instance), registry, cache: new VerifiedPackageCache(path));
}
