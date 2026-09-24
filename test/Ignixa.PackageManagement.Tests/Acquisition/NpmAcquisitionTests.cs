using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ignixa.PackageManagement.Models;
using Shouldly;

#pragma warning disable CA2025 // Synthetic responses transfer ownership to the awaited acquirer, not the registry closure.

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionTests
{
    [Fact]
    public async Task GivenSelectedVersion_WhenAcquired_ThenVerifiesAndPreservesEveryRawEntry()
    {
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = registry.Acquirer();
        AcquiredNpmPackage result = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None);

        registry.Requests.Select(r => r.Uri).ShouldBe([
            "https://registry.test/npm/test.pkg/1.2.3", SyntheticNpmRegistry.Artifact]);
        result.Identity.ShouldBe(SyntheticNpmRegistry.Identity);
        result.SourceId.ShouldBe("source");
        result.Integrity.Integrity.ShouldBe(registry.Integrity);
        result.Extraction.Manifest.Json.ShouldBe(SyntheticNpmRegistry.Manifest);
        result.Extraction.Manifest.FhirVersions.ShouldBe(["4.0.1", "3.0.2"]);
        result.Extraction.JsonEntries.Select(e => e.Path).ShouldBe([
            "package/Patient.json", "package/.index.json", "package/custom-metadata.json"]);
        result.Extraction.JsonEntries.Count(e => e.ResourceType is null).ShouldBe(2);
        result.Extraction.JsonEntries.ShouldBe([
            new StrictPackageEntry("package/Patient.json", """{"resourceType":"Patient","id":"p"}""", "Patient"),
            new StrictPackageEntry("package/.index.json", """{"index-version":1}""", null),
            new StrictPackageEntry("package/custom-metadata.json", """{"custom":"preserved"}""", null)]);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
        registry.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenScopedBuildIdentity_WhenAcquired_ThenUsesSingleEscapedIdentifierUnderRegistryPrefix()
    {
        using var registry = new SyntheticNpmRegistry();
        var identity = new NpmPackageIdentity("@scope/test_pkg", "1.2.3-rc.1+build.7");
        registry.Tarball = SyntheticNpmRegistry.CreateTarball(
            """{"name":"@scope/test_pkg","version":"1.2.3-rc.1+build.7","fhirVersion":"4.0.1"}""");
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(
            request.RequestUri!.Host == "registry.test" ? registry.Metadata(identity.Name, identity.Version) : registry.Tarball));
        using var acquirer = registry.Acquirer();
        (await acquirer.AcquireAsync(identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)).Identity.ShouldBe(identity);
        registry.Requests[0].Uri.ShouldBe("https://registry.test/npm/%40scope%2Ftest_pkg/1.2.3-rc.1%2Bbuild.7");
    }

    [Theory]
    [InlineData("name", PackageAcquisitionError.MetadataIdentityMismatch)]
    [InlineData("version", PackageAcquisitionError.MetadataIdentityMismatch)]
    [InlineData("json", PackageAcquisitionError.InvalidMetadata)]
    [InlineData("missing", PackageAcquisitionError.InvalidMetadata)]
    [InlineData("duplicate", PackageAcquisitionError.InvalidMetadata)]
    [InlineData("encoding", PackageAcquisitionError.UnsupportedContentEncoding)]
    public async Task GivenInvalidMetadata_WhenAcquired_ThenFailsOnceWithoutDownloading(string failure, PackageAcquisitionError code)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) =>
        {
            byte[] bytes = failure switch
            {
                "name" => registry.Metadata(name: "other"),
                "version" => registry.Metadata(version: "1.2.3+other"),
                "json" => "{secret"u8.ToArray(),
                "missing" => "{}"u8.ToArray(),
                "duplicate" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(registry.Metadata()).Replace(
                    "\"name\":", "\"name\":\"test.pkg\",\"name\":", StringComparison.Ordinal)),
                _ => registry.Metadata()
            };
            HttpResponseMessage response = registry.Bytes(bytes);
            if (failure == "encoding")
            {
                response.Content.Headers.ContentEncoding.Add("gzip");
            }
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer();
        var error = await Should.ThrowAsync<PackageAcquisitionException>(() =>
            acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
        error.Error.ShouldBe(code);
        error.InnerException.ShouldBeNull();
        registry.Requests.Count.ShouldBe(1);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Theory]
    [InlineData("http://artifacts.test/files/test.tgz")]
    [InlineData("https://evil.test/files/test.tgz")]
    [InlineData("https://artifacts.test:444/files/test.tgz")]
    [InlineData("https://artifacts.test/files-other/test.tgz")]
    [InlineData("https://artifacts.test/files/%2e%2e/test.tgz")]
    [InlineData("https://artifacts.test/files/../test.tgz")]
    [InlineData("https://artifacts.test/files/a%2fb.tgz")]
    [InlineData("https://artifacts.test/files/a%252fb.tgz")]
    [InlineData("https://artifacts.test/files/test.tgz?secret=%GG")]
    [InlineData("https://secret@artifacts.test/files/test.tgz")]
    [InlineData("https://artifacts.test/files/test.tgz#fragment")]
    [InlineData("https://artifacts.test/files\\test.tgz")]
    public async Task GivenUntrustedArtifact_WhenAcquired_ThenNeverRequestsIt(string artifact)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) => Task.FromResult(registry.Bytes(registry.Metadata(artifact: artifact)));
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.UntrustedUri);
        registry.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("", PackageAcquisitionError.InvalidIntegrity)]
    [InlineData("sha1-abcd", PackageAcquisitionError.InvalidIntegrity)]
    [InlineData("sha512-AA==", PackageAcquisitionError.InvalidIntegrity)]
    [InlineData("absent", PackageAcquisitionError.MissingIntegrity)]
    [InlineData("multiple", PackageAcquisitionError.InvalidIntegrity)]
    public async Task GivenInvalidDigestMetadata_WhenAcquired_ThenFailsBeforeArtifact(string integrity, PackageAcquisitionError code)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (_, _, _) =>
        {
            byte[] metadata = integrity == "absent"
                ? Encoding.UTF8.GetBytes($$$"""{"name":"test.pkg","version":"1.2.3","dist":{"tarball":"{{{SyntheticNpmRegistry.Artifact}}}","shasum":"abc"}}""")
                : registry.Metadata(integrity: integrity == "multiple" ? $"{registry.Integrity} {registry.Integrity}" : integrity);
            return Task.FromResult(registry.Bytes(metadata));
        };
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None))).Error.ShouldBe(code);
        registry.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAdministratorPin_WhenMetadataAgreesOrOmitsIntegrity_ThenVerifies(bool omitIntegrity)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test"
            ? omitIntegrity ? Encoding.UTF8.GetBytes($$$"""{"name":"test.pkg","version":"1.2.3","dist":{"tarball":"{{{SyntheticNpmRegistry.Artifact}}}"}}""") : registry.Metadata()
            : registry.Tarball));
        using var acquirer = registry.Acquirer();
        (await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(),
            CancellationToken.None, new NpmPackageIntegrity(registry.Integrity))).Integrity.Integrity.ShouldBe(registry.Integrity);
    }

    [Fact]
    public async Task GivenConflictingPin_WhenAcquired_ThenFailsWithoutDownloading()
    {
        using var registry = new SyntheticNpmRegistry();
        using var acquirer = registry.Acquirer();
        var pin = new NpmPackageIntegrity("sha512-" + Convert.ToBase64String(new byte[64]));
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None, pin)))
            .Error.ShouldBe(PackageAcquisitionError.IntegrityConflict);
        registry.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenDigestMismatch_WhenAcquired_ThenNeverExtractsOrRetries()
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(request.RequestUri!.Host == "registry.test"
            ? registry.Metadata(integrity: "sha512-" + Convert.ToBase64String(new byte[64])) : "not even a gzip archive"u8.ToArray()));
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.DigestMismatch);
        registry.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenPermanentExtractionIOException_WhenAcquired_ThenMakesOnlyOneMetadataAndTarballAttempt(bool unsafePath)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Tarball = unsafePath ? SyntheticNpmRegistry.CreateTarball(path: "../unsafe.json") : "invalid gzip"u8.ToArray();
        using var acquirer = registry.Acquirer();
        PackageExtractionException error = await Should.ThrowAsync<PackageExtractionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None));
        error.ShouldBeAssignableTo<IOException>();
        registry.Requests.Count.ShouldBe(2);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Fact]
    public async Task GivenDifferentStrictManifestIdentity_WhenAcquired_ThenRejectsOnce()
    {
        using var registry = new SyntheticNpmRegistry
        {
            Tarball = SyntheticNpmRegistry.CreateTarball("""{"name":"test.pkg","version":"1.2.3+other","fhirVersion":"4.0.1"}""")
        };
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.ManifestIdentityMismatch);
        registry.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task GivenStreamedInclusiveBounds_WhenAcquired_ThenCountsActualBytes(bool metadata, bool oneOver)
    {
        using var registry = new SyntheticNpmRegistry();
        int max = (metadata ? registry.Metadata().Length : registry.Tarball.Length) - (oneOver ? 1 : 0);
        registry.Respond = (request, _, _) => Task.FromResult(registry.Bytes(
            request.RequestUri!.Host == "registry.test" ? registry.Metadata() : registry.Tarball, declaredLength: 1));
        var policy = SyntheticNpmRegistry.Policy(
            metadataBytes: metadata ? max : 1048576,
            limits: new PackageExtractionLimits(maxCompressedBytes: metadata ? 33554432 : max));
        using var acquirer = registry.Acquirer();
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
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    public async Task GivenRedirectPolicy_WhenAcquired_ThenChecksEachHopAndUsesFreshAuthentication(int allowed, int actual, bool succeeds)
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, _, _) =>
        {
            if (request.RequestUri!.AbsoluteUri == SyntheticNpmRegistry.Artifact ||
                (actual == 2 && request.RequestUri.AbsolutePath == "/files/redirect.tgz"))
            {
                HttpResponseMessage redirect = registry.Bytes([], HttpStatusCode.Found);
                redirect.Headers.Location = new Uri(request.RequestUri.AbsoluteUri == SyntheticNpmRegistry.Artifact
                    ? "https://artifacts.test/files/redirect.tgz" : "https://other.test/files/final.tgz");
                return Task.FromResult(redirect);
            }
            return Task.FromResult(registry.Bytes(request.RequestUri.Host == "registry.test" ? registry.Metadata() : registry.Tarball));
        };
        using var acquirer = registry.Acquirer((source, uri, _) =>
        {
            source.ShouldBe("source");
            return ValueTask.FromResult<AuthenticationHeaderValue?>(uri.Host == "registry.test"
                ? new("Bearer", "registry-only") : null);
        });
        if (succeeds)
        {
            _ = await acquirer.AcquireAsync(SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(allowed), CancellationToken.None);
        }
        else
        {
            (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
                SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(allowed), CancellationToken.None)))
                .Error.ShouldBe(PackageAcquisitionError.RedirectRejected);
        }
        registry.Requests[0].Authorization.ShouldBe("Bearer registry-only");
        registry.Requests.Skip(1).ShouldAllBe(r => r.Authorization == null);
        registry.Requests.Count.ShouldBe(allowed == 0 ? 2 : 3);
        registry.Bodies.ShouldAllBe(s => s.Disposed);
    }

    [Fact]
    public async Task GivenRedirectCycle_WhenAcquired_ThenFailsBeforeRepeatingRequest()
    {
        using var registry = new SyntheticNpmRegistry();
        registry.Respond = (request, _, _) =>
        {
            HttpResponseMessage response = registry.Bytes([], HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = request.RequestUri;
            return Task.FromResult(response);
        };
        using var acquirer = registry.Acquirer();
        (await Should.ThrowAsync<PackageAcquisitionException>(() => acquirer.AcquireAsync(
            SyntheticNpmRegistry.Identity, SyntheticNpmRegistry.Policy(3), CancellationToken.None)))
            .Error.ShouldBe(PackageAcquisitionError.RedirectRejected);
        registry.Requests.Count.ShouldBe(1);
    }
}
