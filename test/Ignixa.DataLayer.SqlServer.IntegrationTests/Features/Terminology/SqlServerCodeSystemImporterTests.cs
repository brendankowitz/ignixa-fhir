using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// The ported CodeSystem importer, held to the same facts as the EF oracle — except the ones that recorded
/// the parent-resolution defect, which this implementation fixes by construction.
/// <para>
/// The EF importer chose between two insert paths and ran parent resolution on only one, so any CodeSystem
/// at or below 1,000 concepts landed flat. Here there is one path: <c>dbo.ImportTermCodeSystem</c> inserts
/// and resolves the hierarchy server-side in a single transaction, so the size of the CodeSystem cannot
/// change whether the hierarchy survives.
/// </para>
/// </summary>
public class SqlServerCodeSystemImporterTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/ported-vehicles";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<Ignixa.Domain.Terminology.TerminologyImportResult> ImportAsync(string url, string? json = null)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, json ?? TerminologyOracleFixture.HierarchicalCodeSystemJson(url));

        var importer = _fixture.CreateSqlServerImporter();
        return await importer.ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    private Task<int> ConceptCountAsync(string url) => _fixture.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.TermConcept tc " +
        "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
        "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
        $"WHERE s.Value = '{url}'", CancellationToken.None);

    [Fact]
    public async Task GivenAHierarchicalCodeSystem_WhenImported_ThenTheHierarchyIsPersisted()
    {
        // THE FIX. Against the EF importer this same seed produced zero parent links; see
        // TerminologyOracleImportTests, whose assertions record that defect.
        await ImportAsync(SystemUrl);

        (await ConceptCountAsync(SystemUrl)).ShouldBe(4);

        var childrenOfVehicle = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept child " +
            "JOIN dbo.TermConcept parent ON parent.TermConceptId = child.ParentConceptId " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = child.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}' AND parent.Code = 'vehicle'", CancellationToken.None);

        childrenOfVehicle.ShouldBe(2);

        var roots = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}' AND tc.ParentConceptId IS NULL", CancellationToken.None);

        // vehicle and building are roots; car and truck now hang off vehicle.
        roots.ShouldBe(2);
    }

    [Fact]
    public async Task GivenTheHierarchyIsPersisted_WhenSubsumptionIsTested_ThenItAnswersCorrectly()
    {
        // The user-visible payoff: $subsumes was returning "not-subsumed" for every pair in any CodeSystem
        // under 1,000 concepts, as a well-formed FHIR response.
        await ImportAsync(SystemUrl);

        var service = _fixture.CreateTerminologyService();

        var parentToChild = await service.SubsumesAsync(
            new SubsumesParameters("vehicle", "car", SystemUrl, null), CancellationToken.None);
        var childToParent = await service.SubsumesAsync(
            new SubsumesParameters("car", "vehicle", SystemUrl, null), CancellationToken.None);
        var siblings = await service.SubsumesAsync(
            new SubsumesParameters("car", "truck", SystemUrl, null), CancellationToken.None);
        var self = await service.SubsumesAsync(
            new SubsumesParameters("car", "car", SystemUrl, null), CancellationToken.None);

        parentToChild.Outcome.ShouldBe("subsumes");
        childToParent.Outcome.ShouldBe("subsumed-by");
        siblings.Outcome.ShouldBe("not-subsumed");
        self.Outcome.ShouldBe("equivalent");
    }

    [Fact]
    public async Task GivenACodeSystemAtTheOldThreshold_WhenImported_ThenSizeNoLongerDecidesAnything()
    {
        // 1,000 and 1,001 concepts took different code paths before, and only one resolved parents. One
        // path now serves both, so the boundary has no behavioural meaning left.
        var atUrl = "http://example.org/fhir/CodeSystem/ported-flat-1000";
        var aboveUrl = "http://example.org/fhir/CodeSystem/ported-flat-1001";

        await ImportAsync(atUrl, TerminologyOracleFixture.FlatCodeSystemJson(atUrl, 1000));
        await ImportAsync(aboveUrl, TerminologyOracleFixture.FlatCodeSystemJson(aboveUrl, 1001));

        (await ConceptCountAsync(atUrl)).ShouldBe(1000);
        (await ConceptCountAsync(aboveUrl)).ShouldBe(1001);
    }

    [Fact]
    public async Task GivenTheSameCodeSystemTwice_WhenTheContentIsUnchanged_ThenTheSecondImportIsSkipped()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var importer = _fixture.CreateSqlServerImporter();

        await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        (await ConceptCountAsync(SystemUrl)).ShouldBe(4);
    }

    [Fact]
    public async Task GivenAReImportWithChangedContent_WhenImported_ThenThePreviousConceptsAreReplacedNotAdded()
    {
        // The procedure deletes the previous code system row first, and the FK cascade takes its concepts.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var importer = _fixture.CreateSqlServerImporter();
        await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        // Same package resource, different content: two concepts instead of four.
        packageResource.ResourceJson = TerminologyOracleFixture.FlatCodeSystemJson(SystemUrl, 2);
        await _fixture.ExecuteNonQueryAsync(
            $"UPDATE dbo.PackageResource SET ResourceJson = '{packageResource.ResourceJson.Replace("'", "''", StringComparison.Ordinal)}' " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        (await ConceptCountAsync(SystemUrl)).ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnImportedCodeSystem_WhenItCompletes_ThenThePackageRowRecordsTheOutcome()
    {
        await ImportAsync(SystemUrl);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE Canonical = '{SystemUrl}' ORDER BY PackageResourceId DESC", CancellationToken.None);
        var count = await _fixture.ExecuteScalarAsync<int>(
            "SELECT TOP 1 ImportedConceptCount FROM dbo.PackageResource " +
            $"WHERE Canonical = '{SystemUrl}' ORDER BY PackageResourceId DESC", CancellationToken.None);

        status.ShouldBe("Completed");
        count.ShouldBe(4);
    }

    [Fact]
    public async Task GivenMalformedJson_WhenImported_ThenTheFailureLandsOnThePackageRowRatherThanThrowing()
    {
        // Matching the EF contract: the status column is the error channel, not an exception.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, "{\"resourceType\":\"CodeSystem\",\"url\":\"" + SystemUrl + "\"}");

        var importer = _fixture.CreateSqlServerImporter();

        await Should.NotThrowAsync(() => importer.ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None));

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        // content is required, so extraction fails before anything is written.
        status.ShouldBe("Failed");
    }

    [Fact]
    public async Task GivenAWrongResourceType_WhenImportedAsACodeSystem_ThenItIsRejected()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", "http://example.org/fhir/ValueSet/wrong", "{\"resourceType\":\"ValueSet\"}");

        var importer = _fixture.CreateSqlServerImporter();

        await Should.ThrowAsync<ArgumentException>(
            () => importer.ImportCodeSystemAsync(
                _fixture.SystemPartitionId, packageResource, CancellationToken.None));
    }
}
