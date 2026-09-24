using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionHttpsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTlsFixtureHandshakeFailure_WhenUnexpectedOrExplicitlyExpected_ThenRetainsOnlySafeDiagnostics(bool expected)
    {
        async Task ExerciseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new LoopbackHttpsRegistry(expectTrustRejection: expected);
            await server.Ready;
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.BaseUri.Port, timeout.Token);
            await client.GetStream().WriteAsync("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray(), timeout.Token);
            while (server.Diagnostics.Count == 0)
            {
                await Task.Delay(10, timeout.Token);
            }
            server.Diagnostics.ShouldAllBe(code => code.StartsWith("TLS_", StringComparison.Ordinal) || code.StartsWith("ERR_SSL_", StringComparison.Ordinal));
        }
        if (expected)
        {
            await ExerciseAsync();
        }
        else
        {
            var diagnostic = await Should.ThrowAsync<IOException>(ExerciseAsync);
            diagnostic.Message.ShouldStartWith("Unexpected test TLS handshake failure:");
            diagnostic.InnerException.ShouldBeNull();
        }
    }

    [Fact]
    public async Task GivenRealHttpsRegistry_WhenScopedArtifactRedirectsToAnotherOrigin_ThenReauthorizesWithoutLeakingCookiesOrAuthorization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var registry = new LoopbackHttpsRegistry();
        await using var artifacts = new LoopbackHttpsRegistry();
        await Task.WhenAll(registry.Ready, artifacts.Ready);
        byte[] tarball = SyntheticNpmRegistry.CreateTarball("""{"name":"@scope/pkg","version":"1.2.3+build.7","fhirVersion":"4.0.1"}""");
        string integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball));
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = "@scope/pkg",
            version = "1.2.3+build.7",
            dist = new { tarball = new Uri(registry.BaseUri, "files/start.tgz").AbsoluteUri, integrity }
        });
        registry.Respond = path => path.StartsWith("/npm/", StringComparison.Ordinal)
            ? new(200, metadata, Headers: "Content-Type: application/json\r\nSet-Cookie: secret=registry-only; Path=/\r\n")
            : new(302, [], Headers: $"Location: {new Uri(artifacts.BaseUri, "files/final.tgz")}\r\n");
        artifacts.Respond = _ => new(200, tarball);
        var policy = new NpmPackageSourcePolicy("local", new Uri(registry.BaseUri, "npm/"),
            [new Uri(registry.BaseUri, "files/"), new Uri(artifacts.BaseUri, "files/")],
            maxRedirects: 1);
        var authCalls = new List<Uri>();
        using var acquirer = new NpmPackageAcquirer(
            new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            authenticate: (_, uri, _) =>
            {
                authCalls.Add(uri);
                return ValueTask.FromResult<AuthenticationHeaderValue?>(uri.Port == registry.BaseUri.Port
                    ? new("Bearer", "registry-only") : null);
            },
            validateServerCertificate: (_, certificate, _, errors) =>
                registry.IsExpectedCertificate(certificate, errors) || artifacts.IsExpectedCertificate(certificate, errors));
        AcquiredNpmPackage result = await acquirer.AcquireAsync(
            new NpmPackageIdentity("@scope/pkg", "1.2.3+build.7"), policy, timeout.Token);
        result.Integrity.Integrity.ShouldBe(integrity);
        registry.Requests.Select(r => r.Path).ShouldBe(["/npm/%40scope%2Fpkg/1.2.3%2Bbuild.7", "/files/start.tgz"]);
        artifacts.Requests.Single().Path.ShouldBe("/files/final.tgz");
        registry.Requests.ShouldAllBe(r => r.Headers["Authorization"] == "Bearer registry-only");
        artifacts.Requests.Single().Headers.ContainsKey("Authorization").ShouldBeFalse();
        registry.Requests.Concat(artifacts.Requests).ShouldAllBe(r => !r.Headers.ContainsKey("Cookie"));
        authCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GivenRealHttpsRedirectWithDefaultZero_WhenAcquired_ThenNeverRequestsRedirectTarget()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        server.Respond = path => path == "/npm/redirect" ? new(404, []) :
            new(302, [], Headers: $"Location: {new Uri(server.BaseUri, "npm/redirect")}\r\n");
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            validateServerCertificate: (_, certificate, _, errors) => server.IsExpectedCertificate(certificate, errors));
        var policy = new NpmPackageSourcePolicy("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")]);
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, policy, timeout.Token));
        server.Requests.Count.ShouldBe(1);
        error.Error.ShouldBe(PackageAcquisitionError.RedirectRejected);
    }

    [Fact]
    public async Task GivenUntrustedRealCertificate_WhenUsingSafeDefaultTransport_ThenDoesNotAcceptIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = new LoopbackHttpsRegistry(expectTrustRejection: true);
        await server.Ready;
        int authCalls = 0;
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            authenticate: (_, _, _) =>
            {
                authCalls++;
                return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
            });
        var policy = new NpmPackageSourcePolicy("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")]);
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, policy, timeout.Token))).Error.ShouldBe(PackageAcquisitionError.TransportFailure);
        server.Requests.ShouldBeEmpty();
        authCalls.ShouldBe(1);
        // Some TLS implementations close without sending a server-visible alert.
        server.Diagnostics.ShouldAllBe(code => !code.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenRealEscapedSignedArtifactAndRedirect_WhenAcquired_ThenOwnedTransportPreservesExactTargets()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var registry = new LoopbackHttpsRegistry();
        await using var artifacts = new LoopbackHttpsRegistry();
        await Task.WhenAll(registry.Ready, artifacts.Ready);
        byte[] tarball = SyntheticNpmRegistry.CreateTarball();
        string integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball));
        const string initialPath = "/files/a%20b%2Bc.tgz?sig=secret%2fb%2FB+%3d&x=%7e&x=2";
        const string finalPath = "/files/%74est%2Etgz?sig=next%2f+%7e&flag&empty=&x=1&x=2";
        string initial = registry.BaseUri.GetLeftPart(UriPartial.Authority) + initialPath;
        string final = artifacts.BaseUri.GetLeftPart(UriPartial.Authority) + finalPath;
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = "test.pkg",
            version = "1.2.3",
            dist = new { tarball = initial, integrity }
        });
        registry.Respond = path => path.StartsWith("/npm/", StringComparison.Ordinal)
            ? new(200, metadata)
            : new(302, [], Headers: $"Location: {final}\r\n");
        artifacts.Respond = _ => new(200, tarball);
        var authCalls = new List<Uri>();
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            authenticate: (_, uri, _) =>
            {
                authCalls.Add(uri);
                return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
            },
            validateServerCertificate: (_, certificate, _, errors) =>
                registry.IsExpectedCertificate(certificate, errors) || artifacts.IsExpectedCertificate(certificate, errors));
        var policy = new NpmPackageSourcePolicy("local", new Uri(registry.BaseUri, "npm/"),
            [new Uri(registry.BaseUri, "files/"), new Uri(artifacts.BaseUri, "%66iles/")], maxRedirects: 1);
        _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, timeout.Token);
        registry.Requests[1].Path.ShouldBe(initialPath);
        artifacts.Requests.Single().Path.ShouldBe(finalPath);
        authCalls[1].PathAndQuery.ShouldBe(initialPath);
        authCalls[2].PathAndQuery.ShouldBe(finalPath);
        registry.Diagnostics.ShouldBeEmpty();
        artifacts.Diagnostics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRealEncodedHttpBody_WhenMetadataOrTarball_ThenRejectsBeforeAutomaticDecompression(bool artifact)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        byte[] tarball = SyntheticNpmRegistry.CreateTarball();
        string integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball));
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = "test.pkg",
            version = "1.2.3",
            dist = new { tarball = new Uri(server.BaseUri, "files/test.tgz").AbsoluteUri, integrity }
        });
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(artifact ? tarball : metadata, timeout.Token);
        }
        server.Respond = path => artifact && path.StartsWith("/npm/", StringComparison.Ordinal)
            ? new(200, metadata)
            : new(200, compressed.ToArray(), Headers: "Content-Encoding: gzip\r\n");
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            validateServerCertificate: (_, certificate, _, errors) => server.IsExpectedCertificate(certificate, errors));
        var policy = new NpmPackageSourcePolicy("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")]);
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, timeout.Token));
        error.Error.ShouldBe(PackageAcquisitionError.UnsupportedContentEncoding);
        server.Requests.Count.ShouldBe(artifact ? 2 : 1);
        server.Requests.ShouldAllBe(request => request.Headers["Accept-Encoding"] == "identity");
        server.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenRealHttpsAuthenticationChallenge_WhenNoDestinationCredentialsSelected_ThenNeverReplaysAmbientCredentials()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = new LoopbackHttpsRegistry();
        await server.Ready;
        server.Respond = _ => new(401, [], Headers: "WWW-Authenticate: Basic realm=\"synthetic-registry\"\r\n");
        using var acquirer = new NpmPackageAcquirer(new PackageExtractor(NullLogger<PackageExtractor>.Instance),
            validateServerCertificate: (_, certificate, _, errors) => server.IsExpectedCertificate(certificate, errors));
        var policy = new NpmPackageSourcePolicy("local", new Uri(server.BaseUri, "npm/"), [new Uri(server.BaseUri, "files/")]);
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, policy, timeout.Token))).StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        server.Requests.Count.ShouldBe(1);
        server.Requests.Single().Headers.ContainsKey("Authorization").ShouldBeFalse();
        server.Requests.Single().Headers.ContainsKey("Cookie").ShouldBeFalse();
    }
}
