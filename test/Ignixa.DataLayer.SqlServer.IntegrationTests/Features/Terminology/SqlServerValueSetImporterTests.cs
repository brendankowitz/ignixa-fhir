using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// The ported ValueSet importer: the oracle's facts, plus the compose-resolution behaviour the oracle could
/// not reach and the defects fixed on the way through.
/// <para>
/// Every compose case seeds its CodeSystem through the ported importer first, because compose resolution
/// reads concepts out of the database rather than the ValueSet — including the parent links that
/// <c>is-a</c> depends on, which is why these could not have passed before the CodeSystem import was fixed.
/// </para>
/// </summary>
public class SqlServerValueSetImporterTests : IAsyncLifetime
{
    private const string CodeSystemUrl = "http://example.org/fhir/CodeSystem/ported-vs-vehicles";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task SeedCodeSystemAsync()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", CodeSystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(CodeSystemUrl));

        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    private async Task<PackageResource> ImportValueSetAsync(string url, string json)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync("ValueSet", url, json);

        await _fixture.CreateSqlServerImporter().ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        return packageResource;
    }

    private async Task<IReadOnlyList<string>> ExpandedCodesAsync(string url)
    {
        var expansion = await _fixture.CreateTerminologyService().ExpandValueSetAsync(
            new ExpansionParameters(url), CancellationToken.None);

        return expansion is null
            ? []
            : expansion.Contains.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
    }

    private Task<int> ExpansionRowCountAsync(string url) => _fixture.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.TermValueSetExpansion e " +
        "JOIN dbo.TermValueSet vs ON vs.TermValueSetId = e.TermValueSetId " +
        $"WHERE vs.Canonical = '{url}'", CancellationToken.None);

    private static string ComposeValueSetJson(string url, string compose) =>
        "{" +
        "\"resourceType\":\"ValueSet\"," +
        $"\"url\":\"{url}\"," +
        "\"name\":\"PortedComposeValueSet\"," +
        "\"version\":\"1.0.0\"," +
        "\"status\":\"active\"," +
        $"\"compose\":{compose}" +
        "}";

    [Fact]
    public async Task GivenAValueSetWithAnExpansion_WhenImported_ThenItsCodesLandAndItIsMarkedExpanded()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-expanded";

        await ImportValueSetAsync(
            url, TerminologyOracleFixture.ExpandedValueSetJson(url, CodeSystemUrl, "car", "truck", "building"));

        (await ExpansionRowCountAsync(url)).ShouldBe(3);

        var isExpanded = await _fixture.ExecuteScalarAsync<bool>(
            $"SELECT TOP 1 IsExpanded FROM dbo.TermValueSet WHERE Canonical = '{url}'", CancellationToken.None);

        isExpanded.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAnImportedValueSet_WhenExpandedAndValidated_ThenTheReadPathAgrees()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-readback";

        await ImportValueSetAsync(
            url, TerminologyOracleFixture.ExpandedValueSetJson(url, CodeSystemUrl, "car", "truck"));

        (await ExpandedCodesAsync(url)).ShouldBe(["car", "truck"]);

        var service = _fixture.CreateTerminologyService();

        (await service.ValidateCodeAsync(CodeSystemUrl, "car", null, url, CancellationToken.None))
            .IsValid.ShouldBeTrue();
        (await service.ValidateCodeAsync(CodeSystemUrl, "spaceship", null, url, CancellationToken.None))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAValueSetWithoutAName_WhenImported_ThenItFailsOntoThePackageRowRatherThanThrowing()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-nameless";

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", url, "{\"resourceType\":\"ValueSet\",\"url\":\"" + url + "\",\"status\":\"active\"}");

        var importer = _fixture.CreateSqlServerImporter();

        await Should.NotThrowAsync(() => importer.ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None));

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);
        var error = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 ISNULL(ImportErrorMessage, '') FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Failed");
        error.ShouldContain("name is required");
    }

    [Fact]
    public async Task GivenAValueSetWithNoExpansionAtAll_WhenImported_ThenItIsStoredButNotMarkedExpanded()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-empty";

        await ImportValueSetAsync(
            url,
            "{\"resourceType\":\"ValueSet\",\"url\":\"" + url +
            "\",\"name\":\"NoExpansion\",\"status\":\"active\"}");

        var expanded = await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TermValueSet WHERE Canonical = '{url}' AND IsExpanded = 1",
            CancellationToken.None);

        expanded.ShouldBe(0);
        (await _fixture.CreateTerminologyService().ExpandValueSetAsync(
            new ExpansionParameters(url), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenAnExpansionWithNestedContains_WhenImported_ThenTheNestedCodesLandToo()
    {
        // THE FIX. The EF importer read expansion.contains one level deep, so a grouped expansion imported
        // as its group headers alone — and when those headers carried no code of their own, as nothing.
        const string url = "http://example.org/fhir/ValueSet/ported-nested";

        await ImportValueSetAsync(url,
            "{\"resourceType\":\"ValueSet\"," +
            $"\"url\":\"{url}\",\"name\":\"Nested\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"expansion\":{\"contains\":[" +
            "{\"display\":\"Vehicles\",\"contains\":[" +
            $"{{\"system\":\"{CodeSystemUrl}\",\"code\":\"car\",\"display\":\"Car\"}}," +
            $"{{\"system\":\"{CodeSystemUrl}\",\"code\":\"truck\",\"display\":\"Truck\"}}]}}," +
            $"{{\"system\":\"{CodeSystemUrl}\",\"code\":\"building\",\"display\":\"Building\"}}" +
            "]}}");

        // The grouping header itself carries no code and is not a member; its two children are.
        (await ExpandedCodesAsync(url)).ShouldBe(["building", "car", "truck"]);
    }

    [Fact]
    public async Task GivenAnExpansionEntryWithNoSystem_WhenImported_ThenItIsSkippedAndTheRestStillLand()
    {
        // THE FIX. TermValueSetExpansion.SystemId is a foreign key; the EF importer wrote id 0 for an entry
        // with no system, and no System row has id 0, so one such entry failed the entire import.
        const string url = "http://example.org/fhir/ValueSet/ported-systemless";

        await ImportValueSetAsync(url,
            "{\"resourceType\":\"ValueSet\"," +
            $"\"url\":\"{url}\",\"name\":\"Systemless\",\"version\":\"1.0.0\",\"status\":\"active\"," +
            "\"expansion\":{\"contains\":[" +
            "{\"code\":\"orphan\",\"display\":\"No system\"}," +
            $"{{\"system\":\"{CodeSystemUrl}\",\"code\":\"car\",\"display\":\"Car\"}}" +
            "]}}");

        (await ExpandedCodesAsync(url)).ShouldBe(["car"]);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE Canonical = '{url}' ORDER BY PackageResourceId DESC", CancellationToken.None);

        status.ShouldBe("Completed");
    }

    [Fact]
    public async Task GivenAComposeOfExplicitConcepts_WhenImported_ThenThoseCodesAreTheExpansion()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-compose-concepts";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{" +
            $"\"system\":\"{CodeSystemUrl}\"," +
            "\"concept\":[{\"code\":\"car\",\"display\":\"Car\"},{\"code\":\"truck\"}]}]}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["car", "truck"]);
    }

    [Fact]
    public async Task GivenAComposeIncludingAWholeSystem_WhenImported_ThenEveryConceptIsExpanded()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-compose-all";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            $"{{\"include\":[{{\"system\":\"{CodeSystemUrl}\"}}]}}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["building", "car", "truck", "vehicle"]);
    }

    [Fact]
    public async Task GivenAnIsAFilter_WhenComposed_ThenTheNamedCodeAndItsDescendantsAreIncluded()
    {
        // Only reachable now that dbo.ImportTermCodeSystem resolves parent links for every CodeSystem: this
        // filter walks ParentConceptId, and under the EF importer a four-concept CodeSystem imported flat,
        // so is-a matched nothing but the code itself.
        const string url = "http://example.org/fhir/ValueSet/ported-compose-isa";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{" +
            $"\"system\":\"{CodeSystemUrl}\"," +
            "\"filter\":[{\"property\":\"code\",\"op\":\"is-a\",\"value\":\"vehicle\"}]}]}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["car", "truck", "vehicle"]);
    }

    [Fact]
    public async Task GivenADescendentOfFilter_WhenComposed_ThenTheNamedCodeItselfIsExcluded()
    {
        // THE FIX. The EF implementation resolved descendent-of through the same helper as is-a, so both
        // returned the named code. They differ by exactly that one concept.
        const string url = "http://example.org/fhir/ValueSet/ported-compose-descendent";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{" +
            $"\"system\":\"{CodeSystemUrl}\"," +
            "\"filter\":[{\"property\":\"code\",\"op\":\"descendent-of\",\"value\":\"vehicle\"}]}]}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["car", "truck"]);
    }

    [Fact]
    public async Task GivenAnExcludedConcept_WhenComposed_ThenItIsRemovedFromTheExpansion()
    {
        const string url = "http://example.org/fhir/ValueSet/ported-compose-exclude";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{" + $"\"system\":\"{CodeSystemUrl}\"" + "}]," +
            "\"exclude\":[{" + $"\"system\":\"{CodeSystemUrl}\"," +
            "\"concept\":[{\"code\":\"building\"}]}]}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["car", "truck", "vehicle"]);
    }

    [Fact]
    public async Task GivenAnExcludeFilterThisServerCannotEvaluate_WhenComposed_ThenNothingIsExcludedAndTheExpansionSaysSo()
    {
        // THE FIX, and the worst of them. The EF implementation evaluated exclude filters through a switch
        // whose default arm simply broke, leaving the query unrestricted — so this compose, which means
        // "remove the vehicle sub-hierarchy", selected every concept in the CodeSystem and removed them all.
        // A property-based is-a is not exotic: it is how SNOMED subsumption filters are written.
        const string url = "http://example.org/fhir/ValueSet/ported-compose-unsupported";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{" + $"\"system\":\"{CodeSystemUrl}\"" + "}]," +
            "\"exclude\":[{" + $"\"system\":\"{CodeSystemUrl}\"," +
            "\"filter\":[{\"property\":\"concept\",\"op\":\"is-a\",\"value\":\"vehicle\"}]}]}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["building", "car", "truck", "vehicle"]);

        // Not silently: an operator this server cannot evaluate is reported rather than guessed at.
        var isPartial = await _fixture.ExecuteScalarAsync<bool>(
            $"SELECT TOP 1 IsPartialExpansion FROM dbo.TermValueSet WHERE Canonical = '{url}'",
            CancellationToken.None);
        var reason = await _fixture.ExecuteScalarAsync<string>(
            $"SELECT TOP 1 ISNULL(PartialExpansionReason, '') FROM dbo.TermValueSet WHERE Canonical = '{url}'",
            CancellationToken.None);

        isPartial.ShouldBeTrue();
        reason.ShouldContain("Filters not evaluated");
    }

    [Fact]
    public async Task GivenAComposeIncludingAnotherValueSet_WhenImported_ThenItsCodesAreCarriedOver()
    {
        const string sourceUrl = "http://example.org/fhir/ValueSet/ported-compose-source";
        const string url = "http://example.org/fhir/ValueSet/ported-compose-reference";

        await ImportValueSetAsync(
            sourceUrl, TerminologyOracleFixture.ExpandedValueSetJson(sourceUrl, CodeSystemUrl, "car", "truck"));

        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            $"{{\"include\":[{{\"valueSet\":[\"{sourceUrl}\"]}}]}}"));

        (await ExpandedCodesAsync(url)).ShouldBe(["car", "truck"]);
    }

    [Fact]
    public async Task GivenAComposeExcludingAValueSetThatIsNotExpanded_WhenImported_ThenTheExpansionIsMarkedPartial()
    {
        // THE FIX. The EF implementation reported an unresolvable include but passed over an unresolvable
        // exclude in silence — the more dangerous of the two, because the result contains codes that were
        // meant to be removed while claiming to be complete.
        const string url = "http://example.org/fhir/ValueSet/ported-compose-missing-exclude";

        await SeedCodeSystemAsync();
        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{" + $"\"system\":\"{CodeSystemUrl}\"" + "}]," +
            "\"exclude\":[{\"valueSet\":[\"http://example.org/fhir/ValueSet/never-imported\"]}]}"));

        (await ExpandedCodesAsync(url)).Count.ShouldBe(4);

        var isPartial = await _fixture.ExecuteScalarAsync<bool>(
            $"SELECT TOP 1 IsPartialExpansion FROM dbo.TermValueSet WHERE Canonical = '{url}'",
            CancellationToken.None);
        var reason = await _fixture.ExecuteScalarAsync<string>(
            $"SELECT TOP 1 ISNULL(PartialExpansionReason, '') FROM dbo.TermValueSet WHERE Canonical = '{url}'",
            CancellationToken.None);

        isPartial.ShouldBeTrue();
        reason.ShouldContain("never-imported");
    }

    [Fact]
    public async Task GivenAComposeOverAnUninstalledCodeSystem_WhenImported_ThenItIsExpandedButPartialAndEmpty()
    {
        // A ValueSet whose CodeSystem was never installed still counts as expanded, which is what keeps it
        // visible to ExpandValueSetAsync as an honest empty answer rather than as "never imported".
        const string url = "http://example.org/fhir/ValueSet/ported-compose-external";

        await ImportValueSetAsync(url, ComposeValueSetJson(url,
            "{\"include\":[{\"system\":\"http://snomed.info/sct\"}]}"));

        var expansion = await _fixture.CreateTerminologyService().ExpandValueSetAsync(
            new ExpansionParameters(url), CancellationToken.None);

        expansion.ShouldNotBeNull();
        expansion.Total.ShouldBe(0);
        expansion.Incomplete.ShouldBeTrue();

        var reason = await _fixture.ExecuteScalarAsync<string>(
            $"SELECT TOP 1 ISNULL(PartialExpansionReason, '') FROM dbo.TermValueSet WHERE Canonical = '{url}'",
            CancellationToken.None);

        reason.ShouldContain("External systems not imported");
    }

    [Fact]
    public async Task GivenTheSameValueSetTwice_WhenTheContentIsUnchanged_ThenTheStatusStaysCompleted()
    {
        // Was asserting Skipped. That is what overwrote a Completed row whose expansion was fully in the
        // database, and HybridTerminologyService reads that column to decide whether $expand may use the
        // database at all -- so this ValueSet silently fell back to in-memory expansion on every reload.
        const string url = "http://example.org/fhir/ValueSet/ported-reimport";

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", url, TerminologyOracleFixture.ExpandedValueSetJson(url, CodeSystemUrl, "car", "truck"));

        var importer = _fixture.CreateSqlServerImporter();

        await importer.ImportValueSetAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        var second = await importer.ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        second.Status.ShouldBe(Ignixa.Domain.Terminology.TerminologyImportStatus.Completed);
        second.ItemCount.ShouldBe(0);
        (await ExpansionRowCountAsync(url)).ShouldBe(2);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Completed");
    }
}
