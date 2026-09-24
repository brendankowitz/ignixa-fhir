using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// The ported ConceptMap importer, held to the oracle's facts plus the two defects fixed on the way through:
/// a group with no target failed the whole import on a foreign key violation, and an R5 map stored every
/// mapping as "equivalent" regardless of what it said.
/// </summary>
public class SqlServerConceptMapImporterTests : IAsyncLifetime
{
    private const string SourceSystemUrl = "http://example.org/fhir/CodeSystem/ported-cm-vehicles";
    private const string TargetSystemUrl = "http://example.org/fhir/CodeSystem/ported-cm-autos";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<PackageResource> ImportConceptMapAsync(string url, string json)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync("ConceptMap", url, json);

        await _fixture.CreateSqlServerImporter().ImportConceptMapAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        return packageResource;
    }

    private Task<int> ElementCountAsync(string url) => _fixture.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.TermConceptMapElement e " +
        "JOIN dbo.TermConceptMap cm ON cm.TermConceptMapId = e.TermConceptMapId " +
        $"WHERE cm.Canonical = '{url}'", CancellationToken.None);

    private static TranslateParameters Translate(string? url, string code, string system) => new(
        Url: url,
        ConceptMapVersion: null,
        Code: code,
        System: system,
        Version: null,
        Source: null,
        Target: null,
        TargetSystem: null);

    private static string ConceptMapJsonWithTargetCode(
        string url, string name, string sourceSystem, string targetSystem, string targetCode) =>
        "{" +
        "\"resourceType\":\"ConceptMap\"," +
        $"\"url\":\"{url}\"," +
        $"\"name\":\"{name}\"," +
        "\"version\":\"1.0.0\"," +
        "\"status\":\"active\"," +
        "\"group\":[{" +
        $"\"source\":\"{sourceSystem}\"," +
        $"\"target\":\"{targetSystem}\"," +
        "\"element\":[{" +
        "\"code\":\"car\"," +
        $"\"target\":[{{\"code\":\"{targetCode}\",\"equivalence\":\"equivalent\"}}]" +
        "}]}]}";

    [Fact]
    public async Task GivenAConceptMap_WhenImported_ThenItsElementsLandWithBothSystemsResolved()
    {
        const string url = "http://example.org/fhir/ConceptMap/ported-basic";

        await ImportConceptMapAsync(
            url, TerminologyTestFixture.ConceptMapJson(url, SourceSystemUrl, TargetSystemUrl));

        (await ElementCountAsync(url)).ShouldBe(1);

        var systemsCreated = await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value IN ('{SourceSystemUrl}', '{TargetSystemUrl}')",
            CancellationToken.None);

        systemsCreated.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnImportedConceptMap_WhenTranslatedBothWays_ThenEachDirectionAnswersFromItsOwnSide()
    {
        const string url = "http://example.org/fhir/ConceptMap/ported-translate";

        await ImportConceptMapAsync(
            url, TerminologyTestFixture.ConceptMapJson(url, SourceSystemUrl, TargetSystemUrl));

        var service = _fixture.CreateTerminologyService();

        var forward = await service.TranslateCodeAsync(
            Translate(url, "car", SourceSystemUrl), CancellationToken.None);
        var reverse = await service.TranslateCodeAsync(
            Translate(url, "auto", TargetSystemUrl) with { Reverse = true }, CancellationToken.None);

        forward.Result.ShouldBeTrue();
        forward.Matches.Count.ShouldBe(1);
        forward.Matches[0].Concept.Code.ShouldBe("auto");
        forward.Matches[0].Concept.System.ShouldBe(TargetSystemUrl);
        forward.Matches[0].Equivalence.ShouldBe("equivalent");

        reverse.Result.ShouldBeTrue();
        reverse.Matches[0].Concept.Code.ShouldBe("car");
    }

    [Fact]
    public async Task GivenTwoImportedConceptMapsMappingTheSameSourceCode_WhenTranslatedWithAUrl_ThenOnlyThatMapsMatchIsReturned()
    {
        // THE FIX. Without a WHERE on cm.Canonical, a url-scoped $translate returned mappings from every
        // imported ConceptMap that happened to map the same source system+code, not just the one the caller
        // named.
        const string urlA = "http://example.org/fhir/ConceptMap/ported-translate-scope-a";
        const string urlB = "http://example.org/fhir/ConceptMap/ported-translate-scope-b";

        await ImportConceptMapAsync(
            urlA, ConceptMapJsonWithTargetCode(urlA, "ScopeA", SourceSystemUrl, TargetSystemUrl, "autoA"));
        await ImportConceptMapAsync(
            urlB, ConceptMapJsonWithTargetCode(urlB, "ScopeB", SourceSystemUrl, TargetSystemUrl, "autoB"));

        var service = _fixture.CreateTerminologyService();

        var unscoped = await service.TranslateCodeAsync(
            Translate(null, "car", SourceSystemUrl), CancellationToken.None);
        unscoped.Matches.Count.ShouldBe(2);

        var scopedToA = await service.TranslateCodeAsync(
            Translate(urlA, "car", SourceSystemUrl), CancellationToken.None);

        scopedToA.Matches.Count.ShouldBe(1);
        scopedToA.Matches[0].Concept.Code.ShouldBe("autoA");
        scopedToA.Matches[0].Source.ShouldBe(urlA);
    }

    [Fact]
    public async Task GivenAGroupWithNoTargetSystem_WhenImported_ThenTheMappingStillLandsWithNoTargetSystem()
    {
        // THE FIX. TermConceptMapElement.TargetSystemId is a foreign key; the EF importer wrote id 0 when the
        // group declared no target, and no System row has id 0, so the whole ConceptMap failed to import.
        // The column is nullable precisely so a target code can outlive an undeclared system.
        const string url = "http://example.org/fhir/ConceptMap/ported-no-target-system";

        await ImportConceptMapAsync(url,
            "{\"resourceType\":\"ConceptMap\"," +
            $"\"url\":\"{url}\",\"name\":\"NoTargetSystem\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"group\":[{" + $"\"source\":\"{SourceSystemUrl}\"," +
            "\"element\":[{\"code\":\"car\",\"target\":[{\"code\":\"auto\",\"equivalence\":\"equivalent\"}]}]}]}");

        (await ElementCountAsync(url)).ShouldBe(1);

        var targetSystemId = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConceptMapElement e " +
            "JOIN dbo.TermConceptMap cm ON cm.TermConceptMapId = e.TermConceptMapId " +
            $"WHERE cm.Canonical = '{url}' AND e.TargetSystemId IS NULL", CancellationToken.None);

        targetSystemId.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAGroupWithNoSourceSystem_WhenImported_ThenTheFailureNamesTheMissingElement()
    {
        // Also a foreign key violation before, but this one genuinely cannot be stored: SourceSystemId is
        // NOT NULL. The change is what the caller is told — the group index and the reason, rather than a
        // constraint name.
        const string url = "http://example.org/fhir/ConceptMap/ported-no-source-system";

        var packageResource = await _fixture.SeedPackageResourceAsync("ConceptMap", url,
            "{\"resourceType\":\"ConceptMap\"," +
            $"\"url\":\"{url}\",\"name\":\"NoSourceSystem\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"group\":[{\"element\":[{\"code\":\"car\",\"target\":[{\"code\":\"auto\"}]}]}]}");

        var result = await _fixture.CreateSqlServerImporter().ImportConceptMapAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        result.Success.ShouldBeFalse();

        var error = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 ISNULL(ImportErrorMessage, '') FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        error.ShouldContain("group[0].source is required");
    }

    [Fact]
    public async Task GivenAnR5RelationshipInsteadOfEquivalence_WhenImported_ThenTheRelationshipIsStored()
    {
        // THE FIX. R5 renamed equivalence to relationship. Reading only the R4 spelling meant every R5
        // mapping was stored as "equivalent" — including ones that said the opposite.
        const string url = "http://example.org/fhir/ConceptMap/ported-r5-relationship";

        await ImportConceptMapAsync(url,
            "{\"resourceType\":\"ConceptMap\"," +
            $"\"url\":\"{url}\",\"name\":\"R5Relationship\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"group\":[{" + $"\"source\":\"{SourceSystemUrl}\",\"target\":\"{TargetSystemUrl}\"," +
            "\"element\":[{\"code\":\"car\",\"target\":[" +
            "{\"code\":\"auto\",\"relationship\":\"source-is-narrower-than-target\"}]}]}]}");

        var equivalence = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 e.Equivalence FROM dbo.TermConceptMapElement e " +
            "JOIN dbo.TermConceptMap cm ON cm.TermConceptMapId = e.TermConceptMapId " +
            $"WHERE cm.Canonical = '{url}'", CancellationToken.None);

        equivalence.ShouldBe("source-is-narrower-than-target");
    }

    [Fact]
    public async Task GivenAnElementWithNoTarget_WhenImported_ThenItIsKeptAsUnmatched()
    {
        // A deliberately unmapped code is kept as a row so the map can answer "no equivalent" rather than
        // "never heard of it".
        const string url = "http://example.org/fhir/ConceptMap/ported-unmapped";

        await ImportConceptMapAsync(url,
            "{\"resourceType\":\"ConceptMap\"," +
            $"\"url\":\"{url}\",\"name\":\"Unmapped\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"group\":[{" + $"\"source\":\"{SourceSystemUrl}\",\"target\":\"{TargetSystemUrl}\"," +
            "\"element\":[{\"code\":\"hovercraft\",\"display\":\"Hovercraft\"}]}]}");

        var equivalence = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 e.Equivalence FROM dbo.TermConceptMapElement e " +
            "JOIN dbo.TermConceptMap cm ON cm.TermConceptMapId = e.TermConceptMapId " +
            $"WHERE cm.Canonical = '{url}'", CancellationToken.None);

        equivalence.ShouldBe("unmatched");
    }

    [Fact]
    public async Task GivenAReImportWithChangedContent_WhenImported_ThenThePreviousElementsAreReplacedNotAdded()
    {
        const string url = "http://example.org/fhir/ConceptMap/ported-reimport";

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ConceptMap", url, TerminologyTestFixture.ConceptMapJson(url, SourceSystemUrl, TargetSystemUrl));

        var importer = _fixture.CreateSqlServerImporter();
        await importer.ImportConceptMapAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        packageResource.ResourceJson =
            "{\"resourceType\":\"ConceptMap\"," +
            $"\"url\":\"{url}\",\"name\":\"OracleConceptMap\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"group\":[{" + $"\"source\":\"{SourceSystemUrl}\",\"target\":\"{TargetSystemUrl}\"," +
            "\"element\":[" +
            "{\"code\":\"car\",\"target\":[{\"code\":\"auto\"}]}," +
            "{\"code\":\"truck\",\"target\":[{\"code\":\"lorry\"}]}]}]}";

        await _fixture.ExecuteNonQueryAsync(
            $"UPDATE dbo.PackageResource SET ResourceJson = '{packageResource.ResourceJson.Replace("'", "''", StringComparison.Ordinal)}' " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        await importer.ImportConceptMapAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);

        (await ElementCountAsync(url)).ShouldBe(2);
    }

    [Fact]
    public async Task GivenAWrongResourceType_WhenImportedAsAConceptMap_ThenItIsRejected()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", "http://example.org/fhir/ValueSet/ported-cm-wrong", "{\"resourceType\":\"ValueSet\"}");

        await Should.ThrowAsync<ArgumentException>(
            () => _fixture.CreateSqlServerImporter().ImportConceptMapAsync(
                _fixture.SystemPartitionId, packageResource, CancellationToken.None));
    }
}
