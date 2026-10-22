using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class SqlTerminologySourceTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/source-vehicles";
    private const string OtherSystemUrl = "http://example.org/fhir/CodeSystem/other-vehicles";
    private const string ValueSetUrl = "http://example.org/fhir/ValueSet/source-vehicles";
    private const string ConceptMapUrl = "http://example.org/fhir/ConceptMap/source-vehicles";
    private const string ChainSystemUrl = "http://example.org/fhir/CodeSystem/source-chain";
    private const string SharedCanonical = "http://example.org/fhir/shared-canonical";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task ImportAsync(string version = "1.0.0")
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, WithVersion(TerminologyTestFixture.HierarchicalCodeSystemJson(SystemUrl), version));
        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    private async Task ImportValueSetAsync(string version)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", ValueSetUrl,
            WithVersion(TerminologyTestFixture.ExpandedValueSetJson(ValueSetUrl, SystemUrl, "car"), version));
        await _fixture.CreateSqlServerImporter().ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    private async Task ImportConceptMapAsync(string version)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ConceptMap", ConceptMapUrl,
            WithVersion(TerminologyTestFixture.ConceptMapJson(ConceptMapUrl, SystemUrl, OtherSystemUrl), version));
        await _fixture.CreateSqlServerImporter().ImportConceptMapAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    private static string WithVersion(string json, string version)
    {
        var resource = JsonNode.Parse(json)!;
        resource["version"] = version;
        return resource.ToJsonString();
    }

    [Fact]
    public async Task GivenImportedCodeSystem_WhenCoverageAsked_ThenAuthoritative()
    {
        await ImportAsync();

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, new CanonicalRef(SystemUrl), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Fact]
    public async Task GivenNeverImportedCodeSystem_WhenCoverageAsked_ThenNone()
    {
        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, new CanonicalRef("http://example.org/never"), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.None);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenOtherVersionCoverageAsked_ThenNone()
    {
        await ImportAsync("1.0.0");

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, new CanonicalRef(SystemUrl, "2.0.0"), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.None);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenMatchingVersionCoverageAsked_ThenAuthoritative()
    {
        await ImportAsync("1.0.0");

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, new CanonicalRef(SystemUrl, "1.0.0"), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Fact]
    public async Task GivenImportedValueSetVersion_WhenMatchingVersionCoverageAsked_ThenAuthoritative()
    {
        await ImportValueSetAsync("1.0.0");

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.ValueSet, new CanonicalRef(ValueSetUrl, "1.0.0"), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Fact]
    public async Task GivenImportedConceptMapVersion_WhenMatchingVersionCoverageAsked_ThenAuthoritative()
    {
        await ImportConceptMapAsync("1.0.0");

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.ConceptMap, new CanonicalRef(ConceptMapUrl, "1.0.0"), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Fact]
    public async Task GivenImportedConcept_WhenLookedUp_ThenFoundWithDisplay()
    {
        await ImportAsync();

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(
            new CodeRef(SystemUrl, "car"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.True);
        lookup.Concept!.Display.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenImportedSystemWithoutCode_WhenLookedUp_ThenNotFound()
    {
        await ImportAsync();

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(
            new CodeRef(SystemUrl, "boat"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenOtherVersionLookedUp_ThenUndeterminedSystemVersionNotAvailable()
    {
        await ImportAsync("1.0.0");

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(
            new CodeRef(SystemUrl, "car", "2.0.0"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.Unknown);
        lookup.Unknown!.Kind.ShouldBe(UnknownKind.SystemVersionNotAvailable);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenKnownCodeLookedUp_ThenFound()
    {
        await ImportAsync("1.0.0");

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(
            new CodeRef(SystemUrl, "car", "1.0.0"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.True);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenUnknownCodeLookedUp_ThenNotFound()
    {
        await ImportAsync("1.0.0");

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(
            new CodeRef(SystemUrl, "boat", "1.0.0"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
    }

    [Fact]
    public async Task GivenUnknownCode_WhenSubsumesAsked_ThenUndeterminedCodeNotInSystem()
    {
        // Regression: this answered "not-subsumed", a false negative presented as a valid FHIR result.
        await ImportAsync();

        var outcome = await _fixture.CreateTerminologyService()
            .SubsumesAsync(new CodeRef(SystemUrl, "vehicle"), new CodeRef(SystemUrl, "boat"), CancellationToken.None);

        outcome.Unknown!.Kind.ShouldBe(UnknownKind.CodeNotInSystem);
        outcome.Unknown.Subject.ShouldBe($"{SystemUrl}#boat");
    }

    [Fact]
    public async Task GivenUnknownSystem_WhenSubsumesAsked_ThenUndeterminedSystemNotAvailable()
    {
        var outcome = await _fixture.CreateTerminologyService()
            .SubsumesAsync(
                new CodeRef("http://example.org/never", "a"),
                new CodeRef("http://example.org/never", "b"),
                CancellationToken.None);

        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenOtherVersionSubsumesAsked_ThenUndeterminedSystemVersionNotAvailable()
    {
        await ImportAsync("1.0.0");

        var outcome = await _fixture.CreateTerminologyService().SubsumesAsync(
            new CodeRef(SystemUrl, "vehicle", "1.0.0"),
            new CodeRef(SystemUrl, "car", "2.0.0"),
            CancellationToken.None);

        outcome.KnownRelation.ShouldBeNull();
        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemVersionNotAvailable);
    }

    [Fact]
    public async Task GivenImportedCodeSystemVersion_WhenKnownCodesSubsumesAsked_ThenSubsumes()
    {
        await ImportAsync("1.0.0");

        var outcome = await _fixture.CreateTerminologyService().SubsumesAsync(
            new CodeRef(SystemUrl, "vehicle", "1.0.0"),
            new CodeRef(SystemUrl, "car", "1.0.0"),
            CancellationToken.None);

        outcome.KnownRelation.ShouldBe(SubsumptionRelation.Subsumes);
    }

    [Fact]
    public async Task GivenDifferentSystemsWithSharedCode_WhenSubsumesAsked_ThenUndetermined()
    {
        await ImportAsync();

        var outcome = await _fixture.CreateTerminologyService().SubsumesAsync(
            new CodeRef(SystemUrl, "vehicle"),
            new CodeRef(OtherSystemUrl, "vehicle"),
            CancellationToken.None);

        outcome.KnownRelation.ShouldBeNull();
        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
        outcome.Unknown.Subject.ShouldBe(OtherSystemUrl);
    }

    [Theory]
    [InlineData("vehicle", "car", SubsumptionRelation.Subsumes)]
    [InlineData("car", "vehicle", SubsumptionRelation.SubsumedBy)]
    [InlineData("car", "truck", SubsumptionRelation.NotSubsumed)]
    [InlineData("car", "car", SubsumptionRelation.Equivalent)]
    public async Task GivenKnownCodes_WhenSubsumesAsked_ThenRelationIsDetermined(
        string a, string b, SubsumptionRelation expected)
    {
        await ImportAsync();

        var outcome = await _fixture.CreateTerminologyService()
            .SubsumesAsync(new CodeRef(SystemUrl, a), new CodeRef(SystemUrl, b), CancellationToken.None);

        outcome.KnownRelation.ShouldBe(expected);
    }

    [Fact]
    public async Task GivenHierarchyDeeperThanTheWalkBudget_WhenSubsumesAsked_ThenUndeterminedBudgetExceeded()
    {
        // Regression: the 50-level walk cap answered "not-subsumed" for an ancestor it had simply not reached.
        await ImportChainAsync(60);
        var service = _fixture.CreateTerminologyService();

        var withinBudget = await service.SubsumesAsync(
            new CodeRef(ChainSystemUrl, "c0"), new CodeRef(ChainSystemUrl, "c50"), CancellationToken.None);
        var beyondBudget = await service.SubsumesAsync(
            new CodeRef(ChainSystemUrl, "c0"), new CodeRef(ChainSystemUrl, "c51"), CancellationToken.None);
        var reversed = await service.SubsumesAsync(
            new CodeRef(ChainSystemUrl, "c51"), new CodeRef(ChainSystemUrl, "c0"), CancellationToken.None);

        withinBudget.KnownRelation.ShouldBe(SubsumptionRelation.Subsumes);
        beyondBudget.KnownRelation.ShouldBeNull();
        beyondBudget.Unknown!.Kind.ShouldBe(UnknownKind.BudgetExceeded);
        reversed.KnownRelation.ShouldBeNull();
        reversed.Unknown!.Kind.ShouldBe(UnknownKind.BudgetExceeded);
    }

    [Fact]
    public async Task GivenHierarchyCycle_WhenSubsumesAsked_ThenUndeterminedWithCycleReason()
    {
        // Regression: a parent cycle ended the walk as though it had reached the root, answering "not-subsumed".
        await ImportChainAsync(3);
        await _fixture.ExecuteNonQueryAsync($"""
            UPDATE child SET ParentConceptId = parent.TermConceptId
            FROM dbo.TermConcept child
            JOIN dbo.TermConcept parent ON parent.TermCodeSystemId = child.TermCodeSystemId
            JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = child.TermCodeSystemId
            JOIN dbo.PackageResource p ON p.PackageResourceId = cs.PackageResourceId
            WHERE p.Canonical = '{ChainSystemUrl}'
              AND ((child.Code = 'c1' AND parent.Code = 'c2') OR (child.Code = 'c2' AND parent.Code = 'c1'))
            """);

        var outcome = await _fixture.CreateTerminologyService().SubsumesAsync(
            new CodeRef(ChainSystemUrl, "c0"), new CodeRef(ChainSystemUrl, "c2"), CancellationToken.None);

        outcome.KnownRelation.ShouldBeNull();
        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SourceFailed);
        outcome.Unknown.Detail.ShouldContain("cycle");
    }

    [Theory]
    [InlineData(CanonicalKind.CodeSystem, SourceCoverage.None)]
    [InlineData(CanonicalKind.ConceptMap, SourceCoverage.None)]
    [InlineData(CanonicalKind.ValueSet, SourceCoverage.Authoritative)]
    public async Task GivenCanonicalImportedOnlyAsValueSet_WhenUnversionedCoverageAsked_ThenOnlyValueSetIsCovered(
        CanonicalKind kind, SourceCoverage expected)
    {
        // Regression: unversioned coverage asked whether the canonical had any usable content of any kind,
        // so an imported ValueSet made SQL authoritative for a CodeSystem it does not hold.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", SharedCanonical, TerminologyTestFixture.ExpandedValueSetJson(SharedCanonical, SystemUrl, "car"));
        await _fixture.CreateSqlServerImporter().ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(kind, new CanonicalRef(SharedCanonical), CancellationToken.None);

        coverage.ShouldBe(expected);
    }

    private Task SetImportStatusAsync(string canonical, string? status) =>
        _fixture.ExecuteNonQueryAsync(
            $"UPDATE dbo.PackageResource SET TerminologyImportStatus = {(status is null ? "NULL" : $"'{status}'")} " +
            $"WHERE Canonical = '{canonical}'");

    [Theory]
    [InlineData("Completed", false)]
    [InlineData("Failed", false)]
    [InlineData("Failed", true)]
    [InlineData("PartiallyCompleted", false)]
    [InlineData("PartiallyCompleted", true)]
    [InlineData("Skipped", false)]
    [InlineData("Skipped", true)]
    [InlineData("InProgress", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public async Task GivenActiveCodeSystemWithUsableContent_WhenCoverageAsked_ThenAuthoritativeRegardlessOfImportStatus(
        string? status, bool versioned)
    {
        // The import status is diagnostic: a rolled-back replacement leaves Failed on a row that still
        // serves the content it had, and coverage must follow the content (as GetImportStatusAsync does).
        await ImportAsync("1.0.0");
        await SetImportStatusAsync(SystemUrl, status);
        var canonical = versioned ? new CanonicalRef(SystemUrl, "1.0.0") : new CanonicalRef(SystemUrl);

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, canonical, CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Theory]
    [InlineData("Failed", false)]
    [InlineData("Failed", true)]
    [InlineData("PartiallyCompleted", false)]
    [InlineData("Skipped", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public async Task GivenActiveExpandedValueSet_WhenCoverageAsked_ThenAuthoritativeRegardlessOfImportStatus(
        string? status, bool versioned)
    {
        await ImportValueSetAsync("1.0.0");
        await SetImportStatusAsync(ValueSetUrl, status);
        var canonical = versioned ? new CanonicalRef(ValueSetUrl, "1.0.0") : new CanonicalRef(ValueSetUrl);

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.ValueSet, canonical, CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Theory]
    [InlineData("Failed", false)]
    [InlineData("Failed", true)]
    [InlineData(null, false)]
    public async Task GivenActiveConceptMapWithContent_WhenCoverageAsked_ThenAuthoritativeRegardlessOfImportStatus(
        string? status, bool versioned)
    {
        await ImportConceptMapAsync("1.0.0");
        await SetImportStatusAsync(ConceptMapUrl, status);
        var canonical = versioned ? new CanonicalRef(ConceptMapUrl, "1.0.0") : new CanonicalRef(ConceptMapUrl);

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.ConceptMap, canonical, CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Theory]
    [InlineData(CanonicalKind.CodeSystem, "Completed")]
    [InlineData(CanonicalKind.CodeSystem, "InProgress")]
    [InlineData(CanonicalKind.CodeSystem, "Skipped")]
    [InlineData(CanonicalKind.CodeSystem, "Failed")]
    [InlineData(CanonicalKind.CodeSystem, null)]
    [InlineData(CanonicalKind.ValueSet, "InProgress")]
    [InlineData(CanonicalKind.ValueSet, "Skipped")]
    [InlineData(CanonicalKind.ValueSet, "Failed")]
    [InlineData(CanonicalKind.ValueSet, null)]
    [InlineData(CanonicalKind.ConceptMap, "InProgress")]
    [InlineData(CanonicalKind.ConceptMap, "Skipped")]
    [InlineData(CanonicalKind.ConceptMap, "Failed")]
    [InlineData(CanonicalKind.ConceptMap, null)]
    public async Task GivenActiveRowWithoutUsableContent_WhenCoverageAsked_ThenNone(CanonicalKind kind, string? status)
    {
        // Without content there is nothing for SQL to be authoritative about, whatever the status says.
        string canonicalUrl;
        switch (kind)
        {
            case CanonicalKind.CodeSystem:
                await ImportAsync();
                canonicalUrl = SystemUrl;
                await _fixture.ExecuteNonQueryAsync(
                    "DELETE FROM dbo.TermConcept WHERE TermCodeSystemId IN " +
                    "(SELECT cs.TermCodeSystemId FROM dbo.TermCodeSystem cs " +
                    "JOIN dbo.PackageResource p ON p.PackageResourceId = cs.PackageResourceId " +
                    $"WHERE p.Canonical = '{canonicalUrl}')");
                break;
            case CanonicalKind.ValueSet:
                await ImportValueSetAsync("1.0.0");
                canonicalUrl = ValueSetUrl;
                await _fixture.ExecuteNonQueryAsync(
                    "UPDATE dbo.TermValueSet SET IsExpanded = 0 WHERE PackageResourceId IN " +
                    $"(SELECT PackageResourceId FROM dbo.PackageResource WHERE Canonical = '{canonicalUrl}')");
                break;
            default:
                await ImportConceptMapAsync("1.0.0");
                canonicalUrl = ConceptMapUrl;
                await _fixture.ExecuteNonQueryAsync(
                    "DELETE FROM dbo.TermConceptMap WHERE PackageResourceId IN " +
                    $"(SELECT PackageResourceId FROM dbo.PackageResource WHERE Canonical = '{canonicalUrl}')");
                break;
        }

        await SetImportStatusAsync(canonicalUrl, status);

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(kind, new CanonicalRef(canonicalUrl), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.None);
    }

    [Fact]
    public async Task GivenUnexpandedValueSet_WhenUnversionedCoverageAsked_ThenNone()
    {
        // A ValueSet without an expansion is not usable content and must not take routing from the package.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", ValueSetUrl, TerminologyTestFixture.ExpandedValueSetJson(ValueSetUrl, SystemUrl, "car"));
        await _fixture.CreateSqlServerImporter().ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
        await _fixture.ExecuteNonQueryAsync(
            $"UPDATE dbo.TermValueSet SET IsExpanded = 0 WHERE PackageResourceId = {packageResource.PackageResourceId}");

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.ValueSet, new CanonicalRef(ValueSetUrl), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.None);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task GivenWhitespaceVersion_WhenValidatingBindingWithDifferentDisplay_ThenNoConceptDisplayIsSuggested(string version)
    {
        // Guard: the legacy lookup filtered on the raw version, so a blank version matched no concept and no
        // display was suggested. CodeRef normalises blank to "no version", which would start suggesting one.
        await ImportAsync();
        await ImportValueSetAsync("1.0.0");
        var service = _fixture.CreateTerminologyService();

        var blank = await service.ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, SystemUrl, "car", "Display car", version, CancellationToken.None);
        var unversioned = await service.ValidateBindingAsync(
            ValueSetUrl, BindingStrength.Required, SystemUrl, "car", "Display car", null, CancellationToken.None);

        blank.ShouldBe(new BindingValidationResult(true, BindingStrength.Required, IssueSeverity.Information, "Code is valid", null));
        unversioned.SuggestedDisplay.ShouldBe("Car");
    }

    /// <summary>
    /// Imports concepts c0..c(n-1) and links each ci to parent c(i-1) directly in SQL, so c0 is the root and
    /// c(n-1) is n-1 levels below it. Built with SQL rather than nested JSON to control the exact depth.
    /// </summary>
    private async Task ImportChainAsync(int conceptCount)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", ChainSystemUrl, TerminologyTestFixture.FlatCodeSystemJson(ChainSystemUrl, conceptCount));
        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
        await _fixture.ExecuteNonQueryAsync($"""
            UPDATE child SET ParentConceptId = parent.TermConceptId
            FROM dbo.TermConcept child
            JOIN dbo.TermConcept parent ON parent.TermCodeSystemId = child.TermCodeSystemId
                AND parent.Code = CONCAT('c', CAST(SUBSTRING(child.Code, 2, 10) AS int) - 1)
            JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = child.TermCodeSystemId
            WHERE cs.PackageResourceId = {packageResource.PackageResourceId} AND child.Code <> 'c0'
            """);
    }
}
