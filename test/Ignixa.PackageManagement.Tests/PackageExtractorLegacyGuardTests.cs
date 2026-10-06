using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.PackageManagement.Tests;

public class PackageExtractorLegacyGuardTests
{
    private readonly PackageExtractor _extractor = new(NullLogger<PackageExtractor>.Instance);

    [Fact]
    public async Task GivenMalformedAndFilteredEntries_WhenLegacyExtracting_ThenGuardPreservesPermissiveSkipping()
    {
        using var package = new MemoryStream(StrictPackageFixture.Package(
            ("package/broken.json", "{"),
            ("package/patient.json", """{"resourceType":"Patient","id":"p","url":"https://example.org/p"}"""),
            ("package/no-id.json", """{"resourceType":"SearchParameter","url":"https://example.org/missing"}"""),
            ("package/valid.json", StrictPackageFixture.SearchParameter)));

        var result = await _extractor.ExtractAsync(package, CancellationToken.None);

        result.Resources.Count.ShouldBe(1);
        result.Resources[0].ResourceId.ShouldBe("test");
        result.Resources[0].FhirVersion.ShouldBe("4.0.1");
    }

    [Fact]
    public async Task GivenLateAliasedManifests_WhenLegacyExtracting_ThenGuardPreservesLastManifestAndEarlyR4Default()
    {
        using var package = new MemoryStream(StrictPackageFixture.Gzip(StrictPackageFixture.Tar(
            ("package/first.json", StrictPackageFixture.SearchParameter),
            ("nested/PACKAGE.JSON", """{"name":"old","version":"1","fhirVersion":"3.0.2"}"""),
            ("package/second.json", StrictPackageFixture.SearchParameter),
            ("elsewhere/package.json", """{"name":"last","version":"2","fhirVersions":["5.0.0"]}"""))));

        var result = await _extractor.ExtractAsync(package, CancellationToken.None);

        result.Manifest.Name.ShouldBe("last");
        result.Manifest.FhirVersion.ShouldBe("4.0.1");
        result.Resources.Select(r => r.FhirVersion).ShouldBe(["4.0.1", "3.0.2"]);
    }
}
