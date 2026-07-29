using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// Phase F Task 5b, second tranche: the terminology operations the first tranche did not reach, plus the
/// import behaviours Task 6 has to preserve. Recording what the <b>EF</b> implementation does, not what it
/// ought to do — where the two differ the comment says so.
/// </summary>
public class TerminologyOracleRemainingOpsTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/oracle-vehicles";
    private const string ValueSetUrl = "http://example.org/fhir/ValueSet/oracle-vehicles";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<Ignixa.Domain.Terminology.TerminologyImportResult> ImportCodeSystemAsync(string url)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, TerminologyOracleFixture.HierarchicalCodeSystemJson(url));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            return await importer.ImportCodeSystemAsync(
                _fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenAnUnexpandedValueSet_WhenExpanded_ThenNullIsReturnedRatherThanAnEmptyExpansion()
    {
        // ExpandValueSetAsync looks for a TermValueSet marked IsExpanded. A ValueSet that was never
        // imported is indistinguishable from one imported but not expanded: both yield null, and the
        // caller cannot tell which from the result alone.
        var service = _fixture.CreateTerminologyService();

        var result = await service.ExpandValueSetAsync(
            new ExpansionParameters(ValueSetUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenNoConceptMap_WhenACodeIsTranslated_ThenTheResultIsUnsuccessfulRatherThanThrowing()
    {
        await ImportCodeSystemAsync(SystemUrl);
        var service = _fixture.CreateTerminologyService();

        var result = await service.TranslateCodeAsync(
            new TranslateParameters(
                Url: "http://example.org/fhir/ConceptMap/never-imported",
                ConceptMapVersion: null,
                Code: "car",
                System: SystemUrl,
                Version: null,
                Source: null,
                Target: null,
                TargetSystem: null),
            CancellationToken.None);

        result.Result.ShouldBeFalse();
        result.Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenNoValueSet_WhenARequiredBindingIsValidated_ThenItFailsRatherThanPassingByDefault()
    {
        // A required binding against a ValueSet that does not exist must not silently pass. This is the
        // safety-relevant direction: failing open here would let unvalidated codes through.
        var service = _fixture.CreateTerminologyService();

        var result = await service.ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, SystemUrl, "car", null, null, CancellationToken.None);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnImportedCodeSystem_WhenTheImportStatusIsRequested_ThenItReportsCompleted()
    {
        await ImportCodeSystemAsync(SystemUrl);

        var status = await _fixture.GetImportStatusAsync(SystemUrl, CancellationToken.None);

        status.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenNothingImported_WhenTheImportStatusIsRequested_ThenNullIsReturned()
    {
        var status = await _fixture.GetImportStatusAsync(
            "http://example.org/fhir/CodeSystem/never-imported", CancellationToken.None);

        status.ShouldBeNull();
    }

    [Fact]
    public async Task GivenTheSameCodeSystemImportedTwice_WhenTheContentIsUnchanged_ThenConceptsAreNotDuplicated()
    {
        // Idempotency runs off a content hash plus a Completed status: an unchanged re-import short-circuits
        // before touching concepts. Without that, re-loading a package would multiply every concept.
        var url = SystemUrl;
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, TerminologyOracleFixture.HierarchicalCodeSystemJson(url));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
            await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var conceptCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{url}'", CancellationToken.None);

        conceptCount.ShouldBe(4);
    }

    [Fact]
    public async Task GivenAnImportedCodeSystem_WhenItCompletes_ThenThePackageRowRecordsTheOutcome()
    {
        // The import status and concept count live on dbo.PackageResource, which is how a partially
        // completed import is distinguishable from one that never started.
        await ImportCodeSystemAsync(SystemUrl);

        var status = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
            $"WHERE Canonical = '{SystemUrl}' ORDER BY PackageResourceId DESC", CancellationToken.None);

        var importedCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT TOP 1 ImportedConceptCount FROM dbo.PackageResource " +
            $"WHERE Canonical = '{SystemUrl}' ORDER BY PackageResourceId DESC", CancellationToken.None);

        status.ShouldBe("Completed");
        importedCount.ShouldBe(4);
    }

    [Fact]
    public async Task GivenACodeSystemAtTheBulkThreshold_WhenImported_ThenEveryConceptStillLands()
    {
        // The 1,000-concept threshold is the boundary the parent-resolution defect lives on: at or below it
        // the importer takes the AddRange path that never runs the second pass, above it the bulk path that
        // does. This pins the row count on the small side; Task 6 keeps both paths and the same threshold.
        var url = "http://example.org/fhir/CodeSystem/oracle-flat-1000";
        var json = TerminologyOracleFixture.FlatCodeSystemJson(url, conceptCount: 1000);

        var packageResource = await _fixture.SeedPackageResourceAsync("CodeSystem", url, json);

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var conceptCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{url}'", CancellationToken.None);

        conceptCount.ShouldBe(1000);
    }

    [Fact]
    public async Task GivenACodeSystemAboveTheBulkThreshold_WhenImported_ThenEveryConceptStillLands()
    {
        // The other side of the boundary: 1,001 concepts takes the SqlBulkCopy path. Row state must match
        // the small path's, which is the property Task 6's port has to preserve across both.
        var url = "http://example.org/fhir/CodeSystem/oracle-flat-1001";
        var json = TerminologyOracleFixture.FlatCodeSystemJson(url, conceptCount: 1001);

        var packageResource = await _fixture.SeedPackageResourceAsync("CodeSystem", url, json);

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }

        var conceptCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{url}'", CancellationToken.None);

        conceptCount.ShouldBe(1001);
    }

    [Fact]
    public async Task GivenANonValueSetResource_WhenImportedAsAValueSet_ThenItIsRejected()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await Should.ThrowAsync<ArgumentException>(
                () => importer.ImportValueSetAsync(
                    _fixture.SystemPartitionId, packageResource, CancellationToken.None));
        }
    }

    [Fact]
    public async Task GivenANonConceptMapResource_WhenImportedAsAConceptMap_ThenItIsRejected()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await Should.ThrowAsync<ArgumentException>(
                () => importer.ImportConceptMapAsync(
                    _fixture.SystemPartitionId, packageResource, CancellationToken.None));
        }
    }
}
