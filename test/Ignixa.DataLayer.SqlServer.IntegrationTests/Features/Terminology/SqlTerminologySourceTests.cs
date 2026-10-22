using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class SqlTerminologySourceTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/source-vehicles";
    private const string OtherSystemUrl = "http://example.org/fhir/CodeSystem/other-vehicles";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task ImportAsync(string version = "1.0.0")
    {
        var resource = JsonNode.Parse(TerminologyTestFixture.HierarchicalCodeSystemJson(SystemUrl))!;
        resource["version"] = version;

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, resource.ToJsonString());
        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
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
}
