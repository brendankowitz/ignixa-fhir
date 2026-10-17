// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Npm;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Package acquisition (stub registry, never the network), the package store and the package reader.
/// New behavior (Task 7): 24779ff has no package acquisition (verified: the file does not compile there).
/// </summary>
public class TerminologyPackageTests
{
#pragma warning disable CA2000 // Each registry client owns its stub handler and lives as long as its session.

    [Fact]
    public async Task GivenAPackageNotYetStored_WhenLoaded_ThenItIsDownloadedFromPackagesFhirOrgOnlyAndStoredForOfflineUse()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        StubHttpHandler registry = Registry(Archive(Files()));

        JsonNode first = Json(await TermList.ListAsync(Open(folder, Online(registry, store)), null, null, null, null, 0, false, CancellationToken.None));
        JsonNode offline = Json(await TermList.ListAsync(Open(folder, Offline(store)), null, null, null, null, 0, false, CancellationToken.None));

        ((string)first["facts"]!["sources"]![0]!["origin"]!).ShouldBe("downloaded");
        registry.Requests.Select(uri => uri.AbsoluteUri).ShouldBe([$"https://packages.fhir.org/{PackageId}", $"https://packages.fhir.org/{PackageId}/{PackageVersion}"]);
        File.Exists(Path.Combine(store.Path, "packages", $"{PackageId}#{PackageVersion}.tgz")).ShouldBeTrue();
        ((string)offline["facts"]!["sources"]![0]!["origin"]!).ShouldBe("package-store");
        ((int)offline["facts"]!["sources"]![0]!["codeSystems"]!).ShouldBe(2);
        ((int)offline["facts"]!["sources"]![0]!["codeSystemsWithoutConcepts"]!).ShouldBe(1);
        ((int)offline["facts"]!["sources"]![0]!["valueSets"]!).ShouldBe(8);
        ((int)offline["facts"]!["sources"]![0]!["conceptMaps"]!).ShouldBe(1);
        offline["facts"]!["sources"]![0]!["fhirVersions"]!.AsArray().Select(version => (string)version!).ShouldBe(["4.0.1"]);
        ((bool)offline["facts"]!["offline"]!).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAPackageWithEntriesOutsidePackageAndInSubfolders_WhenRead_ThenOnlyTopLevelTerminologyIsLoadedAndTheRestCounted()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermList.ListAsync(OpenWithPackage(folder, store), null, null, "ignored", null, 0, false, CancellationToken.None));

        JsonNode files = result["facts"]!["sources"]![0]!["files"]!;
        ((int)result["facts"]!["total"]!).ShouldBe(0);
        ((int)files["otherResources"]!).ShouldBe(1);
        ((int)files["filesIgnored"]!).ShouldBe(3);
        ((int)files["unusable"]!).ShouldBe(0);
    }

    [Fact]
    public async Task GivenABareId_WhenLoaded_ThenTheRegistryLatestVersionIsUsedAndTheFactsSaySo()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        TerminologyOptions options = Online(Registry(Archive(Files())), store) with { Packages = [] };

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, options), PackageId, CancellationToken.None));

        ((string)result["facts"]!["package"]!).ShouldBe($"{PackageId}@{PackageVersion}");
        ((string)result["facts"]!["versionFrom"]!).ShouldBe("registry-latest");
        ((bool)result["facts"]!["alreadyLoaded"]!).ShouldBeFalse();
        result["facts"]!["codeSystems"]!.AsArray().Select(item => (string)item!["url"]!).ShouldBe([Body, Partial, Snomed]);
    }

    [Fact]
    public async Task GivenAPackageAlreadyLoaded_WhenLoadedAgain_ThenNothingIsReadAndTheFactsSaySo()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        StubHttpHandler registry = Registry(Archive(Files()));
        FolderSession session = Open(folder, Online(registry, store));

        JsonNode result = Json(await TermLoad.LoadAsync(session, $"{PackageId}@{PackageVersion}", CancellationToken.None));

        ((bool)result["facts"]!["alreadyLoaded"]!).ShouldBeTrue();
        registry.Requests.Count.ShouldBe(2);
        ((string)result["issues"]![0]!["code"]!).ShouldBe("package-already-loaded");
    }

    [Fact]
    public async Task GivenOfflineAndNothingStored_WhenAPackageIsLoaded_ThenTheCallFailsWithoutTouchingTheNetwork()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, Offline(store, "hl7.fhir.r4.core@4.0.1") with { Packages = [] }), "other.package@2.0.0", CancellationToken.None));

        result["facts"].ShouldBeNull();
        ((string)result["issues"]![0]!["code"]!).ShouldBe("package-unavailable-offline");
    }

    [Fact]
    public async Task GivenOfflineWithABareId_WhenLoaded_ThenTheStoredVersionsAreNamed()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        _ = Json(await TermList.ListAsync(OpenWithPackage(folder, store), null, null, null, null, 0, false, CancellationToken.None));

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, Offline(store)), PackageId, CancellationToken.None));

        ((string)result["issues"]![0]!["code"]!).ShouldBe("package-unavailable-offline");
        ((string)result["issues"]![0]!["diagnostics"]!).ShouldContain($"{PackageId}@{PackageVersion}");
    }

    [Fact]
    public async Task GivenNothingCanBeLoaded_WhenATerminologyToolIsCalled_ThenItFailsWithTheLoadProblemsInsteadOfAnsweringNotLoaded()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = Json(await TermLookup.LookupAsync(Open(folder, Offline(store)), Body, "heart", null, CancellationToken.None));

        result["facts"].ShouldBeNull();
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldBe(["terminology-not-loaded", "package-unavailable-offline"]);
    }

    [Fact]
    public async Task GivenAnArchiveThatDoesNotMatchThePublishedShasum_WhenDownloaded_ThenItIsNeitherLoadedNorStored()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        StubHttpHandler registry = Registry(Archive(Files()), shasum: new string('0', 40));

        JsonNode result = Json(await TermLookup.LookupAsync(Open(folder, Online(registry, store)), Body, "heart", null, CancellationToken.None));

        result["facts"].ShouldBeNull();
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain(FhirPackageRegistry.DownloadFailedCode);
        Directory.Exists(Path.Combine(store.Path, "packages")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, FhirPackageRegistry.NotFoundCode)]
    [InlineData(HttpStatusCode.Found, FhirPackageRegistry.DownloadFailedCode)]
    [InlineData(HttpStatusCode.ServiceUnavailable, FhirPackageRegistry.DownloadFailedCode)]
    public async Task GivenARegistryFailure_WhenAPackageIsLoaded_ThenTheFailureIsReportedAndNoRedirectIsFollowed(HttpStatusCode status, string code)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        var registry = new StubHttpHandler(_ =>
        {
            HttpResponseMessage response = StubHttpHandler.Status(status);
            response.Headers.Location = new Uri("https://elsewhere.example/package.tgz");
            return response;
        });

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, Online(registry, store) with { Packages = [] }), "some.package@1.0.0", CancellationToken.None));

        ((string)result["issues"]![0]!["code"]!).ShouldBe(code);
        registry.Requests.ShouldAllBe(uri => uri.Host == "packages.fhir.org");
    }

    [Fact]
    public async Task GivenAPackageWhoseManifestNamesAnotherPackage_WhenLoaded_ThenItIsRefused()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        List<(string Path, string Content)> files = [.. Files()];
        files[0] = ("package/package.json", """{"name":"evil.package","version":"1.0.0"}""");

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, Online(Registry(Archive(files)), store) with { Packages = [] }), $"{PackageId}@{PackageVersion}", CancellationToken.None));

        ((string)result["issues"]![0]!["code"]!).ShouldBe(PackageAcquisition.IdentityMismatchCode);
    }

    [Fact]
    public async Task GivenAStoredArchiveChangedOnDisk_WhenLoadedOffline_ThenTheCorruptionIsReportedNotReadPast()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        _ = await TermList.ListAsync(OpenWithPackage(folder, store), null, null, null, null, 0, false, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(store.Path, "packages", $"{PackageId}#{PackageVersion}.tgz"), Archive(Files(BodyCodeSystem(heartDisplay: "Tampered"))));

        JsonNode result = Json(await TermLookup.LookupAsync(Open(folder, Offline(store)), Body, "heart", null, CancellationToken.None));

        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain(PackageStore.CorruptCode);
    }

    [Fact]
    public async Task GivenAFhirPackageCacheDirectory_WhenThePackageIsNotStored_ThenItIsReadFromTheCacheWithoutTheNetwork()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        using var fhirCache = new TestFolder();
        foreach ((string path, string content) in Files().Where(file => file.Path.StartsWith("package/", StringComparison.Ordinal)))
        {
            fhirCache.Write(Path.Combine($"{PackageId}#{PackageVersion}", path), content);
        }

        JsonNode result = Json(await TermLookup.LookupAsync(Open(folder, Offline(store) with { FhirPackageCache = fhirCache.Path }), Body, "mi", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("found");
        Directory.Exists(Path.Combine(store.Path, "packages")).ShouldBeFalse();
        File.Exists(Path.Combine(fhirCache.Path, $"{PackageId}#{PackageVersion}", "package", "CodeSystem-body.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAPackageStoreInsideTheDataFolder_WhenAPackageIsDownloaded_ThenNothingIsWrittenThereAndTheAnswerSaysSo()
    {
        using var folder = new TestFolder();
        string inside = Path.Combine(folder.Path, "cache");
        var options = new TerminologyOptions
        {
            Packages = [PackageRequest.Parse($"{PackageId}@{PackageVersion}")],
            Store = PackageStore.Under(inside),
            Registry = new FhirPackageRegistry(Registry(Archive(Files()))),
        };

        JsonNode result = Json(await TermLookup.LookupAsync(Open(folder, options), Body, "mi", null, CancellationToken.None));

        ((string)result["facts"]!["outcome"]!).ShouldBe("found");
        Directory.Exists(inside).ShouldBeFalse();
        JsonNode issue = result["issues"]!.AsArray().Single(item => (string)item!["code"]! == PackageAcquisition.StoreUnwritableCode)!;
        ((string)issue["diagnostics"]!).ShouldContain("inside the data folder");
    }

    [Theory]
    [InlineData("hl7.fhir.r4.core@4.0", "invalid-package")]
    [InlineData("hl7.fhir.r4.core@latest", "invalid-package")]
    [InlineData("HL7.FHIR.R4.CORE@4.0.1", "invalid-package")]
    [InlineData("@scope/name@1.0.0", "invalid-package")]
    public async Task GivenAPackageThatIsNotAnExactNpmIdentity_WhenLoaded_ThenItIsRefusedBeforeAnyRequest(string package, string code)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        StubHttpHandler registry = Registry(Archive(Files()));

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, Online(registry, store) with { Packages = [] }), package, CancellationToken.None));

        ((string)result["issues"]![0]!["code"]!).ShouldBe(code);
        registry.Requests.Where(uri => !uri.AbsolutePath.Contains("hl7.fhir.r4.core", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void GivenAnArchiveWithoutAManifest_WhenRead_ThenItIsRejected()
    {
        byte[] archive = Archive(Files().Skip(1));

        Should.Throw<PackageFormatException>(() => PackageArchiveReader.Read(archive, CancellationToken.None)).Message.ShouldContain("package.json");
    }

    [Fact]
    public void GivenAnArchiveRepeatingAPath_WhenRead_ThenItIsRejected()
    {
        byte[] archive = Archive([.. Files(), ("package/CodeSystem-body.json", BodyCodeSystem(heartDisplay: "Second"))]);

        Should.Throw<PackageFormatException>(() => PackageArchiveReader.Read(archive, CancellationToken.None)).Message.ShouldContain("repeats");
    }

    [Fact]
    public void GivenBytesThatAreNotGzip_WhenRead_ThenTheyAreRejected()
    {
        Should.Throw<PackageFormatException>(() => PackageArchiveReader.Read([1, 2, 3, 4], CancellationToken.None));
    }
#pragma warning restore CA2000

#pragma warning disable CA2000 // Each registry client owns its stub handler and lives as long as its session.
    // Regression (Task 7 review 9c): at 96d6150 the first load of the default core package reported alreadyLoaded, because the
    // implicit default load had just run.
    [Fact]
    public async Task GivenNothingConfigured_WhenTheFirstCallLoadsTheCorePackage_ThenItIsLoadedByThisCallExplicitly()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        StubHttpHandler registry = Registry([("hl7.fhir.r4.core", "4.0.1", Archive(PackageFiles("hl7.fhir.r4.core", "4.0.1", "4.0.1", CodeSystem("http://example.org/core", "1", "", "a"))), null)]);
        FolderSession session = Open(folder, Online(registry, store) with { Packages = [] });

        JsonNode result = Json(await TermLoad.LoadAsync(session, "hl7.fhir.r4.core@4.0.1", CancellationToken.None));

        ((bool)result["facts"]!["alreadyLoaded"]!).ShouldBeFalse();
        ((string)result["facts"]!["origin"]!).ShouldBe("downloaded");
        session.Terminology.Current!.Store.Sources.ShouldHaveSingleItem().Info.IsDefault.ShouldBeFalse();
    }

    // Regression (Task 7 review 9f): at 96d6150 a registry latest that is not a version was reported as the caller's mistake.
    [Fact]
    public async Task GivenARegistryLatestThatIsNotAVersion_WhenABareIdIsLoaded_ThenTheMessageBlamesTheRegistry()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        var registry = new StubHttpHandler(_ => StubHttpHandler.Bytes(System.Text.Encoding.UTF8.GetBytes("""{"name":"pkg.odd","dist-tags":{"latest":"current"},"versions":{}}"""), "application/json"));

        JsonNode result = Json(await TermLoad.LoadAsync(Open(folder, Online(registry, store) with { Packages = [] }), "pkg.odd", CancellationToken.None));

        JsonNode issue = result["issues"]![0]!;
        ((string)issue["code"]!).ShouldBe(PackageRequest.InvalidCode);
        ((string)issue["diagnostics"]!).ShouldContain("The FHIR package registry https://packages.fhir.org/ names a latest version");
    }

    // Regression (Task 7 review 9g, 9j): at 96d6150 a package for another FHIR version loaded silently, and a load answer
    // carried the load issues of the state before the load.
    [Fact]
    public async Task GivenAPackageForAnotherFhirVersion_WhenLoaded_ThenTheLoadAnswerItselfWarnsAboutIt()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        StubHttpHandler registry = Registry([(PackageId, PackageVersion, Archive(Files()), null), ("pkg.r5", "1.0.0", Archive(PackageFiles("pkg.r5", "1.0.0", "5.0.0", CodeSystem("http://example.org/r5", "1", "", "a"))), null)]);
        FolderSession session = Open(folder, Online(registry, store));

        JsonNode result = Json(await TermLoad.LoadAsync(session, "pkg.r5@1.0.0", CancellationToken.None));

        JsonNode warning = result["issues"]!.AsArray().Single(issue => (string)issue!["code"]! == "package-fhir-version-mismatch")!;
        ((string)warning["diagnostics"]!).ShouldContain("pkg.r5@1.0.0 declares FHIR 5.0.0");
    }
#pragma warning restore CA2000
}
