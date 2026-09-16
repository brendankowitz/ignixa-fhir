using System.Net;
using System.Net.Http.Headers;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

#pragma warning disable CA2025 // Synthetic responses transfer ownership to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionAcceptanceGuardTests
{
    [Fact]
    public void GivenDefaultSourcePolicy_WhenConstructed_ThenPinsEveryAcquisitionDefault()
    {
        NpmPackageSourcePolicy policy = SyntheticNpmRegistry.Policy();
        policy.MaxMetadataBytes.ShouldBe(1048576);
        policy.MaxRedirects.ShouldBe(0);
        policy.Retry.MaxAttempts.ShouldBe(3);
        policy.Retry.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(120));
        policy.Retry.TotalTimeout.ShouldBe(TimeSpan.FromSeconds(300));
        policy.Retry.InitialDelay.ShouldBe(TimeSpan.FromSeconds(1));
        policy.Retry.MaxDelay.ShouldBe(TimeSpan.FromSeconds(30));
        policy.ExtractionLimits.MaxCompressedBytes.ShouldBe(33554432);
        policy.ExtractionLimits.MaxExpandedBytes.ShouldBe(268435456);
        policy.ExtractionLimits.MaxEntryBytes.ShouldBe(16777216);
        policy.ExtractionLimits.MaxArchiveEntries.ShouldBe(10000);
        policy.ExtractionLimits.MaxJsonDepth.ShouldBe(64);
    }

    [Fact]
    public async Task GivenRetryAfterEqualToMaximum_WhenBudgetFits_ThenHonorsInclusiveBoundaryWithoutEarlyRetry()
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        registry.Respond = (request, count, _) =>
        {
            if (count != 1)
            {
                return Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test" ? registry.Metadata() : registry.Tarball));
            }
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        (await clock.WaitForDelayAsync(TimeSpan.FromSeconds(30))).ShouldBe(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(29));
        registry.Requests.Count.ShouldBe(1);
        task.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await task.WaitAsync(TimeSpan.FromSeconds(5));
        registry.Requests.Count.ShouldBe(3);
        registry.Bodies.ShouldAllBe(body => body.Disposed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GivenTarballTransientFailure_WhenRetried_ThenCountsMetadataAndTarballAndDisposesEachBody(bool readFailure, bool recover)
    {
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        var failingBodies = new List<ThrowingAcquisitionStream>();
        int metadataCount = 0;
        int artifactCount = 0;
        registry.Respond = (request, _, _) =>
        {
            if (request.RequestUri!.Host == "registry.test")
            {
                metadataCount++;
                return Task.FromResult(registry.Bytes(registry.Metadata()));
            }
            artifactCount++;
            if (recover && artifactCount == 3)
            {
                return Task.FromResult(registry.Bytes(registry.Tarball));
            }
            if (!readFailure)
            {
                return Task.FromResult(registry.Bytes([], HttpStatusCode.ServiceUnavailable));
            }
            var body = new ThrowingAcquisitionStream();
            failingBodies.Add(body);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        };
        using var acquirer = registry.Acquirer(timeProvider: clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        foreach (int seconds in new[] { 1, 2 })
        {
            TimeSpan delay = await clock.WaitForDelayAsync(TimeSpan.FromSeconds(seconds));
            clock.Advance(delay);
        }
        if (recover)
        {
            _ = await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            var error = await Should.ThrowAsync<PackageAcquisitionException>(() => task);
            error.Error.ShouldBe(readFailure ? PackageAcquisitionError.TransportFailure : PackageAcquisitionError.HttpFailure);
            error.ToString().ShouldNotContain("secret");
        }
        metadataCount.ShouldBe(3);
        artifactCount.ShouldBe(3);
        registry.Bodies.ShouldAllBe(body => body.Disposed);
        failingBodies.ShouldAllBe(body => body.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenConcurrentCorruptWinnerOrPublicationIoFailure_WhenAcquired_ThenFailsPermanentlyAndCleansOwnedStaging(bool directoryWinner)
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            registry, cache: new VerifiedPackageCache(directory.Path));
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        string published = Directory.GetFiles(directory.Path, "*.tgz").Single();
        File.Delete(published);
        int requestsBefore = registry.Requests.Count;
        registry.Respond = async (request, _, cancellationToken) =>
        {
            if (request.RequestUri!.Host == "registry.test")
            {
                return registry.Bytes(registry.Metadata());
            }
            if (directoryWinner)
            {
                Directory.CreateDirectory(published);
            }
            else
            {
                await File.WriteAllBytesAsync(published, "corrupt"u8.ToArray(), cancellationToken);
            }
            return registry.Bytes(registry.Tarball);
        };
        try
        {
            var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
            error.Error.ShouldBe(PackageAcquisitionError.CacheFailure);
            error.ToString().ShouldNotContain(directory.Path);
            registry.Requests.Count.ShouldBe(requestsBefore + 2);
            Directory.GetFiles(directory.Path, "*.partial").ShouldBeEmpty();
            if (!directoryWinner)
            {
                (await File.ReadAllTextAsync(published)).ShouldBe("corrupt");
            }
        }
        finally
        {
            if (directoryWinner && Directory.Exists(published))
            {
                Directory.Delete(published);
            }
        }
    }

    [Fact]
    public async Task GivenOnlySignedQueryChanges_WhenCacheHit_ThenQueryNeverEntersCacheIdentity()
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        string query = "?sig=secret%2f+%7e&x=1&x=2";
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test"
            ? registry.Metadata(artifact: SyntheticNpmRegistry.Artifact + query) : registry.Tarball));
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            registry, cache: new VerifiedPackageCache(directory.Path));
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        query = "?sig=different%2F&token=another";
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        registry.Requests.Count.ShouldBe(3);
        Path.GetFileName(Directory.GetFiles(directory.Path).Single()).ShouldMatch("^[A-F0-9]{64}\\.tgz$");
    }

    [Fact]
    public async Task GivenCleanupOnlyFailureAfterVerifiedConcurrentWinner_WhenAcquired_ThenFailsExplicitlyWithoutRetry()
    {
        using var directory = new OwnedCacheDirectory();
        using var registry = new SyntheticNpmRegistry();
        var clock = new ManualAcquisitionTime();
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            registry, timeProvider: clock, cache: new VerifiedPackageCache(directory.Path));
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        string published = Directory.GetFiles(directory.Path, "*.tgz").Single();
        File.Delete(published);
        int stagingChecks = 0;
        string? staging = null;
        clock.BeforeTimestamp = () =>
        {
            staging = Directory.GetFiles(directory.Path, "*.partial").SingleOrDefault();
            if (staging is null)
            {
                return;
            }
            if (++stagingChecks == 1)
            {
                WriteConcurrentWinner(published, registry.Tarball);
            }
            else if (stagingChecks == 3)
            {
                clock.BeforeTimestamp = null;
                File.Delete(staging);
                Directory.CreateDirectory(staging);
            }
        };
        try
        {
            var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
            error.Error.ShouldBe(PackageAcquisitionError.CacheFailure);
            error.Data[PackageAcquisitionException.CacheCleanupFailureDataKey].ShouldBeNull();
            error.ToString().ShouldNotContain(directory.Path);
            registry.Requests.Count.ShouldBe(4);
            (await File.ReadAllBytesAsync(published)).ShouldBe(registry.Tarball);
            stagingChecks.ShouldBe(3);
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
            {
                Directory.Delete(staging);
            }
        }
    }

    private static void WriteConcurrentWinner(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
}
