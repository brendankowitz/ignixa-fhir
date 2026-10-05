using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// Read-path contract for <see cref="IPackageResourceRepository"/> — Phase F Task 4.
/// <para>
/// <b>Provenance, stated accurately:</b> unlike
/// <see cref="SqlServerPackageResourceRepositoryWriteTests"/>, these were written directly against the new
/// implementation rather than run green against the EF one first. They encode the EF semantics as read from
/// its source, not as observed from executing it. Two caught real defects on their first run anyway, but the
/// weaker provenance is worth knowing if one is ever in question.
/// </para>
/// <para>
/// <c>GetLatestByCanonicalAsync</c>'s ordering is the single deliberate behaviour change in this task, and
/// would fail against EF by design: that implementation ordered versions as plain strings, so
/// <c>1.10.0</c> ranked below <c>1.9.0</c>.
/// </para>
/// </summary>
public class SqlServerPackageResourceRepositoryReadTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private IPackageResourceRepository CreateRepository()
        => new SqlServerPackageResourceRepository(
            _database.SqlExecutionService,
            _database.TenantId,
            NullLogger<SqlServerPackageResourceRepository>.Instance);

    private static string NewPackageId() => $"pkg.{Guid.NewGuid():N}";

    private static PackageResource Resource(
        string packageId,
        string resourceId,
        string canonical,
        string resourceType = "StructureDefinition",
        string packageVersion = "1.0.0",
        string? version = null,
        bool isActive = true,
        string fhirVersion = "4.0.1",
        string json = """{"resourceType":"StructureDefinition"}""") => new()
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            ResourceType = resourceType,
            Canonical = canonical,
            Version = version,
            ResourceId = resourceId,
            ResourceJson = json,
            FhirVersion = fhirVersion,
            IsActive = isActive,
        };

    [Fact]
    public async Task GivenAnInactiveResource_WhenFetchedByCanonical_ThenItIsNotReturned()
    {
        // Every read in this repository is active-only. Deactivating a package hides it from resolution
        // without deleting it, so this filter is the whole point of DeactivatePackageAsync.
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var canonical = $"http://example.org/{packageId}/sd";

        await repository.UpsertAsync(Resource(packageId, "sd-1", canonical, isActive: false), CancellationToken.None);

        (await repository.GetByCanonicalAsync(canonical, null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenTwoBusinessVersions_WhenFetchedByCanonicalWithAVersion_ThenOnlyThatOneMatches()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var canonical = $"http://example.org/{packageId}/sd";

        await repository.UpsertAsync(Resource(packageId, "sd-1", canonical, version: "1.0"), CancellationToken.None);
        await repository.UpsertAsync(Resource(packageId, "sd-2", canonical, version: "2.0"), CancellationToken.None);

        var found = await repository.GetByCanonicalAsync(canonical, "2.0", CancellationToken.None);

        found.ShouldNotBeNull();
        found.ResourceId.ShouldBe("sd-2");
    }

    [Fact]
    public async Task GivenAResourceInOnePackage_WhenFetchedFromAnother_ThenNothingIsReturned()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var canonical = $"http://example.org/{packageId}/sd";

        await repository.UpsertAsync(Resource(packageId, "sd-1", canonical), CancellationToken.None);

        (await repository.GetFromPackageAsync(packageId, "1.0.0", canonical, CancellationToken.None)).ShouldNotBeNull();
        (await repository.GetFromPackageAsync(packageId, "9.9.9", canonical, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenDoubleDigitMinorVersions_WhenFetchingTheLatest_ThenVersionsCompareNumericallyNotAsText()
    {
        // THE deliberate behaviour change in Task 4. The EF implementation ordered by the raw version string
        // while carrying a comment claiming PARSENAME-based semantic parsing that was never written, so
        // "1.9.0" beat "1.10.0" and the wrong row came back as "latest". This fact fails against EF by design.
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var canonical = $"http://example.org/{packageId}/sd";

        await repository.UpsertAsync(Resource(packageId, "sd-1", canonical, packageVersion: "1.9.0"), CancellationToken.None);
        await repository.UpsertAsync(Resource(packageId, "sd-1", canonical, packageVersion: "1.10.0"), CancellationToken.None);

        var latest = await repository.GetLatestByCanonicalAsync(canonical, null, CancellationToken.None);

        latest.ShouldNotBeNull();
        latest.PackageVersion.ShouldBe("1.10.0");
    }

    [Fact]
    public async Task GivenMixedResourceTypes_WhenListingAPackage_ThenTheyAreOrderedByTypeThenCanonical()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sp-1", "http://example.org/z", resourceType: "SearchParameter"),
            Resource(packageId, "sd-2", "http://example.org/b"),
            Resource(packageId, "sd-1", "http://example.org/a"),
        ], CancellationToken.None);

        var all = await repository.ListPackageResourcesAsync(packageId, "1.0.0", null, CancellationToken.None);

        // ResourceType orders first and ordinally, so "SearchParameter" precedes "StructureDefinition";
        // Canonical only breaks ties within a type.
        all.Select(r => r.Canonical).ShouldBe(
            ["http://example.org/z", "http://example.org/a", "http://example.org/b"]);

        var onlySearchParams = await repository.ListPackageResourcesAsync(
            packageId, "1.0.0", "SearchParameter", CancellationToken.None);
        onlySearchParams.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenSeveralResourcesInOnePackage_WhenListingLoadedPackages_ThenThePackageAppearsOnce()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", "http://example.org/a"),
            Resource(packageId, "sd-2", "http://example.org/b"),
        ], CancellationToken.None);

        var loaded = await repository.ListLoadedPackagesAsync(CancellationToken.None);

        loaded.Count(p => p.PackageId == packageId).ShouldBe(1);
    }

    [Fact]
    public async Task GivenABareTypeName_WhenFetchingStructureDefinitions_ThenTheSuffixMatchIsUsed()
    {
        // A canonical containing '/' is an exact match; a bare name matches any canonical ending in "/name",
        // so "Patient" resolves "http://hl7.org/fhir/StructureDefinition/Patient".
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var typeName = $"Custom{Guid.NewGuid():N}"[..20];

        await repository.UpsertAsync(
            Resource(packageId, "sd-1", $"http://hl7.org/fhir/StructureDefinition/{typeName}"), CancellationToken.None);

        var byBareName = await repository.GetStructureDefinitionsByCanonicalAsync(typeName, null, CancellationToken.None);
        byBareName.Count.ShouldBe(1);

        var byFullUrl = await repository.GetStructureDefinitionsByCanonicalAsync(
            $"http://hl7.org/fhir/StructureDefinition/{typeName}", null, CancellationToken.None);
        byFullUrl.Count.ShouldBe(1);

        // A bare name must not match a canonical that merely contains it mid-path.
        var noMatch = await repository.GetStructureDefinitionsByCanonicalAsync(
            $"{typeName}X", null, CancellationToken.None);
        noMatch.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAPackage_WhenCheckingExistence_ThenOnlyActiveVersionsCount()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(Resource(packageId, "sd-1", "http://example.org/a"), CancellationToken.None);

        (await repository.PackageVersionExistsAsync(packageId, "1.0.0", 0, CancellationToken.None)).ShouldBeTrue();
        (await repository.PackageVersionExistsAsync(packageId, "9.9.9", 0, CancellationToken.None)).ShouldBeFalse();

        await repository.DeactivatePackageAsync(packageId, "1.0.0", CancellationToken.None);

        (await repository.PackageVersionExistsAsync(packageId, "1.0.0", 0, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenSearchParametersWithDifferentBases_WhenFetchedByResourceType_ThenBaseArrayMembershipDecides()
    {
        // base[] lives inside the resource JSON, not in a column, so this filter is applied in memory.
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sp-1", $"http://example.org/{packageId}/sp1", resourceType: "SearchParameter",
                json: """{"resourceType":"SearchParameter","base":["Patient","Observation"]}"""),
            Resource(packageId, "sp-2", $"http://example.org/{packageId}/sp2", resourceType: "SearchParameter",
                json: """{"resourceType":"SearchParameter","base":["Encounter"]}"""),
            Resource(packageId, "sp-3", $"http://example.org/{packageId}/sp3", resourceType: "SearchParameter",
                json: """{"resourceType":"SearchParameter"}"""),
        ], CancellationToken.None);

        var forPatient = await repository.GetSearchParametersByResourceTypeAsync("Patient", null, CancellationToken.None);

        forPatient.Select(r => r.ResourceId).ShouldContain("sp-1");
        forPatient.Select(r => r.ResourceId).ShouldNotContain("sp-2");

        // A SearchParameter with no base[] at all matches nothing rather than everything.
        forPatient.Select(r => r.ResourceId).ShouldNotContain("sp-3");
    }

    [Fact]
    public async Task GivenNoOperationNames_WhenFetchingOperationDefinitions_ThenTheQueryIsSkipped()
    {
        var repository = CreateRepository();

        var result = await repository.GetOperationDefinitionsAsync([], null, CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOperationDefinitions_WhenFetchedByName_ThenOnlyTheNamedOnesReturn()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "everything", $"http://example.org/{packageId}/op1", resourceType: "OperationDefinition"),
            Resource(packageId, "validate", $"http://example.org/{packageId}/op2", resourceType: "OperationDefinition"),
        ], CancellationToken.None);

        var found = await repository.GetOperationDefinitionsAsync(["everything"], null, CancellationToken.None);

        found.Select(r => r.ResourceId).ShouldContain("everything");
        found.Select(r => r.ResourceId).ShouldNotContain("validate");
    }

    [Fact]
    public async Task GivenAMalformedCanonical_WhenFetchingAStructureMap_ThenItIsAMissNotAnError()
    {
        // Callers resolve arbitrary references through here, so an unparseable or non-http(s) canonical is
        // "not found" rather than a fault.
        var repository = CreateRepository();

        (await repository.GetStructureMapByUrlAsync("not a url", CancellationToken.None)).ShouldBeNull();
        (await repository.GetStructureMapByUrlAsync("ftp://example.org/map", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenAStructureMap_WhenFetchedByUrl_ThenTheMostRecentlyLoadedWins()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var canonical = $"http://example.org/{packageId}/map";

        var older = Resource(packageId, "map-1", canonical, resourceType: "StructureMap", packageVersion: "1.0.0");
        older.LoadedDate = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = Resource(packageId, "map-1", canonical, resourceType: "StructureMap", packageVersion: "2.0.0");
        newer.LoadedDate = DateTimeOffset.UtcNow;

        await repository.UpsertAsync(older, CancellationToken.None);
        await repository.UpsertAsync(newer, CancellationToken.None);

        var found = await repository.GetStructureMapByUrlAsync(canonical, CancellationToken.None);

        found.ShouldNotBeNull();
        found.PackageVersion.ShouldBe("2.0.0");
    }

    [Fact]
    public async Task GivenAMixedPackage_WhenFetchingResourcesForActivation_ThenOnlyConformanceTypesReturn()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", $"http://example.org/{packageId}/a"),
            Resource(packageId, "sp-1", $"http://example.org/{packageId}/b", resourceType: "SearchParameter"),
            Resource(packageId, "cs-1", $"http://example.org/{packageId}/c", resourceType: "CodeSystem"),
        ], CancellationToken.None);

        var forActivation = await repository.GetResourcesForActivationAsync(packageId, "1.0.0", CancellationToken.None);

        forActivation.Select(r => r.ResourceType).Distinct().OrderBy(t => t, StringComparer.Ordinal)
            .ShouldBe(["SearchParameter", "StructureDefinition"]);
    }

    [Fact]
    public async Task GivenAnyResource_WhenRead_ThenTheTerminologyImportColumnsAreNotPopulated()
    {
        // Inherited: the EF mapper read eleven of the seventeen columns, leaving TerminologyImportStatus,
        // ContentHash and the four Import* fields null on every returned model. Pinned so a future reader
        // knows the nulls are the contract here, not missing data in the table.
        var repository = CreateRepository();
        var packageId = NewPackageId();
        var canonical = $"http://example.org/{packageId}/cs";

        await repository.UpsertAsync(
            Resource(packageId, "cs-1", canonical, resourceType: "CodeSystem"), CancellationToken.None);

        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.PackageResource SET TerminologyImportStatus = 'Completed', ContentHash = 'abc', " +
            $"ImportedConceptCount = 7 WHERE PackageId = '{packageId}'", CancellationToken.None);

        var read = await repository.GetByCanonicalAsync(canonical, null, CancellationToken.None);

        read.ShouldNotBeNull();
        read.TerminologyImportStatus.ShouldBeNull();
        read.ContentHash.ShouldBeNull();
        read.ImportedConceptCount.ShouldBeNull();
    }
}
