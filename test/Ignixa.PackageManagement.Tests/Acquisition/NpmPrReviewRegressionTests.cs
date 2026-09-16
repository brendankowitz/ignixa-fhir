using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit.Abstractions;

#pragma warning disable CA2025 // Synthetic responses transfer to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmPrReviewRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("gzip;bad")]
    [InlineData("")]
    [InlineData("identity,")]
    [InlineData("identity;bad")]
    [InlineData("identity,,identity")]
    public async Task GivenMalformedRawContentEncoding_WhenMetadataOrTarball_ThenRejectsBeforeReading(string encoding)
    {
        foreach (bool artifact in new[] { false, true })
        {
            using var registry = new SyntheticNpmRegistry();
            registry.Respond = (request, _, _) =>
            {
                bool metadata = request.RequestUri!.Host == "registry.test";
                HttpResponseMessage response = registry.Bytes(metadata ? registry.Metadata() : registry.Tarball);
                if (artifact != metadata)
                {
                    response.Content.Headers.TryAddWithoutValidation("Content-Encoding", encoding);
                    output.WriteLine($"Runtime={Environment.Version}; raw='{encoding}' (typed parsing deliberately not invoked)");
                }
                return Task.FromResult(response);
            };
            using var acquirer = registry.Acquirer();
            var error = await Should.ThrowAsync<PackageAcquisitionException>(() =>
                acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
            error.Error.ShouldBe(PackageAcquisitionError.UnsupportedContentEncoding);
            registry.Requests.Count.ShouldBe(artifact ? 2 : 1);
            registry.Bodies.ShouldAllBe(body => body.Disposed);
        }
    }

    [Theory]
    [InlineData("1", "60")]
    [InlineData("1", "1")]
    [InlineData("1", "invalid-secret")]
    public async Task GivenDuplicateRawRetryAfter_WhenAcquired_ThenStopsWithoutEarlyRetry(string first, string second)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) =>
        {
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.ServiceUnavailable);
            response.Headers.TryAddWithoutValidation("Retry-After", new[] { first, second });
            int rawCount = response.Headers.NonValidated["Retry-After"].Count;
            output.WriteLine($"Runtime={Environment.Version}; typed={response.Headers.RetryAfter}; rawCount={rawCount}");
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer(timeProvider: new ManualAcquisitionTime());
        Task<AcquiredNpmPackage> task = acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);
        task.IsCompleted.ShouldBeTrue();
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => task);
        error.Error.ShouldBe(PackageAcquisitionError.HttpFailure);
        error.ToString().ShouldNotContain("secret");
        registry.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("crypto")]
    [InlineData("programmer")]
    [InlineData("unrelated-cancellation")]
    public async Task GivenThrowingTlsValidator_WhenUsingPublicTransport_ThenIsSanitizedPermanentFailure(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new LoopbackHttpsRegistry(expectTrustRejection: true);
        await server.Ready;
        int authCalls = 0;
        int validationCalls = 0;
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            authenticate: (_, _, _) =>
            {
                authCalls++;
                return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
            },
            validateServerCertificate: (_, _, _, _) =>
            {
                validationCalls++;
                throw failure switch
                {
                    "crypto" => new CryptographicException("secret trust callback"),
                    "programmer" => new InvalidOperationException("secret trust callback"),
                    _ => new OperationCanceledException("secret unrelated cancellation")
                };
            });
        var policy = new NpmPackageSourcePolicy("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")]);
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, timeout.Token));
        output.WriteLine($"Runtime={Environment.Version}; callback={failure}; authCalls={authCalls}; validationCalls={validationCalls}");
        error.Error.ShouldBe(PackageAcquisitionError.TransportFailure);
        error.ToString().ShouldNotContain("secret");
        error.InnerException.ShouldBeNull();
        authCalls.ShouldBe(1);
        validationCalls.ShouldBe(1);
        server.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenCallerOrDeadlineCancellationInsideThrowingTlsValidator_WhenTransportUnwinds_ThenPreservesActualCancellation(bool deadline)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var clock = new ManualAcquisitionTime();
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(deadline ? 60 : 5), clock);
        await using var server = new LoopbackHttpsRegistry(expectTrustRejection: true);
        await server.Ready;
        int validations = 0;
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            timeProvider: clock,
            validateServerCertificate: (_, _, _, _) =>
            {
                validations++;
                clock.Advance(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("secret trust callback");
            });
        var retry = new PackageAcquisitionRetryPolicy(attemptTimeout: TimeSpan.FromSeconds(deadline ? 5 : 20),
            totalTimeout: TimeSpan.FromSeconds(deadline ? 5 : 20), maxDelay: TimeSpan.FromSeconds(1));
        var policy = new NpmPackageSourcePolicy("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")], retry: retry);
        Exception? failure = await Record.ExceptionAsync(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, caller.Token).WaitAsync(timeout.Token));
        if (deadline)
        {
            failure.ShouldBeOfType<PackageAcquisitionException>().Error.ShouldBe(PackageAcquisitionError.Timeout);
        }
        else
        {
            Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken.ShouldBe(caller.Token);
        }
        failure!.ToString().ShouldNotContain("secret");
        validations.ShouldBe(1);
        server.Requests.ShouldBeEmpty();
    }
}
