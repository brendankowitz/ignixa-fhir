using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// Phase F Task 5b — the terminology oracle for <c>SqlTerminologyService</c>. Runs against the <b>EF</b>
/// implementation to capture its behaviour before the port.
/// <para>
/// Each test takes a service with its own cache. <c>LookupCodeAsync</c> memoises on
/// <c>system|version|code</c> and returns before touching the database on a hit, so sharing one service
/// across cases would silently assert against the cache rather than the query under test.
/// </para>
/// </summary>
public class TerminologyOracleServiceTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/oracle-vehicles";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = await TerminologyOracleFixture.CreateAsync();
        await ImportHierarchyAsync();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task ImportHierarchyAsync()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var (importer, context) = await _fixture.CreateImporterAsync();
        await using (context)
        {
            await importer.ImportCodeSystemAsync(
                _fixture.SystemPartitionId, packageResource, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenAKnownCode_WhenLookedUp_ThenItIsFoundWithItsDisplay()
    {
        var service = _fixture.CreateTerminologyService();

        var result = await service.LookupCodeAsync(SystemUrl, "car", null, CancellationToken.None);

        result.Found.ShouldBeTrue();
        result.Display.ShouldBe("Car");
    }

    [Fact]
    public async Task GivenAnUnknownCodeInAKnownSystem_WhenLookedUp_ThenNotFoundIsReturnedRatherThanThrowing()
    {
        var service = _fixture.CreateTerminologyService();

        var result = await service.LookupCodeAsync(SystemUrl, "spaceship", null, CancellationToken.None);

        result.Found.ShouldBeFalse();
        result.Display.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAnUnknownSystem_WhenLookedUp_ThenNotFoundIsReturnedRatherThanThrowing()
    {
        // Unknown system and unknown code take different branches but must produce the same shape --
        // callers distinguish them by Found alone.
        var service = _fixture.CreateTerminologyService();

        var result = await service.LookupCodeAsync(
            "http://example.org/fhir/CodeSystem/never-imported", "car", null, CancellationToken.None);

        result.Found.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenBlankArguments_WhenLookedUp_ThenTheyAreRejected()
    {
        var service = _fixture.CreateTerminologyService();

        await Should.ThrowAsync<ArgumentException>(
            () => service.LookupCodeAsync("  ", "car", null, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(
            () => service.LookupCodeAsync(SystemUrl, "  ", null, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAConceptComparedToItself_WhenSubsumptionIsTested_ThenItIsEquivalent()
    {
        var service = _fixture.CreateTerminologyService();

        var result = await service.SubsumesAsync(
            new SubsumesParameters("car", "car", SystemUrl, null), CancellationToken.None);

        result.Outcome.ShouldBe("equivalent");
    }

    [Fact]
    public async Task GivenAParentAndItsChild_WhenSubsumptionIsTested_ThenNotSubsumedIsReturnedBecauseTheHierarchyWasNeverPersisted()
    {
        // RECORDS A DEFECT. The correct answer is "subsumes".
        //
        // SqlCodeSystemImporter resolves parent references in a second pass
        // (UpdateParentReferencesAsync) that only runs on the bulk path, taken when a CodeSystem exceeds
        // 1,000 concepts. At or below that threshold it calls AddRange on concepts whose ParentConceptId is
        // still null and never applies the parent map, so the hierarchy lands flat. SubsumesAsync then walks
        // parents that are not there and reports no relationship.
        //
        // The effect is silent: $subsumes returns a well-formed FHIR response for every concept pair in
        // every CodeSystem under 1,000 concepts -- which is most custom and IG CodeSystems -- and it is
        // simply wrong. See TerminologyOracleImportTests for the row-level version of the same finding.
        var service = _fixture.CreateTerminologyService();

        var result = await service.SubsumesAsync(
            new SubsumesParameters("vehicle", "car", SystemUrl, null), CancellationToken.None);

        result.Outcome.ShouldBe("not-subsumed");
    }

    [Fact]
    public async Task GivenAChildAndItsParent_WhenSubsumptionIsTested_ThenNotSubsumedIsReturnedBecauseTheHierarchyWasNeverPersisted()
    {
        // RECORDS A DEFECT. The correct answer is "subsumed-by". Same cause as the fact above.
        var service = _fixture.CreateTerminologyService();

        var result = await service.SubsumesAsync(
            new SubsumesParameters("car", "vehicle", SystemUrl, null), CancellationToken.None);

        result.Outcome.ShouldBe("not-subsumed");
    }

    [Fact]
    public async Task GivenTwoSiblings_WhenSubsumptionIsTested_ThenNeitherSubsumesTheOther()
    {
        // car and truck share a parent but neither is an ancestor of the other.
        var service = _fixture.CreateTerminologyService();

        var result = await service.SubsumesAsync(
            new SubsumesParameters("car", "truck", SystemUrl, null), CancellationToken.None);

        result.Outcome.ShouldBe("not-subsumed");
    }

    [Fact]
    public async Task GivenAnUnknownSystem_WhenSubsumptionIsTested_ThenNotSubsumedIsReturned()
    {
        // Unknown system, unknown code, and genuinely unrelated codes all collapse to the same outcome, so
        // a caller cannot distinguish "no such system" from "not related". That is the current contract.
        var service = _fixture.CreateTerminologyService();

        var result = await service.SubsumesAsync(
            new SubsumesParameters("car", "vehicle", "http://example.org/fhir/CodeSystem/never-imported", null),
            CancellationToken.None);

        result.Outcome.ShouldBe("not-subsumed");
    }

    [Fact]
    public async Task GivenAnUnknownCode_WhenSubsumptionIsTested_ThenNotSubsumedIsReturned()
    {
        var service = _fixture.CreateTerminologyService();

        var result = await service.SubsumesAsync(
            new SubsumesParameters("vehicle", "spaceship", SystemUrl, null), CancellationToken.None);

        result.Outcome.ShouldBe("not-subsumed");
    }

    [Fact]
    public async Task GivenBlankSubsumptionArguments_WhenTested_ThenTheyAreRejected()
    {
        var service = _fixture.CreateTerminologyService();

        await Should.ThrowAsync<ArgumentException>(
            () => service.SubsumesAsync(new SubsumesParameters("  ", "car", SystemUrl, null), CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(
            () => service.SubsumesAsync(new SubsumesParameters("car", "  ", SystemUrl, null), CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(
            () => service.SubsumesAsync(new SubsumesParameters("car", "vehicle", "  ", null), CancellationToken.None));
    }

    [Fact]
    public async Task GivenNoValueSet_WhenACodeIsValidated_ThenValidationIsRefusedRatherThanCheckedAgainstTheCodeSystem()
    {
        // There is no CodeSystem-only validation path: ValidateCodeAsync returns before touching the
        // database unless a ValueSet URL is supplied. A caller holding a system and code but no ValueSet
        // cannot validate it here, and gets IsValid=false rather than an argument exception -- so the
        // refusal is indistinguishable from "the code is invalid" unless the caller reads Message.
        var service = _fixture.CreateTerminologyService();

        var result = await service.ValidateCodeAsync(SystemUrl, "car", null, null, CancellationToken.None);

        result.IsValid.ShouldBeFalse();
        result.Message.ShouldBe("ValueSet URL is required");
    }

    [Fact]
    public async Task GivenNoCode_WhenValidated_ThenTheMissingCodeIsReportedRatherThanThrown()
    {
        var service = _fixture.CreateTerminologyService();

        var result = await service.ValidateCodeAsync(
            SystemUrl, null, null, "http://example.org/fhir/ValueSet/anything", CancellationToken.None);

        result.IsValid.ShouldBeFalse();
        result.Message.ShouldBe("Code is required");
    }
}
