using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ignixa.PackageManagement.Models;
using Shouldly;

#pragma warning disable CA2025 // Responses transfer ownership to the awaited acquirer.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionBoundaryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenDeclaredOversize_WhenAcquired_ThenRejectsBeforeBodyRead(bool metadata)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, _, _) =>
        {
            bool isMetadata = request.RequestUri!.Host == "registry.test";
            HttpResponseMessage response = registry.Bytes(isMetadata ? registry.Metadata() : registry.Tarball,
                declaredLength: isMetadata == metadata ? int.MaxValue : null);
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None))).Error.ShouldBe(
                metadata ? PackageAcquisitionError.MetadataSizeLimit : PackageAcquisitionError.CompressedSizeLimit);
        registry.Bodies.Last().BytesRead.ShouldBe(0);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
        registry.Requests.Count.ShouldBe(metadata ? 1 : 2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task GivenUnknownLengthChunkedBody_WhenAcquired_ThenAppliesInclusiveStreamingLimit(bool metadata, bool oneOver)
    {
        using var registry = new SyntheticNpmRegistry();
        int bound = (metadata ? registry.Metadata().Length : registry.Tarball.Length) - (oneOver ? 1 : 0);
        using var acquirer = registry.Acquirer();
        var policy = SyntheticNpmRegistry.Policy(
            metadataBytes: metadata ? bound : 1048576,
            limits: new PackageExtractionLimits(maxCompressedBytes: metadata ? 33554432 : bound));
        if (oneOver)
        {
            (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, policy, CancellationToken.None))).Error.ShouldBe(
                    metadata ? PackageAcquisitionError.MetadataSizeLimit : PackageAcquisitionError.CompressedSizeLimit);
        }
        else
        {
            _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, CancellationToken.None);
        }
        registry.Bodies[metadata ? 0 : 1].BytesRead.ShouldBe(bound + (oneOver ? 1 : 0));
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Theory]
    [InlineData("http://artifacts.test/files/redirect.tgz")]
    [InlineData("https://artifacts.test/files-other/redirect.tgz")]
    [InlineData("https://artifacts.test:444/files/redirect.tgz")]
    [InlineData("https://evil.test/files/redirect.tgz")]
    [InlineData("https://artifacts.test/files/%2fredirect.tgz")]
    [InlineData("../files/redirect.tgz")]
    [InlineData("https://secret@artifacts.test/files/redirect.tgz")]
    [InlineData("/files/redirect.tgz?secret=%0d")]
    public async Task GivenUntrustedRedirect_WhenAcquired_ThenRejectsBeforeRequestOrAuthentication(string destination)
    {
        using var registry = new SyntheticNpmRegistry();
        var authentication = new List<string>();
        registry.Respond = (request, _, _) =>
        {
            if (request.RequestUri!.Host == "registry.test")
            {
                return Task.FromResult(registry.Bytes(registry.Metadata()));
            }
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.Found);
            response.Headers.Location = new Uri(destination, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer((_, uri, _) =>
        {
            authentication.Add(uri.AbsoluteUri);
            return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
        });
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(1), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.UntrustedUri);
        registry.Requests.Count.ShouldBe(2);
        authentication.Count.ShouldBe(2);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Fact]
    public async Task GivenAuthorizedCrossOriginRedirect_WhenAcquired_ThenAuthenticatesFreshForEachDestinationAndDisposesRequests()
    {
        using var registry = new SyntheticNpmRegistry();
        var requestBodies = new List<StrictInputStream>();
        registry.Respond = (request, _, _) =>
        {
            var body = new StrictInputStream([]);
            requestBodies.Add(body);
            request.Content = new StreamContent(body);
            request.Headers.Contains("Cookie").ShouldBeFalse();
            HttpResponseMessage response;
            if (request.RequestUri!.AbsoluteUri == SyntheticNpmRegistry.Artifact)
            {
                response = registry.Bytes([], HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://another.test:444/files/final.tgz");
            }
            else
            {
                response = registry.Bytes(request.RequestUri.Host == "registry.test" ? registry.Metadata() : registry.Tarball);
            }
            response.Headers.Add("Set-Cookie", "secret=do-not-forward");
            return Task.FromResult(response);
        };
        var policy = new NpmPackageSourcePolicy("Source", new Uri("https://registry.test/npm/"),
            [new Uri("https://artifacts.test/files/"), new Uri("https://another.test:444/files/")], maxRedirects: 1);
        using (var acquirer = registry.Acquirer((source, uri, _) =>
        {
            source.ShouldBe("Source");
            return ValueTask.FromResult<AuthenticationHeaderValue?>(uri.Host switch
            {
                "registry.test" => new("Bearer", "registry-token"),
                "artifacts.test" => new("Bearer", "artifact-token"),
                _ => null
            });
        }))
        {
            _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, policy, CancellationToken.None);
        }
        registry.Requests.Select(r => r.Authorization).ShouldBe(["Bearer registry-token", "Bearer artifact-token", null]);
        requestBodies.ShouldAllBe(body => body.Disposed);
        registry.Bodies.ShouldAllBe(body => body.Disposed);
        registry.Disposed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/npm/redirect", true)]
    [InlineData("/outside/redirect", false)]
    [InlineData("https://artifacts.test/files/redirect", false)]
    public async Task GivenMetadataRedirect_WhenAcquired_ThenStaysWithinRegistryTrust(string destination, bool allowed)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, count, _) =>
        {
            if (count == 1)
            {
                HttpResponseMessage response = registry.Bytes([], HttpStatusCode.PermanentRedirect);
                response.Headers.Location = new Uri(destination, UriKind.RelativeOrAbsolute);
                return Task.FromResult(response);
            }
            return Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test" ? registry.Metadata() : registry.Tarball));
        };
        using var acquirer = registry.Acquirer();
        if (allowed)
        {
            _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(1), CancellationToken.None);
            registry.Requests.Count.ShouldBe(3);
        }
        else
        {
            (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(1), CancellationToken.None)))
                .Error.ShouldBe(PackageAcquisitionError.UntrustedUri);
            registry.Requests.Count.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    public async Task GivenEncodedArtifact_WhenAcquired_ThenDoesNotDecompressBeforeDigestOrBounds(string encoding)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, _, _) =>
        {
            HttpResponseMessage response = registry.Bytes(request.RequestUri!.Host == "registry.test" ? registry.Metadata() : registry.Tarball);
            if (request.RequestUri.Host != "registry.test")
            {
                response.Content.Headers.ContentEncoding.Add(encoding);
            }
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.UnsupportedContentEncoding);
        registry.Bodies.Last().BytesRead.ShouldBe(0);
        registry.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"name":"test.pkg","version":"1.2.3","dist":null}""")]
    [InlineData("""{"name":"test.pkg","version":"1.2.3","dist":{"tarball":1}}""")]
    [InlineData("""{"name":"test.pkg","version":"1.2.3","dist":{"tarball":"https://artifacts.test/files/test.tgz","tarball":"https://evil.test/"}}""")]
    public async Task GivenMalformedMetadataShape_WhenAcquired_ThenRejectsWithoutRetry(string json)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes(Encoding.UTF8.GetBytes(json)));
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.InvalidMetadata);
        registry.Requests.Count.ShouldBe(1);
    }
}
