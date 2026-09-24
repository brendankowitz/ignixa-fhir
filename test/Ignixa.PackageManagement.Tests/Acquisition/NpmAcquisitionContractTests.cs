using Ignixa.PackageManagement.Models;
using Ignixa.PackageManagement.Infrastructure;
using Shouldly;

namespace Ignixa.PackageManagement.Tests.Acquisition;

public class NpmAcquisitionContractTests
{
    [Fact]
    public void GivenPublicAcquirerConstructors_WhenSelectingTransport_ThenUnrestrictedHttpInjectionIsNotExposed()
    {
        typeof(NpmPackageAcquirer).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .ShouldNotContain(parameter => typeof(HttpMessageHandler).IsAssignableFrom(parameter.ParameterType) ||
                parameter.ParameterType == typeof(HttpClient));
    }

    [Theory]
    [InlineData(" test.pkg ", " 1.2.3 ", "test.pkg", "1.2.3")]
    [InlineData("@scope/test_pkg", "1.2.3-rc.10+build.007", "@scope/test_pkg", "1.2.3-rc.10+build.007")]
    public void GivenExactIdentity_WhenConstructed_ThenPreservesNormalizedIdentity(
        string name, string version, string expectedName, string expectedVersion)
    {
        var identity = new NpmPackageIdentity(name, version);
        identity.Name.ShouldBe(expectedName);
        identity.Version.ShouldBe(expectedVersion);
        identity.ShouldNotBe(new NpmPackageIdentity(expectedName, "1.2.3+different"));
    }

    [Theory]
    [InlineData("Package", "1.0.0")]
    [InlineData("_package", "1.0.0")]
    [InlineData("@Scope/pkg", "1.0.0")]
    [InlineData("@scope/pkg/extra", "1.0.0")]
    [InlineData("pkg%2fother", "1.0.0")]
    [InlineData("pkg", "latest")]
    [InlineData("pkg", "^1.0.0")]
    [InlineData("pkg", "1.0")]
    [InlineData("pkg", "v1.0.0")]
    [InlineData("pkg", "01.0.0")]
    [InlineData("pkg", "1.0.0-01")]
    [InlineData("pkg", "1.0.0/path")]
    [InlineData("é", "1.0.0")]
    public void GivenInvalidIdentity_WhenConstructed_ThenRejectsWithoutEchoingInput(string name, string version)
    {
        Action act = () => _ = new NpmPackageIdentity(name, version);
        Should.Throw<ArgumentException>(act).Message.ShouldNotContain($"{name}@{version}");
    }

    [Fact]
    public void GivenIdentityLengths_WhenConstructed_ThenEnforcesInclusiveBounds()
    {
        _ = new NpmPackageIdentity(new string('a', 214), "1.0.0+" + new string('a', 250));
        Should.Throw<ArgumentException>(() => new NpmPackageIdentity(new string('a', 215), "1.0.0"));
        Should.Throw<ArgumentException>(() => new NpmPackageIdentity("pkg", "1.0.0+" + new string('a', 251)));
    }

    [Theory]
    [InlineData("http://registry.test/npm/")]
    [InlineData("https://registry.test")]
    [InlineData("https://registry.test/npm")]
    [InlineData("https://user:secret@registry.test/npm/")]
    [InlineData("https://registry.test/npm/?secret")]
    [InlineData("https://registry.test/npm/#fragment")]
    [InlineData("https://registry.test/npm/%2e%2e/")]
    [InlineData("https://registry.test/npm/../")]
    [InlineData("https://registry.test/npm\\other/")]
    public void GivenAmbiguousPrefix_WhenConstructed_ThenRejects(string prefix)
    {
        Should.Throw<ArgumentException>(() => new NpmPackageSourcePolicy(
            "source", new Uri(prefix), [new Uri("https://artifact.test/files/")]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://secret")]
    [InlineData("_source")]
    [InlineData("s ource")]
    [InlineData("sourcé")]
    public void GivenInvalidSource_WhenConstructed_ThenRejects(string source)
    {
        Should.Throw<ArgumentException>(() => new NpmPackageSourcePolicy(
            source, new Uri("https://registry.test/npm/"), [new Uri("https://artifact.test/files/")]));
    }

    [Fact]
    public void GivenPolicy_WhenConstructed_ThenCapturesPrefixesAndEnforcesBudgets()
    {
        var prefixes = new List<Uri> { new("https://artifact.test/files/") };
        var policy = new NpmPackageSourcePolicy(" Source ", new Uri("https://registry.test/npm/"), prefixes);
        prefixes.Clear();
        policy.SourceId.ShouldBe("Source");
        policy.AllowedArtifactPrefixes.Count.ShouldBe(1);
        Should.Throw<ArgumentException>(() => new NpmPackageSourcePolicy("source", policy.RegistryBaseUri, []));
        Should.Throw<ArgumentException>(() => new NpmPackageSourcePolicy(new string('s', 129), policy.RegistryBaseUri, policy.AllowedArtifactPrefixes));
        Should.Throw<ArgumentOutOfRangeException>(() => new PackageAcquisitionRetryPolicy(maxAttempts: 0));
        Should.Throw<ArgumentException>(() => new PackageAcquisitionRetryPolicy(attemptTimeout: TimeSpan.FromSeconds(301)));
        Should.Throw<ArgumentException>(() => new PackageAcquisitionRetryPolicy(initialDelay: TimeSpan.FromSeconds(31)));
        Should.Throw<ArgumentOutOfRangeException>(() => new NpmPackageSourcePolicy(
            "source", policy.RegistryBaseUri, policy.AllowedArtifactPrefixes, maxMetadataBytes: 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha1-abc")]
    [InlineData("sha512-not-base64")]
    [InlineData("sha512-AA==")]
    public void GivenInvalidIntegrity_WhenConstructed_ThenRejects(string value)
    {
        Should.Throw<ArgumentException>(() => new NpmPackageIntegrity(value));
    }

    [Fact]
    public void GivenCanonicalIntegrity_WhenConstructed_ThenRejectsWhitespaceMultipleAndNoncanonicalTokens()
    {
        string canonical = "sha512-" + Convert.ToBase64String(new byte[64]);
        new NpmPackageIntegrity(canonical).Integrity.ShouldBe(canonical);
        Should.Throw<ArgumentException>(() => new NpmPackageIntegrity(" " + canonical));
        Should.Throw<ArgumentException>(() => new NpmPackageIntegrity(canonical + " " + canonical));
        // The unused low bits of the final base64 sextet must be zero.
        Should.Throw<ArgumentException>(() => new NpmPackageIntegrity(canonical[..^3] + "B=="));
    }
}
