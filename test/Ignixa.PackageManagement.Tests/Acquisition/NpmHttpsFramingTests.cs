using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmHttpsFramingTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task GivenRealTruncatedResponse_WhenRetried_ThenCountsEveryRequestAndPublishesOnlyCompleteVerifiedArtifact(
        bool artifact, bool chunked, bool recover)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var directory = new OwnedCacheDirectory();
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        byte[] tarball = SyntheticNpmRegistry.CreateTarball();
        byte[] metadata = Metadata(server.BaseUri, "test.pkg", "files/test.tgz", tarball);
        int failures = 0;
        server.Respond = path =>
        {
            bool isMetadata = path.StartsWith("/npm/", StringComparison.Ordinal);
            byte[] body = isMetadata ? metadata : tarball;
            if (isMetadata != artifact && (!recover || ++failures < 3))
            {
                return new(200, body, chunked, BytesToSend: body.Length / 2, Truncate: true);
            }
            return new(200, body, chunked);
        };
        using var acquirer = Acquirer(server, new VerifiedPackageCache(directory.Path));
        var retry = new PackageAcquisitionRetryPolicy(initialDelay: TimeSpan.FromTicks(1), maxDelay: TimeSpan.FromTicks(1));
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, Policy(server, retry), timeout.Token);
        if (recover)
        {
            (await task).Integrity.Integrity.ShouldBe(Digest(tarball));
            (await File.ReadAllBytesAsync(Directory.GetFiles(directory.Path, "*.tgz").Single(), timeout.Token)).ShouldBe(tarball);
        }
        else
        {
            (await Should.ThrowAsync<PackageAcquisitionException>(() => task)).Error.ShouldBe(PackageAcquisitionError.TransportFailure);
            Directory.GetFiles(directory.Path).ShouldBeEmpty();
        }
        server.Requests.Count(r => r.Path.StartsWith("/npm/", StringComparison.Ordinal)).ShouldBe(3);
        server.Requests.Count(r => r.Path.StartsWith("/files/", StringComparison.Ordinal)).ShouldBe(artifact ? 3 : recover ? 1 : 0);
        Directory.GetFiles(directory.Path, "*.partial").ShouldBeEmpty();
        server.Diagnostics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, false, "caller")]
    [InlineData(false, true, "caller")]
    [InlineData(true, false, "caller")]
    [InlineData(true, true, "caller")]
    [InlineData(false, false, "attempt")]
    [InlineData(false, true, "attempt")]
    [InlineData(true, false, "attempt")]
    [InlineData(true, true, "attempt")]
    [InlineData(false, false, "total")]
    [InlineData(false, true, "total")]
    [InlineData(true, false, "total")]
    [InlineData(true, true, "total")]
    public async Task GivenRealStalledChunkedBody_WhenCanceledOrDeadlineExpires_ThenNeverPublishesOrCompletesBeforeTerminator(
        bool artifact, bool finalChunk, string cancellation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var caller = new CancellationTokenSource();
        using var directory = new OwnedCacheDirectory();
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        byte[] tarball = SyntheticNpmRegistry.CreateTarball();
        byte[] metadata = Metadata(server.BaseUri, "test.pkg", "files/test.tgz", tarball);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond = path =>
        {
            bool isMetadata = path.StartsWith("/npm/", StringComparison.Ordinal);
            byte[] body = isMetadata ? metadata : tarball;
            return isMetadata != artifact
                ? new(200, body, BytesToSend: finalChunk ? body.Length : body.Length / 2, Stall: true, BodySent: sent)
                : new(200, body);
        };
        var clock = new ManualAcquisitionTime();
        using var acquirer = Acquirer(server, new VerifiedPackageCache(directory.Path), clock);
        var retry = new PackageAcquisitionRetryPolicy(maxAttempts: cancellation == "total" ? 3 : 1,
            attemptTimeout: TimeSpan.FromSeconds(5), totalTimeout: TimeSpan.FromSeconds(cancellation == "total" ? 5 : 20),
            maxDelay: TimeSpan.FromSeconds(1));
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, Policy(server, retry), caller.Token);
        await sent.Task.WaitAsync(timeout.Token);
        task.IsCompleted.ShouldBeFalse();
        if (cancellation == "caller")
        {
            await caller.CancelAsync();
            (await Should.ThrowAsync<OperationCanceledException>(() => task.WaitAsync(timeout.Token))).CancellationToken.ShouldBe(caller.Token);
        }
        else
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            (await Should.ThrowAsync<PackageAcquisitionException>(() => task.WaitAsync(timeout.Token))).Error.ShouldBe(PackageAcquisitionError.Timeout);
        }
        server.Requests.Count.ShouldBe(artifact ? 2 : 1);
        Directory.GetFiles(directory.Path).ShouldBeEmpty();
        server.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOnePublicAcquirerWithConcurrentDistinctDestinations_WhenOneCallerCancels_ThenOtherDigestAuthAndCacheRemainIsolated()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var canceled = new CancellationTokenSource();
        using var directory = new OwnedCacheDirectory();
        await using var first = new LoopbackHttpsRegistry();
        await using var second = new LoopbackHttpsRegistry();
        await Task.WhenAll(first.Ready, second.Ready);
        byte[] firstTar = SyntheticNpmRegistry.CreateTarball();
        byte[] secondTar = SyntheticNpmRegistry.CreateTarball("""{"name":"second.pkg","version":"1.2.3"}""");
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Respond = path => path.StartsWith("/npm/", StringComparison.Ordinal)
            ? new(200, Metadata(first.BaseUri, "test.pkg", "files/first.tgz", firstTar))
            : new(200, firstTar, Stall: true, BodySent: sent);
        second.Respond = path => path.StartsWith("/npm/", StringComparison.Ordinal)
            ? new(200, Metadata(second.BaseUri, "second.pkg", "files/second.tgz", secondTar))
            : new(200, secondTar);
        var auth = new ConcurrentQueue<(string Source, int Port)>();
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            authenticate: (source, uri, _) =>
            {
                auth.Enqueue((source, uri.Port));
                return ValueTask.FromResult<AuthenticationHeaderValue?>(new("Bearer", source));
            },
            cache: new VerifiedPackageCache(directory.Path),
            validateServerCertificate: (_, certificate, _, errors) =>
                first.IsExpectedCertificate(certificate, errors) || second.IsExpectedCertificate(certificate, errors));
        var firstPolicy = new NpmPackageSourcePolicy("first", new Uri(first.BaseUri, "npm/"), [new Uri(first.BaseUri, "files/")]);
        var secondPolicy = new NpmPackageSourcePolicy("second", new Uri(second.BaseUri, "npm/"), [new Uri(second.BaseUri, "files/")]);
        Task<AcquiredNpmPackage> abandoned = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, firstPolicy, canceled.Token);
        await sent.Task.WaitAsync(timeout.Token);
        Task<AcquiredNpmPackage> survivor = acquirer.AcquireAsync(new("second.pkg", "1.2.3"), secondPolicy, timeout.Token);
        await canceled.CancelAsync();
        (await Should.ThrowAsync<OperationCanceledException>(() => abandoned.WaitAsync(timeout.Token))).CancellationToken.ShouldBe(canceled.Token);
        AcquiredNpmPackage result = await survivor;
        result.SourceId.ShouldBe("second");
        result.Integrity.Integrity.ShouldBe(Digest(secondTar));
        result.Extraction.Manifest.Name.ShouldBe("second.pkg");
        first.Requests.Count.ShouldBe(2);
        second.Requests.Count.ShouldBe(2);
        first.Requests.ShouldAllBe(r => r.Headers["Authorization"] == "Bearer first");
        second.Requests.ShouldAllBe(r => r.Headers["Authorization"] == "Bearer second");
        auth.ShouldAllBe(call => call.Source == "first" ? call.Port == first.BaseUri.Port : call.Source == "second" && call.Port == second.BaseUri.Port);
        (await File.ReadAllBytesAsync(Directory.GetFiles(directory.Path, "*.tgz").Single(), timeout.Token)).ShouldBe(secondTar);
        Directory.GetFiles(directory.Path, "*.partial").ShouldBeEmpty();
    }

    private static string Digest(byte[] bytes) => "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes));

    private static byte[] Metadata(Uri origin, string name, string path, byte[] tarball) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        name, version = "1.2.3", dist = new { tarball = new Uri(origin, path).AbsoluteUri, integrity = Digest(tarball) }
    });

    private static NpmPackageSourcePolicy Policy(LoopbackHttpsRegistry server, PackageAcquisitionRetryPolicy retry) =>
        new("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")], retry: retry);

    private static NpmPackageAcquirer Acquirer(LoopbackHttpsRegistry server, VerifiedPackageCache cache, TimeProvider? clock = null) =>
        new(new PackageExtractor(NullLogger<PackageExtractor>.Instance), cache: cache, timeProvider: clock,
            validateServerCertificate: (_, certificate, _, errors) => server.IsExpectedCertificate(certificate, errors));
}
