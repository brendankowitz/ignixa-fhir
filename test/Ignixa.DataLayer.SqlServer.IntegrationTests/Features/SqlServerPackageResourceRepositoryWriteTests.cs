using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// Write-path contract for <see cref="IPackageResourceRepository"/> — Phase F Task 4a. Written against the
/// EF implementation first so it encodes what that implementation actually does, then repointed at the
/// raw-ADO.NET one. Unlike background jobs, <c>PackageResourceEntity</c> matches <c>dbo.PackageResource</c>
/// exactly (all 17 columns, types, lengths and nullability), so this is a genuine port and the EF version is
/// a usable oracle.
/// </summary>
public class SqlServerPackageResourceRepositoryWriteTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    // The single seam Task 4 flipped. Every assertion below was written and run green against the EF
    // implementation first; none were edited when this changed.
    private IPackageResourceRepository CreateRepository()
        => new SqlServerPackageResourceRepository(
            _database.SqlExecutionService,
            _database.TenantId,
            NullLogger<SqlServerPackageResourceRepository>.Instance);

    private static PackageResource Resource(
        string packageId,
        string resourceId,
        string resourceType = "StructureDefinition",
        string packageVersion = "1.0.0",
        string? version = null,
        bool isActive = true,
        string json = """{"resourceType":"StructureDefinition"}""") => new()
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            ResourceType = resourceType,
            Canonical = $"http://example.org/{resourceId}",
            Version = version,
            ResourceId = resourceId,
            ResourceJson = json,
            FhirVersion = "4.0.1",
            IsActive = isActive,
        };

    private static string NewPackageId() => $"pkg.{Guid.NewGuid():N}";

    private Task<int> CountAsync(string packageId) => _database.ExecuteScalarAsync<int>(
        $"SELECT COUNT(*) FROM dbo.PackageResource WHERE PackageId = '{packageId}'", CancellationToken.None);

    [Fact]
    public async Task GivenANewResource_WhenUpserted_ThenItIsInserted()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(Resource(packageId, "sd-1"), CancellationToken.None);

        (await CountAsync(packageId)).ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnExistingResource_WhenUpsertedAgain_ThenItIsUpdatedNotDuplicated()
    {
        // Identity is the unique constraint: PackageId + PackageVersion + ResourceType + ResourceId.
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(Resource(packageId, "sd-1", json: """{"v":1}"""), CancellationToken.None);
        await repository.UpsertAsync(Resource(packageId, "sd-1", json: """{"v":2}"""), CancellationToken.None);

        (await CountAsync(packageId)).ShouldBe(1);

        var storedJson = await _database.ExecuteScalarAsync<string>(
            $"SELECT ResourceJson FROM dbo.PackageResource WHERE PackageId = '{packageId}'", CancellationToken.None);
        storedJson.ShouldBe("""{"v":2}""");
    }

    [Fact]
    public async Task GivenAResourceWithImportState_WhenUpsertedAgain_ThenTheImportStateSurvives()
    {
        // UpdateEntityFromModel touches seven columns and deliberately leaves the terminology-import ones
        // alone, so re-loading a package does not discard import progress already recorded against it.
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(Resource(packageId, "cs-1", resourceType: "CodeSystem"), CancellationToken.None);

        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.PackageResource SET TerminologyImportStatus = 'Completed', ImportedConceptCount = 42 " +
            $"WHERE PackageId = '{packageId}'", CancellationToken.None);

        await repository.UpsertAsync(
            Resource(packageId, "cs-1", resourceType: "CodeSystem", json: """{"v":2}"""), CancellationToken.None);

        var status = await _database.ExecuteScalarAsync<string>(
            $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageId = '{packageId}'",
            CancellationToken.None);
        var count = await _database.ExecuteScalarAsync<int>(
            $"SELECT ImportedConceptCount FROM dbo.PackageResource WHERE PackageId = '{packageId}'",
            CancellationToken.None);

        status.ShouldBe("Completed");
        count.ShouldBe(42);
    }

    [Fact]
    public async Task GivenAMixedBatch_WhenBatchUpserted_ThenExistingRowsUpdateAndNewOnesInsert()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(Resource(packageId, "sd-1", json: """{"v":1}"""), CancellationToken.None);

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", json: """{"v":2}"""),
            Resource(packageId, "sd-2", json: """{"v":1}"""),
        ], CancellationToken.None);

        (await CountAsync(packageId)).ShouldBe(2);

        var updated = await _database.ExecuteScalarAsync<string>(
            $"SELECT ResourceJson FROM dbo.PackageResource WHERE PackageId = '{packageId}' AND ResourceId = 'sd-1'",
            CancellationToken.None);
        updated.ShouldBe("""{"v":2}""");
    }

    [Fact]
    public async Task GivenAnEmptyBatch_WhenBatchUpserted_ThenNothingHappens()
    {
        var repository = CreateRepository();

        await Should.NotThrowAsync(() => repository.BatchUpsertAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task GivenActiveResources_WhenThePackageIsDeactivated_ThenOnlyActiveRowsAreCountedAndFlipped()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1"),
            Resource(packageId, "sd-2"),
            Resource(packageId, "sd-3", isActive: false),
        ], CancellationToken.None);

        var deactivated = await repository.DeactivatePackageAsync(packageId, "1.0.0", CancellationToken.None);

        // Only the two that were active are affected; the already-inactive row is not recounted.
        deactivated.ShouldBe(2);

        var activeRemaining = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.PackageResource WHERE PackageId = '{packageId}' AND IsActive = 1",
            CancellationToken.None);
        activeRemaining.ShouldBe(0);
    }

    [Fact]
    public async Task GivenInactiveResources_WhenThePackageIsReactivated_ThenOnlyInactiveRowsAreCountedAndFlipped()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", isActive: false),
            Resource(packageId, "sd-2", isActive: true),
        ], CancellationToken.None);

        var reactivated = await repository.ReactivatePackageAsync(packageId, "1.0.0", CancellationToken.None);

        reactivated.ShouldBe(1);

        var activeCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.PackageResource WHERE PackageId = '{packageId}' AND IsActive = 1",
            CancellationToken.None);
        activeCount.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAPackage_WhenDeleted_ThenEveryRowGoesRegardlessOfActiveState()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1"),
            Resource(packageId, "sd-2", isActive: false),
        ], CancellationToken.None);

        var deleted = await repository.DeletePackageAsync(packageId, "1.0.0", CancellationToken.None);

        deleted.ShouldBe(2);
        (await CountAsync(packageId)).ShouldBe(0);
    }

    [Fact]
    public async Task GivenAnotherPackageVersion_WhenOneVersionIsDeleted_ThenTheOtherIsUntouched()
    {
        var repository = CreateRepository();
        var packageId = NewPackageId();

        await repository.UpsertAsync(Resource(packageId, "sd-1", packageVersion: "1.0.0"), CancellationToken.None);
        await repository.UpsertAsync(Resource(packageId, "sd-1", packageVersion: "2.0.0"), CancellationToken.None);

        await repository.DeletePackageAsync(packageId, "1.0.0", CancellationToken.None);

        var remaining = await _database.ExecuteScalarAsync<string>(
            $"SELECT PackageVersion FROM dbo.PackageResource WHERE PackageId = '{packageId}'", CancellationToken.None);
        remaining.ShouldBe("2.0.0");
    }

    [Fact]
    public async Task GivenBlankIdentifiers_WhenPackageOperationsAreCalled_ThenTheyAreRejected()
    {
        var repository = CreateRepository();

        await Should.ThrowAsync<ArgumentException>(
            () => repository.DeactivatePackageAsync("  ", "1.0.0", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(
            () => repository.ReactivatePackageAsync("pkg", "  ", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(
            () => repository.DeletePackageAsync("  ", "1.0.0", CancellationToken.None));
    }
}
