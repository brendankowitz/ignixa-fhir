using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// Phase F Task 5b, third tranche: round-trip coverage for ValueSet and ConceptMap import, which the first
/// two tranches only covered by their wrong-resource-type rejections. Without these, porting
/// <c>ImportValueSetAsync</c> and <c>ImportConceptMapAsync</c> — and the compose-expansion logic behind
/// them — would be unverifiable.
/// <para>
/// Recording what the <b>EF</b> implementation does, before the port.
/// </para>
/// </summary>
public class TerminologyOracleValueSetConceptMapTests : IAsyncLifetime
{
    private const string CodeSystemUrl = "http://example.org/fhir/CodeSystem/oracle-vehicles";
    private const string ValueSetUrl = "http://example.org/fhir/ValueSet/oracle-vehicles";
    private const string ConceptMapUrl = "http://example.org/fhir/ConceptMap/oracle-vehicles";
    private const string TargetSystemUrl = "http://example.org/fhir/CodeSystem/oracle-autos";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GivenAValueSetWithAnExpansion_WhenImported_ThenItsCodesLandAndItIsMarkedExpanded()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet",
            ValueSetUrl,
            TerminologyOracleFixture.ExpandedValueSetJson(ValueSetUrl, CodeSystemUrl, "car", "truck", "building"));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportValueSetAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var codeCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermValueSetExpansion e " +
            "JOIN dbo.TermValueSet vs ON vs.TermValueSetId = e.TermValueSetId " +
            $"WHERE vs.Canonical = '{ValueSetUrl}'", CancellationToken.None);

        codeCount.ShouldBe(3);

        // IsExpanded gates every read path: ExpandValueSetAsync and ValidateCodeAsync both require it, so a
        // ValueSet imported without it is invisible to them.
        var isExpanded = await _fixture.ExecuteScalarAsync<bool>(
            $"SELECT TOP 1 IsExpanded FROM dbo.TermValueSet WHERE Canonical = '{ValueSetUrl}'",
            CancellationToken.None);

        isExpanded.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAnImportedValueSet_WhenExpanded_ThenTheServiceReturnsItsCodes()
    {
        // The read path's view of the same data: import through the importer, read back through the
        // service. This is the pairing the port has to keep working across both halves.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet",
            ValueSetUrl,
            TerminologyOracleFixture.ExpandedValueSetJson(ValueSetUrl, CodeSystemUrl, "car", "truck"));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportValueSetAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var service = _fixture.CreateTerminologyService();
        var expansion = await service.ExpandValueSetAsync(
            new ExpansionParameters(ValueSetUrl), CancellationToken.None);

        expansion.ShouldNotBeNull();
        expansion.Total.ShouldBe(2);
        expansion.Contains.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal)
            .ShouldBe(["car", "truck"]);
    }

    [Fact]
    public async Task GivenAnImportedValueSet_WhenACodeInItIsValidated_ThenItIsValid()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet",
            ValueSetUrl,
            TerminologyOracleFixture.ExpandedValueSetJson(ValueSetUrl, CodeSystemUrl, "car"));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportValueSetAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var service = _fixture.CreateTerminologyService();

        var valid = await service.ValidateCodeAsync(CodeSystemUrl, "car", null, ValueSetUrl, CancellationToken.None);
        var invalid = await service.ValidateCodeAsync(CodeSystemUrl, "spaceship", null, ValueSetUrl, CancellationToken.None);

        valid.IsValid.ShouldBeTrue();
        invalid.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAValueSetWithoutAName_WhenImported_ThenItFailsOntoThePackageRowRatherThanThrowing()
    {
        // ValueSet.name is mandatory to the importer even though FHIR treats it as optional, so a
        // conformant ValueSet without one cannot be imported. The interesting part is how that surfaces:
        // ImportValueSetAsync catches every exception, records TerminologyImportStatus = 'Failed' with the
        // message on dbo.PackageResource, and returns a failed result. The status column is the error
        // channel, so a caller that ignores the result sees no exception at all.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet",
            ValueSetUrl,
            "{\"resourceType\":\"ValueSet\",\"url\":\"" + ValueSetUrl + "\",\"status\":\"active\"}");

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await Should.NotThrowAsync(
                () => importer.ImportValueSetAsync(
                    _fixture.SystemPartitionId, packageResource, CancellationToken.None));
        }

        var status = await _fixture.ExecuteScalarAsync<string>(
            $"SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        var error = await _fixture.ExecuteScalarAsync<string>(
            $"SELECT TOP 1 ISNULL(ImportErrorMessage, '') FROM dbo.PackageResource " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        status.ShouldBe("Failed");
        error.ShouldContain("name is required");
    }

    [Fact]
    public async Task GivenAValueSetWithNoExpansionAtAll_WhenImported_ThenItIsStoredButNotMarkedExpanded()
    {
        // No expansion and no compose: the ValueSet row is created but IsExpanded stays false, which makes
        // it invisible to ExpandValueSetAsync and ValidateCodeAsync. Importing is therefore not the same as
        // being usable, and nothing in the result distinguishes the two.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet",
            ValueSetUrl,
            "{\"resourceType\":\"ValueSet\",\"url\":\"" + ValueSetUrl +
            "\",\"name\":\"NoExpansion\",\"status\":\"active\"}");

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportValueSetAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var isExpanded = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermValueSet " +
            $"WHERE Canonical = '{ValueSetUrl}' AND IsExpanded = 1", CancellationToken.None);

        isExpanded.ShouldBe(0);

        var service = _fixture.CreateTerminologyService();
        var expansion = await service.ExpandValueSetAsync(
            new ExpansionParameters(ValueSetUrl), CancellationToken.None);

        expansion.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAConceptMap_WhenImported_ThenItsElementsLandWithBothSystemsResolved()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ConceptMap",
            ConceptMapUrl,
            TerminologyOracleFixture.ConceptMapJson(ConceptMapUrl, CodeSystemUrl, TargetSystemUrl));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportConceptMapAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var elementCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConceptMapElement e " +
            "JOIN dbo.TermConceptMap cm ON cm.TermConceptMapId = e.TermConceptMapId " +
            $"WHERE cm.Canonical = '{ConceptMapUrl}'", CancellationToken.None);

        elementCount.ShouldBe(1);

        // Group source and target URLs are resolved through ISystemRepository.GetOrCreateAsync during
        // import, so importing a ConceptMap creates system rows as a side effect.
        var systemsCreated = await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value IN ('{CodeSystemUrl}', '{TargetSystemUrl}')",
            CancellationToken.None);

        systemsCreated.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnImportedConceptMap_WhenACodeIsTranslated_ThenTheMappingIsReturned()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ConceptMap",
            ConceptMapUrl,
            TerminologyOracleFixture.ConceptMapJson(ConceptMapUrl, CodeSystemUrl, TargetSystemUrl));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportConceptMapAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var service = _fixture.CreateTerminologyService();

        var result = await service.TranslateCodeAsync(
            new TranslateParameters(
                Url: ConceptMapUrl,
                ConceptMapVersion: null,
                Code: "car",
                System: CodeSystemUrl,
                Version: null,
                Source: null,
                Target: null,
                TargetSystem: null),
            CancellationToken.None);

        result.Result.ShouldBeTrue();
        result.Matches.Count.ShouldBe(1);
        result.Matches[0].Concept.Code.ShouldBe("auto");
        result.Matches[0].Concept.System.ShouldBe(TargetSystemUrl);
        result.Matches[0].Equivalence.ShouldBe("equivalent");
    }

    [Fact]
    public async Task GivenAnImportedConceptMap_WhenTranslatedInReverse_ThenTheSourceSideIsReturned()
    {
        // Reverse swaps which side of the mapping the supplied code matches against, so the same data
        // answers a different question. Pinned because the port builds the two directions from different
        // columns and could silently transpose them.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ConceptMap",
            ConceptMapUrl,
            TerminologyOracleFixture.ConceptMapJson(ConceptMapUrl, CodeSystemUrl, TargetSystemUrl));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportConceptMapAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var service = _fixture.CreateTerminologyService();

        var result = await service.TranslateCodeAsync(
            new TranslateParameters(
                Url: ConceptMapUrl,
                ConceptMapVersion: null,
                Code: "auto",
                System: TargetSystemUrl,
                Version: null,
                Source: null,
                Target: null,
                TargetSystem: null)
            { Reverse = true },
            CancellationToken.None);

        result.Result.ShouldBeTrue();
        result.Matches[0].Concept.Code.ShouldBe("car");
    }
}
