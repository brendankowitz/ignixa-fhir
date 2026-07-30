using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// The three members the terminology import path needs from <see cref="IPackageResourceRepository"/>,
/// against a real database. These are the reason all three of its consumers used to reach past the
/// interface into <c>FhirDbContext.PackageResources</c> directly.
/// </summary>
public class SqlServerPackageResourceRepositoryTerminologyTests : IAsyncLifetime
{
    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private IPackageResourceRepository CreateRepository() => new SqlServerPackageResourceRepository(
        _fixture.SqlExecutionService,
        _fixture.SystemPartitionId,
        NullLogger<SqlServerPackageResourceRepository>.Instance);

    private Task InsertAsync(
        string packageId, string packageVersion, string resourceType, string canonical, string? status) =>
        _fixture.ExecuteNonQueryAsync(
            "INSERT INTO dbo.PackageResource " +
            "(PackageId, PackageVersion, ResourceType, Canonical, ResourceId, ResourceJson, FhirVersion, IsActive, TerminologyImportStatus) " +
            $"VALUES ('{packageId}', '{packageVersion}', '{resourceType}', '{canonical}', " +
            $"'{canonical.Split('/')[^1]}', '{{}}', '4.0.1', 1, " +
            $"{(status is null ? "NULL" : $"'{status}'")})",
            CancellationToken.None);

