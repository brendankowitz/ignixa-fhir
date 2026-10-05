using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class SharedContentPackageMutationTests
{
    [Fact]
    public async Task GivenSplitSharedContent_WhenPackageMutationsAreRequested_ThenAllRejectBeforeChangingStoredRows()
    {
        var packages = await TestTenantDatabase.CreateEmptyAsync();
        var terminology = await TestTenantDatabase.CreateEmptyAsync();
        try
        {
            var sql = SharedContentDatabaseTopologyTests.CreateSql(packages.ConnectionString, terminology.ConnectionString);
            var repository = new SqlServerPackageResourceRepository(
                sql, 1, NullLogger<SqlServerPackageResourceRepository>.Instance,
                new SharedContentDatabaseGuard(sql, 1, 0));
            var resource = new PackageResource
            {
                PackageId = "terminology.contract.guarded", PackageVersion = "1", ResourceId = "guarded",
                ResourceType = "ValueSet", Canonical = "http://terminology-contract.example/guarded", ResourceJson = "{}",
                FhirVersion = "4.0.1", IsActive = true,
            };
            var seedRepository = new SqlServerPackageResourceRepository(
                packages.SqlExecutionService, 1, NullLogger<SqlServerPackageResourceRepository>.Instance);
            await seedRepository.UpsertAsync(resource, CancellationToken.None);
            var stored = (await seedRepository.GetFromPackageAsync(resource.PackageId, resource.PackageVersion, resource.Canonical))!;
            resource.ResourceJson = """{"changed":true}""";

            Func<Task>[] operations =
            [
                () => repository.UpsertAsync(resource, CancellationToken.None),
                () => repository.BatchUpsertAsync([resource], CancellationToken.None),
                () => repository.DeactivatePackageAsync(resource.PackageId, resource.PackageVersion, CancellationToken.None),
                () => repository.ReactivatePackageAsync(resource.PackageId, resource.PackageVersion, CancellationToken.None),
                () => repository.DeletePackageAsync(resource.PackageId, resource.PackageVersion, CancellationToken.None),
                () => repository.MarkTerminologyImportFailedAsync(stored.PackageResourceId, "should not be written", CancellationToken.None),
                () => repository.GetByPackageResourceIdAsync(stored.PackageResourceId, CancellationToken.None),
            ];
            foreach (var operation in operations)
            {
                (await Should.ThrowAsync<InvalidOperationException>(operation)).Message.ShouldContain("shared SQL database");
            }

            (await packages.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PackageResource WHERE IsActive = 1 AND ResourceJson = '{}' AND TerminologyImportStatus IS NULL")).ShouldBe(1);
            (await terminology.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.PackageResource")).ShouldBe(0);
        }
        finally
        {
            await terminology.DisposeAsync();
            await packages.DisposeAsync();
        }
    }
}
