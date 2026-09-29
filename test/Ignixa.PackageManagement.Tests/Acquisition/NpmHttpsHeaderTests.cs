using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmHttpsHeaderTests
{
    [Theory]
    [InlineData(false, "gzip;bad")]
    [InlineData(true, "gzip;bad")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "identity,,identity")]
    [InlineData(true, "identity,")]
    public async Task GivenRealMalformedEncodingHeader_WhenMetadataOrArtifact_ThenRejectsOriginalWireValue(bool artifact, string encoding)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        byte[] tarball = SyntheticNpmRegistry.CreateTarball();
        byte[] metadata = Metadata(server, tarball);
        server.Respond = path => artifact && path.StartsWith("/npm/", StringComparison.Ordinal)
            ? new(200, metadata)
            : new(200, artifact ? tarball : metadata, Headers: $"Content-Encoding: {encoding}\r\n");
        using var acquirer = Acquirer(server);
        (await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, Policy(server), timeout.Token)))
            .Error.ShouldBe(PackageAcquisitionError.UnsupportedContentEncoding);
        server.Requests.Count.ShouldBe(artifact ? 2 : 1);
    }

    [Theory]
    [InlineData("Retry-After: 1\r\nRetry-After: 60\r\n")]
    [InlineData("Retry-After: 1\r\nRetry-After: 1\r\n")]
    [InlineData("Retry-After: 1, 60\r\n")]
    [InlineData("Retry-After: \r\n")]
    [InlineData("Retry-After: Thu, 01 Jan 1970 00:00:02 GMT\r\nRetry-After: Thu, 01 Jan 1970 00:00:03 GMT\r\n")]
    public async Task GivenRealAmbiguousRetryAfter_WhenAcquired_ThenStopsAfterOneRequest(string headers)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        server.Respond = _ => new(503, [], Headers: headers);
        using var acquirer = Acquirer(server, new ManualAcquisitionTime());
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, Policy(server), timeout.Token));
        error.Error.ShouldBe(PackageAcquisitionError.HttpFailure);
        error.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        server.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRealWholeRetryAfterDeltaOrDate_WhenAcquired_ThenHonorsDateCommaAndDoesNotRetryEarly(bool date)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        byte[] tarball = SyntheticNpmRegistry.CreateTarball();
        byte[] metadata = Metadata(server, tarball);
        server.Respond = path => server.Requests.Count == 1
            ? new(503, [], Headers: $"Retry-After: {(date ? "Thu, 01 Jan 1970 00:00:02 GMT" : "2")}\r\n")
            : new(200, path.StartsWith("/npm/", StringComparison.Ordinal) ? metadata : tarball);
        var clock = new ManualAcquisitionTime();
        using var acquirer = Acquirer(server, clock);
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, Policy(server), timeout.Token);
        (await clock.WaitForDelayAsync(TimeSpan.FromSeconds(2), task)).ShouldBe(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(1));
        server.Requests.Count.ShouldBe(1);
        task.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await task;
        server.Requests.Count.ShouldBe(3);
    }

    private static byte[] Metadata(LoopbackHttpsRegistry server, byte[] tarball) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        name = "test.pkg", version = "1.2.3",
        dist = new { tarball = new Uri(server.BaseUri, "files/test.tgz").AbsoluteUri, integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball)) }
    });

    private static NpmPackageSourcePolicy Policy(LoopbackHttpsRegistry server) =>
        new("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")]);

    private static NpmPackageAcquirer Acquirer(LoopbackHttpsRegistry server, TimeProvider? clock = null) =>
        new(new PackageExtractor(NullLogger<PackageExtractor>.Instance), timeProvider: clock,
            validateServerCertificate: (_, certificate, _, errors) => server.IsExpectedCertificate(certificate, errors));
}
