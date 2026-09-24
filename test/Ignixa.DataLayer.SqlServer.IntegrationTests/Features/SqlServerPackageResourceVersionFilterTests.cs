using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// Pins two parameters that <see cref="IPackageResourceRepository"/> accepts and does not apply, so the gap
/// is documented behaviour with a named test rather than something a future reader discovers by surprise.
/// <para>
/// <b><c>fhirVersion</c> (seven methods).</b> Callers pass <c>"R4"</c>/<c>"R4B"</c>/<c>"R5"</c>/<c>"Stu3"</c>
/// (<c>OperationsSegment.GetFhirVersionString</c>); the column holds what the NPM manifest declared, e.g.
/// <c>"4.0.1"</c>. An equality filter matches nothing, so enabling it would empty the CapabilityStatement's
/// operations and the StructureDefinition summaries rather than narrow them. Honouring it needs version
/// normalisation plus set-membership, since manifests declare a list such as
/// <c>["4.0.1","4.3.0","5.0.0"]</c>. The EF implementation carried the filter commented out with
/// "pending resolution of exact matching strategy" — this is that unresolved strategy, not an oversight.
/// </para>
/// <para>
/// <b><c>tenantId</c> on <c>PackageVersionExistsAsync</c>.</b> <c>dbo.PackageResource</c> has no tenant
/// column at all; package content is global. <c>ImplementationGuideProvider</c> passes a real tenant id and
/// logs "already loaded for tenant {TenantId}", so it believes otherwise. Making that true is a schema
/// change, not a data-access one.
/// </para>
/// <para>
/// <b>These tests are meant to fail</b> the day either gap is closed. That is the signal to update them
/// deliberately, not to relax them.
/// </para>
/// </summary>
public class SqlServerPackageResourceVersionFilterTests : IAsyncLifetime
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

    private static PackageResource Resource(string packageId, string resourceId, string canonical, string fhirVersion) => new()
    {
        PackageId = packageId,
        PackageVersion = "1.0.0",
        ResourceType = "StructureDefinition",
        Canonical = canonical,
        ResourceId = resourceId,
        ResourceJson = """{"resourceType":"StructureDefinition"}""",
        FhirVersion = fhirVersion,
        IsActive = true,
    };

    [Fact]
    public async Task GivenResourcesOfSeveralFhirVersions_WhenFetchedWithAVersionThatCannotMatch_ThenAllAreStillReturned()
    {
        // "R4" can never equal "4.0.1", which is why the filter is not applied. If it were applied naively,
        // this call would return nothing and /metadata would lose its StructureDefinitions.
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", $"http://example.org/{packageId}/a", "4.0.1"),
            Resource(packageId, "sd-2", $"http://example.org/{packageId}/b", "5.0.0"),
        ], CancellationToken.None);

        var asR4 = await repository.GetAllStructureDefinitionsAsync("R4", CancellationToken.None);

        var mine = asR4.Where(r => r.PackageId == packageId).ToList();
        mine.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnExactStoredFhirVersion_WhenFetchedWithIt_ThenItStillDoesNotNarrow()
    {
        // Even a value that *could* match is not applied -- the parameter is inert on every path, so the
        // behaviour does not depend on which string the caller happens to pass.
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", $"http://example.org/{packageId}/a", "4.0.1"),
            Resource(packageId, "sd-2", $"http://example.org/{packageId}/b", "5.0.0"),
        ], CancellationToken.None);

        var asFourOhOne = await repository.GetAllStructureDefinitionsAsync("4.0.1", CancellationToken.None);

        asFourOhOne.Where(r => r.PackageId == packageId).Count().ShouldBe(2);
    }

    [Fact]
    public async Task GivenSearchParametersOfSeveralFhirVersions_WhenFetchedWithAVersion_ThenAllAreReturned()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            new PackageResource
            {
                PackageId = packageId, PackageVersion = "1.0.0", ResourceType = "SearchParameter",
                Canonical = $"http://example.org/{packageId}/sp1", ResourceId = "sp-1",
                ResourceJson = """{"resourceType":"SearchParameter"}""", FhirVersion = "4.0.1", IsActive = true,
            },
            new PackageResource
            {
                PackageId = packageId, PackageVersion = "1.0.0", ResourceType = "SearchParameter",
                Canonical = $"http://example.org/{packageId}/sp2", ResourceId = "sp-2",
                ResourceJson = """{"resourceType":"SearchParameter"}""", FhirVersion = "5.0.0", IsActive = true,
            },
        ], CancellationToken.None);

        var all = await repository.GetAllSearchParametersAsync("R4", CancellationToken.None);

        all.Where(r => r.PackageId == packageId).Count().ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnyTenantId_WhenCheckingPackageExistence_ThenTheAnswerIsTheSame()
    {
        // Packages are global. Two different tenant ids must give identical answers, because there is no
        // tenant dimension in the table to distinguish them.
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(
            Resource(packageId, "sd-1", $"http://example.org/{packageId}/a", "4.0.1"), CancellationToken.None);

        var asTenantOne = await repository.PackageVersionExistsAsync(packageId, "1.0.0", 1, CancellationToken.None);
        var asTenantTwo = await repository.PackageVersionExistsAsync(packageId, "1.0.0", 2, CancellationToken.None);

        asTenantOne.ShouldBeTrue();
        asTenantTwo.ShouldBe(asTenantOne);
    }
}
