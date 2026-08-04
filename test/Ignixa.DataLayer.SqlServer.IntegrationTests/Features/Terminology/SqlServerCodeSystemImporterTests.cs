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

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<Ignixa.Domain.Terminology.TerminologyImportResult> ImportAsync(string url, string? json = null)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, json ?? TerminologyTestFixture.HierarchicalCodeSystemJson(url));

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
        // THE FIX. Against the EF importer this same seed produced zero parent links: it chose between two
        // insert paths and ran parent resolution on only one, so every CodeSystem at or below 1,000 concepts
        // landed flat. That implementation and the oracle recording its behaviour are both deleted; the
        // defect is described in the commit that introduced dbo.ImportTermCodeSystem.
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

        await ImportAsync(atUrl, TerminologyTestFixture.FlatCodeSystemJson(atUrl, 1000));
        await ImportAsync(aboveUrl, TerminologyTestFixture.FlatCodeSystemJson(aboveUrl, 1001));

        (await ConceptCountAsync(atUrl)).ShouldBe(1000);
        (await ConceptCountAsync(aboveUrl)).ShouldBe(1001);
    }

    [Fact]
    public async Task GivenTheSameCodeSystemTwice_WhenTheContentIsUnchanged_ThenTheStatusStaysCompletedAndNoWorkIsRedone()
    {
        // The second pass must report Completed, not Skipped. Reporting Skipped was how the row's status got
        // downgraded from Completed, which both took $expand off the database for this CodeSystem --
        // HybridTerminologyService routes anything other than Completed to the in-memory fallback -- and
        // failed this very guard on the following load, re-importing in full every time.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyTestFixture.HierarchicalCodeSystemJson(SystemUrl));

        var importer = _fixture.CreateSqlServerImporter();

        var first = await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        var second = await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        first.Status.ShouldBe(Ignixa.Domain.Terminology.TerminologyImportStatus.Completed);
        first.ItemCount.ShouldBe(4);

        second.Status.ShouldBe(Ignixa.Domain.Terminology.TerminologyImportStatus.Completed);
        second.ItemCount.ShouldBe(0);

        (await ConceptCountAsync(SystemUrl)).ShouldBe(4);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Completed");
    }

    [Fact]
    public async Task GivenACodeSystemSkippedForItsOwnSake_WhenImportedAgain_ThenItStaysSkippedAndIsNotReconsidered()
    {
        // content=not-present will never import no matter how often it is retried, so Skipped has to satisfy
        // the unchanged-content guard as well as Completed does. While the guard matched only Completed,
        // every one of these was re-parsed and re-decided on every package load.
        var url = "http://example.org/fhir/CodeSystem/ported-skip-stays-skipped";

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, CodeSystemWithContentJson(url, "not-present"));

        var importer = _fixture.CreateSqlServerImporter();

        await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        var completedAfterFirst = await _fixture.ExecuteScalarAsync<DateTimeOffset>(
            "SELECT TOP 1 ImportCompletedDate FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        var second = await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        second.Status.ShouldBe(Ignixa.Domain.Terminology.TerminologyImportStatus.Skipped);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Skipped");
        (await ConceptCountAsync(url)).ShouldBe(0);

        // The load-bearing assertion: everything above holds whether or not the guard accepts Skipped,
        // because re-deciding the skip reaches the same outcome. RecordSkippedAsync re-stamps this timestamp
        // every time it runs, so an unmoved value is what proves the second pass short-circuited instead.
        var completedAfterSecond = await _fixture.ExecuteScalarAsync<DateTimeOffset>(
            "SELECT TOP 1 ImportCompletedDate FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        completedAfterSecond.ShouldBe(completedAfterFirst);
    }

    [Fact]
    public async Task GivenAReImportWithChangedContent_WhenImported_ThenThePreviousConceptsAreReplacedNotAdded()
    {
        // The procedure deletes the previous code system row first, and the FK cascade takes its concepts.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyTestFixture.HierarchicalCodeSystemJson(SystemUrl));

        var importer = _fixture.CreateSqlServerImporter();
        await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        // Same package resource, different content: two concepts instead of four.
        packageResource.ResourceJson = TerminologyTestFixture.FlatCodeSystemJson(SystemUrl, 2);
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

    private static string CodeSystemWithContentJson(string url, string content) =>
        "{\"resourceType\":\"CodeSystem\"," +
        $"\"url\":\"{url}\",\"version\":\"1.0.0\",\"status\":\"active\",\"content\":\"{content}\"," +
        "\"concept\":[{\"code\":\"car\",\"display\":\"Car\"}]}";

    [Fact]
    public async Task GivenACodeSystemSupplement_WhenImported_ThenItIsSkippedRatherThanShadowingTheRealCodeSystem()
    {
        // A supplement adds properties to concepts belonging to another CodeSystem and carries that
        // CodeSystem's url. Importing it as an ordinary CodeSystem puts a second TermCodeSystem row under the
        // same SystemId, and LookupCodeAsync breaks ties by ImportedDate DESC — so the supplement's concepts
        // would shadow the real ones. Both implementations skip supplements; merging them is unimplemented.
        await ImportAsync(SystemUrl);

        var supplement = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, CodeSystemWithContentJson(SystemUrl, "supplement"));

        var result = await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, supplement, CancellationToken.None);

        result.Status.ShouldBe(Ignixa.Domain.Terminology.TerminologyImportStatus.Skipped);

        // Still exactly one code system under this url, still the original four concepts.
        var codeSystemRows = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermCodeSystem cs " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{SystemUrl}'", CancellationToken.None);

        codeSystemRows.ShouldBe(1);
        (await ConceptCountAsync(SystemUrl)).ShouldBe(4);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {supplement.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Skipped");
    }

    [Fact]
    public async Task GivenACodeSystemWithContentNotPresent_WhenImported_ThenNothingIsImported()
    {
        var url = "http://example.org/fhir/CodeSystem/ported-not-present";

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, CodeSystemWithContentJson(url, "not-present"));

        var result = await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        result.Status.ShouldBe(Ignixa.Domain.Terminology.TerminologyImportStatus.Skipped);
        (await ConceptCountAsync(url)).ShouldBe(0);

        // The hash is stamped on the skip path too, so an unchanged package is not reconsidered forever.
        var hash = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 ISNULL(ContentHash, '') FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        hash.ShouldNotBeEmpty();
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