    [Fact]
    public async Task GivenASeededPackageResource_WhenFetchedById_ThenTheRowIsReturned()
    {
        var seeded = await _fixture.SeedPackageResourceAsync(
            "CodeSystem",
            "http://example.org/fhir/CodeSystem/by-id",
            TerminologyOracleFixture.HierarchicalCodeSystemJson("http://example.org/fhir/CodeSystem/by-id"));

        var result = await CreateRepository().GetByPackageResourceIdAsync(
            seeded.PackageResourceId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.PackageResourceId.ShouldBe(seeded.PackageResourceId);
        result.Canonical.ShouldBe(seeded.Canonical);
        result.ResourceType.ShouldBe("CodeSystem");
        result.PackageId.ShouldBe(seeded.PackageId);

        // The activity hashes this to decide whether anything changed, so a truncated or re-encoded
        // ResourceJson would make every import look like new content.
        result.ResourceJson.ShouldBe(seeded.ResourceJson);
    }

    [Fact]
    public async Task GivenAnIdThatDoesNotExist_WhenFetchedById_ThenNullIsReturned()
    {
        var result = await CreateRepository().GetByPackageResourceIdAsync(987654321, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenTerminologyResourcesInSeveralStates_WhenListingPendingImports_ThenOnlyNonTerminalOnesAreReturned()
    {
        await InsertAsync("pkg.a", "1.0.0", "CodeSystem", "http://example.org/a/cs-null", null);
        await InsertAsync("pkg.a", "1.0.0", "ValueSet", "http://example.org/a/vs-pending", "Pending");
        await InsertAsync("pkg.a", "1.0.0", "ConceptMap", "http://example.org/a/cm-failed", "Failed");
        await InsertAsync("pkg.a", "1.0.0", "CodeSystem", "http://example.org/a/cs-done", "Completed");
        await InsertAsync("pkg.a", "1.0.0", "CodeSystem", "http://example.org/a/cs-skipped", "Skipped");

        // Left behind by an import that died mid-flight. The EF query matched NULL/Pending/Failed by name
        // and so never retried these; excluding only the terminal statuses is what recovers them.
        await InsertAsync("pkg.a", "1.0.0", "CodeSystem", "http://example.org/a/cs-stuck", "InProgress");

        // Not a terminology resource, so it is never a candidate however its status reads.
        await InsertAsync("pkg.a", "1.0.0", "StructureDefinition", "http://example.org/a/sd", null);

        var pending = await CreateRepository().ListPendingTerminologyImportsAsync(
            "pkg.a", "1.0.0", CancellationToken.None);

        var group = pending.ShouldHaveSingleItem();
        group.PackageId.ShouldBe("pkg.a");
        group.PackageVersion.ShouldBe("1.0.0");
        group.PackageResourceIds.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GivenPendingResourcesAcrossPackages_WhenListingWithoutAFilter_ThenTheyAreGroupedByPackageVersion()
    {
        await InsertAsync("pkg.b", "1.0.0", "CodeSystem", "http://example.org/b1/cs", "Pending");
        await InsertAsync("pkg.b", "1.0.0", "ValueSet", "http://example.org/b1/vs", "Pending");
        await InsertAsync("pkg.b", "2.0.0", "CodeSystem", "http://example.org/b2/cs", "Pending");
        await InsertAsync("pkg.c", "1.0.0", "ConceptMap", "http://example.org/c/cm", null);

        var pending = await CreateRepository().ListPendingTerminologyImportsAsync(
            packageId: null, packageVersion: null, CancellationToken.None);

        // Two versions of pkg.b are two groups, not one: each is a separate import orchestration.
        pending.Count.ShouldBe(3);
        pending.ShouldContain(p => p.PackageId == "pkg.b" && p.PackageVersion == "1.0.0" && p.PackageResourceIds.Count == 2);
        pending.ShouldContain(p => p.PackageId == "pkg.b" && p.PackageVersion == "2.0.0" && p.PackageResourceIds.Count == 1);
        pending.ShouldContain(p => p.PackageId == "pkg.c" && p.PackageVersion == "1.0.0" && p.PackageResourceIds.Count == 1);
    }

    [Fact]
    public async Task GivenEverythingIsTerminal_WhenListingPendingImports_ThenNothingIsReturned()
    {
        await InsertAsync("pkg.d", "1.0.0", "CodeSystem", "http://example.org/d/cs", "Completed");
        await InsertAsync("pkg.d", "1.0.0", "ValueSet", "http://example.org/d/vs", "Skipped");

        var pending = await CreateRepository().ListPendingTerminologyImportsAsync(
            "pkg.d", "1.0.0", CancellationToken.None);

        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAPackageResource_WhenMarkedFailed_ThenTheStatusAndMessageAreRecorded()
    {
        var seeded = await _fixture.SeedPackageResourceAsync(
            "CodeSystem",
            "http://example.org/fhir/CodeSystem/mark-failed",
            TerminologyOracleFixture.HierarchicalCodeSystemJson("http://example.org/fhir/CodeSystem/mark-failed"));

        await CreateRepository().MarkTerminologyImportFailedAsync(
            seeded.PackageResourceId, "something went wrong", CancellationToken.None);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {seeded.PackageResourceId}", CancellationToken.None);

        var message = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 ImportErrorMessage FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {seeded.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Failed");
        message.ShouldBe("something went wrong");
    }

    [Fact]
    public async Task GivenAnErrorMessageLongerThanTheColumn_WhenMarkedFailed_ThenItIsTruncatedRatherThanLost()
    {
        // ImportErrorMessage is NVARCHAR(1000). Writing more than that fails the statement outright, which
        // would lose the error entirely rather than record a shortened one.
        var seeded = await _fixture.SeedPackageResourceAsync(
            "CodeSystem",
            "http://example.org/fhir/CodeSystem/mark-failed-long",
            TerminologyOracleFixture.HierarchicalCodeSystemJson("http://example.org/fhir/CodeSystem/mark-failed-long"));

        await CreateRepository().MarkTerminologyImportFailedAsync(
            seeded.PackageResourceId, new string('x', 5000), CancellationToken.None);

        var length = await _fixture.ExecuteScalarAsync<int>(
            "SELECT TOP 1 LEN(ImportErrorMessage) FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {seeded.PackageResourceId}", CancellationToken.None);

        length.ShouldBe(1000);
    }
}
