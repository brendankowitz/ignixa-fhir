using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// Case is part of a FHIR code's identity, and these pin that the storage and every read path agree on it.
/// <para>
/// They could not have passed before the terminology code columns were given an explicit
/// <c>COLLATE Latin1_General_100_CS_AS</c>: without one, every <c>Code = @code</c> ran under the database's
/// default collation, which is case-insensitive on a stock SQL Server and on Azure SQL Database alike. The
/// import path had always compared codes case-sensitively (the composer filters with
/// <c>StringComparison.Ordinal</c>), so the same code was matched one way when written and another when
/// read — a wrong-case code validated as valid, and for UCUM that is a conformance defect rather than a
/// nicety, since <c>mg</c> and <c>MG</c> and <c>Gy</c> and <c>gy</c> are different units.
/// </para>
/// <para>
/// FHIR lets a CodeSystem declare <c>caseSensitive=false</c>, and <c>dbo.TermCodeSystem.CaseSensitive</c>
/// has always recorded that without any query reading it. Half of what follows pins the flag now being
/// honoured; the other half pins that honouring it has not become a blanket case-insensitive match.
/// </para>
/// </summary>
public class TerminologyCaseSensitivityTests : IAsyncLifetime
{
    private const string SensitiveSystemUrl = "http://example.org/fhir/CodeSystem/case-sensitive-units";
    private const string InsensitiveSystemUrl = "http://example.org/fhir/CodeSystem/case-insensitive-units";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    /// <summary>
    /// A CodeSystem whose codes differ from each other only by case where <paramref name="caseSensitive"/>
    /// is true, with a two-level hierarchy so <c>$subsumes</c> has a real parent walk. <c>Gy</c> (gray) and
    /// <c>gy</c> are the UCUM pair the defect was reported against.
    /// </summary>
    private static string UnitsCodeSystemJson(string url, bool caseSensitive) =>
        "{" +
        "\"resourceType\":\"CodeSystem\"," +
        $"\"url\":\"{url}\"," +
        "\"version\":\"1.0.0\"," +
        "\"status\":\"active\"," +
        "\"content\":\"complete\"," +
        "\"hierarchyMeaning\":\"is-a\"," +
        $"\"caseSensitive\":{(caseSensitive ? "true" : "false")}," +
        "\"concept\":[" +
        "{\"code\":\"unit\",\"display\":\"Unit\",\"concept\":[" +
        "{\"code\":\"Gy\",\"display\":\"gray\"}," +
        "{\"code\":\"mg\",\"display\":\"milligram\"}]}" +
        "]}";

    private async Task ImportCodeSystemAsync(string url, bool caseSensitive)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, UnitsCodeSystemJson(url, caseSensitive));

        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    private async Task<PackageResource> ImportValueSetAsync(string url, string codeSystemUrl, params string[] codes)
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", url, TerminologyTestFixture.ExpandedValueSetJson(url, codeSystemUrl, codes));

        await _fixture.CreateSqlServerImporter().ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        return packageResource;
    }

    [Fact]
    public async Task GivenACaseSensitiveCodeSystem_WhenValidatingACodeInTheWrongCase_ThenItIsNotValid()
    {
        const string valueSetUrl = "http://example.org/fhir/ValueSet/case-sensitive-units";

        await ImportCodeSystemAsync(SensitiveSystemUrl, caseSensitive: true);
        await ImportValueSetAsync(valueSetUrl, SensitiveSystemUrl, "Gy", "mg");

        var service = _fixture.CreateTerminologyService();

        // The control: the code as the CodeSystem spells it is valid, so a failure below is about case and
        // not about the ValueSet having failed to import.
        (await service.ValidateCodeAsync(SensitiveSystemUrl, "Gy", null, valueSetUrl, CancellationToken.None))
            .IsValid.ShouldBeTrue();

        var wrongCase = await service.ValidateCodeAsync(
            SensitiveSystemUrl, "gy", null, valueSetUrl, CancellationToken.None);

        wrongCase.IsValid.ShouldBeFalse();
        wrongCase.Severity.ShouldBe(IssueSeverity.Error);

        var wrongCaseUpper = await service.ValidateCodeAsync(
            SensitiveSystemUrl, "MG", null, valueSetUrl, CancellationToken.None);

        wrongCaseUpper.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenACaseInsensitiveCodeSystem_WhenValidatingACodeInTheWrongCase_ThenItIsValid()
    {
        const string valueSetUrl = "http://example.org/fhir/ValueSet/case-insensitive-units";

        await ImportCodeSystemAsync(InsensitiveSystemUrl, caseSensitive: false);
        await ImportValueSetAsync(valueSetUrl, InsensitiveSystemUrl, "Gy", "mg");

        var service = _fixture.CreateTerminologyService();

        // caseSensitive=false is the CodeSystem's own declaration that these are the same code, so the
        // storage being case-sensitive must not be allowed to turn it into a different one.
        (await service.ValidateCodeAsync(InsensitiveSystemUrl, "gy", null, valueSetUrl, CancellationToken.None))
            .IsValid.ShouldBeTrue();
        (await service.ValidateCodeAsync(InsensitiveSystemUrl, "MG", null, valueSetUrl, CancellationToken.None))
            .IsValid.ShouldBeTrue();

        // A code that is not in the ValueSet at all is still invalid -- the relaxed collation widens which
        // spellings match, not which codes exist.
        (await service.ValidateCodeAsync(InsensitiveSystemUrl, "spaceship", null, valueSetUrl, CancellationToken.None))
            .IsValid.ShouldBeFalse();
    }

    /// <summary>
    /// A ValueSet expansion whose displays deliberately share no substring with their codes, so a filter
    /// matching is unambiguously the Code column doing it. The shared
    /// <see cref="TerminologyTestFixture.ExpandedValueSetJson"/> gives every entry the display
    /// "Display &lt;code&gt;", which contains the code and would satisfy the filter through the Display
    /// branch no matter what the Code branch did.
    /// </summary>
    private static string DisjointDisplayValueSetJson(string url, string codeSystemUrl) =>
        "{" +
        "\"resourceType\":\"ValueSet\"," +
        $"\"url\":\"{url}\"," +
        "\"name\":\"DisjointDisplayValueSet\"," +
        "\"version\":\"1.0.0\"," +
        "\"status\":\"active\"," +
        "\"expansion\":{\"contains\":[" +
        $"{{\"system\":\"{codeSystemUrl}\",\"code\":\"Gy\",\"display\":\"radiation absorbed dose\"}}," +
        $"{{\"system\":\"{codeSystemUrl}\",\"code\":\"mg\",\"display\":\"one thousandth of a gram\"}}" +
        "]}}";

    [Fact]
    public async Task GivenAnExpansionFilterInADifferentCase_WhenExpanded_ThenItStillMatchesTheCode()
    {
        // $expand's filter is a text search a picker types into, not a code identity comparison, and it
        // matched case-insensitively against both Code and Display before any of this. Making code equality
        // case-sensitive must not narrow it, so the filter forces the insensitive collation back on
        // explicitly rather than inheriting whatever the column now has.
        const string valueSetUrl = "http://example.org/fhir/ValueSet/case-sensitive-filter";

        await ImportCodeSystemAsync(SensitiveSystemUrl, caseSensitive: true);

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "ValueSet", valueSetUrl, DisjointDisplayValueSetJson(valueSetUrl, SensitiveSystemUrl));
        await _fixture.CreateSqlServerImporter().ImportValueSetAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        var service = _fixture.CreateTerminologyService();

        var lowered = await service.ExpandValueSetAsync(
            new ExpansionParameters(valueSetUrl, Filter: "gy"), CancellationToken.None);

        lowered.ShouldNotBeNull();
        lowered.Total.ShouldBe(1);
        lowered.Contains.ShouldHaveSingleItem().Code.ShouldBe("Gy");

        var uppered = await service.ExpandValueSetAsync(
            new ExpansionParameters(valueSetUrl, Filter: "MG"), CancellationToken.None);

        uppered.ShouldNotBeNull();
        uppered.Total.ShouldBe(1);
        uppered.Contains.ShouldHaveSingleItem().Code.ShouldBe("mg");
    }

    [Fact]
    public async Task GivenBothKindsOfCodeSystem_WhenLookingUpACodeInTheWrongCase_ThenOnlyTheInsensitiveOneResolvesIt()
    {
        await ImportCodeSystemAsync(SensitiveSystemUrl, caseSensitive: true);
        await ImportCodeSystemAsync(InsensitiveSystemUrl, caseSensitive: false);

        var service = _fixture.CreateTerminologyService();

        (await service.LookupCodeAsync(SensitiveSystemUrl, "Gy", null, CancellationToken.None))
            .Found.ShouldBeTrue();
        (await service.LookupCodeAsync(SensitiveSystemUrl, "gy", null, CancellationToken.None))
            .Found.ShouldBeFalse();

        var insensitive = await service.LookupCodeAsync(
            InsensitiveSystemUrl, "gy", null, CancellationToken.None);

        insensitive.Found.ShouldBeTrue();

        // Resolved to the stored concept rather than merely reporting "found", which a fallback that matched
        // the wrong row would not do.
        insensitive.Display.ShouldBe("gray");
    }

    [Fact]
    public async Task GivenACaseInsensitiveCodeSystem_WhenSubsumesIsAskedInTheWrongCase_ThenItAnswersFromTheCodesItFound()
    {
        // THE MIXED-COMPARISON FIX. $subsumes used to select both codes in one
        // `Code IN (@codeA, @codeB)` query and then split the returned rows in C# with `==`, which is
        // ordinal. The two halves of one operation therefore disagreed about what equality means: SQL
        // returned the row, the C# predicate rejected it, and the operation answered "not-subsumed" with a
        // well-formed FHIR response and no indication anything had gone wrong. Both codes here are spelled
        // in a case the database does not store, so every row this reaches SQL through is one the old C#
        // predicate would have discarded.
        await ImportCodeSystemAsync(InsensitiveSystemUrl, caseSensitive: false);

        var service = _fixture.CreateTerminologyService();

        var parentToChild = await service.SubsumesAsync(
            new SubsumesParameters("UNIT", "GY", InsensitiveSystemUrl, null), CancellationToken.None);
        var childToParent = await service.SubsumesAsync(
            new SubsumesParameters("GY", "UNIT", InsensitiveSystemUrl, null), CancellationToken.None);
        var siblings = await service.SubsumesAsync(
            new SubsumesParameters("GY", "MG", InsensitiveSystemUrl, null), CancellationToken.None);

        parentToChild.Outcome.ShouldBe("subsumes");
        childToParent.Outcome.ShouldBe("subsumed-by");
        siblings.Outcome.ShouldBe("not-subsumed");

        // Two spellings of one code in a case-insensitive CodeSystem are the same concept, so they are
        // equivalent rather than unrelated.
        (await service.SubsumesAsync(new SubsumesParameters("GY", "gy", InsensitiveSystemUrl, null), CancellationToken.None))
            .Outcome.ShouldBe("equivalent");
    }

    [Fact]
    public async Task GivenACaseSensitiveCodeSystem_WhenSubsumesIsAskedInTheWrongCase_ThenItIsNotSubsumed()
    {
        await ImportCodeSystemAsync(SensitiveSystemUrl, caseSensitive: true);

        var service = _fixture.CreateTerminologyService();

        // The control for the test above: the same shape of question against a CodeSystem that has not
        // declared its codes case-insensitive must not resolve, or the fallback is a blanket relaxation.
        (await service.SubsumesAsync(new SubsumesParameters("unit", "Gy", SensitiveSystemUrl, null), CancellationToken.None))
            .Outcome.ShouldBe("subsumes");
        (await service.SubsumesAsync(new SubsumesParameters("UNIT", "GY", SensitiveSystemUrl, null), CancellationToken.None))
            .Outcome.ShouldBe("not-subsumed");
    }

    [Fact]
    public async Task GivenACodeSystemWithCodesDifferingOnlyByCase_WhenImported_ThenBothLandAndKeepTheirOwnParents()
    {
        // dbo.TermConceptList's own comment claims a CodeSystem with duplicate codes is tolerated rather
        // than rejected. Under a case-insensitive collation that was not true of a code system carrying both
        // `AB` and `ab`: UQ_TermConcept_CodeSystem_Code saw one code twice and took the whole import down.
        // And where only one of a pair existed, the parent-resolution join in dbo.ImportTermCodeSystem
        // matched `parent.Code = src.ParentCode` case-insensitively, linking a child to a parent whose code
        // differed only by case.
        const string url = "http://example.org/fhir/CodeSystem/mixed-case-codes";

        var json =
            "{" +
            "\"resourceType\":\"CodeSystem\"," +
            $"\"url\":\"{url}\"," +
            "\"version\":\"1.0.0\"," +
            "\"status\":\"active\"," +
            "\"content\":\"complete\"," +
            "\"hierarchyMeaning\":\"is-a\"," +
            "\"caseSensitive\":true," +
            "\"concept\":[" +
            "{\"code\":\"AB\",\"display\":\"Upper\",\"concept\":[{\"code\":\"AB-child\",\"display\":\"Upper child\"}]}," +
            "{\"code\":\"ab\",\"display\":\"Lower\",\"concept\":[{\"code\":\"ab-child\",\"display\":\"Lower child\"}]}" +
            "]}";

        var packageResource = await _fixture.SeedPackageResourceAsync("CodeSystem", url, json);
        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        var conceptCount = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TermConcept tc " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{url}'", CancellationToken.None);

        conceptCount.ShouldBe(4);

        var upperChildsParentDisplay = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 parent.Display FROM dbo.TermConcept child " +
            "JOIN dbo.TermConcept parent ON parent.TermConceptId = child.ParentConceptId " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = child.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{url}' AND child.Code = 'AB-child'", CancellationToken.None);

        var lowerChildsParentDisplay = await _fixture.ExecuteScalarAsync<string>(
            "SELECT TOP 1 parent.Display FROM dbo.TermConcept child " +
            "JOIN dbo.TermConcept parent ON parent.TermConceptId = child.ParentConceptId " +
            "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = child.TermCodeSystemId " +
            "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
            $"WHERE s.Value = '{url}' AND child.Code = 'ab-child'", CancellationToken.None);

        upperChildsParentDisplay.ShouldBe("Upper");
        lowerChildsParentDisplay.ShouldBe("Lower");
    }

    [Fact]
    public async Task GivenSystemUrisDifferingOnlyByCase_WhenResolved_ThenTheyAreDistinctSystems()
    {
        // dbo.System's clustered primary key is on Value, so under a case-insensitive collation two FHIR
        // system URIs differing only by case collapsed onto one SystemId -- and every concept, expansion
        // entry and token search-parameter row keyed on it with them. FHIR compares system URIs as
        // case-sensitive strings, and SqlServerSearchIndexReferenceDataCache's own in-memory map is keyed
        // ordinally, so the collation was the one place that disagreed.
        const string lower = "http://example.org/fhir/CodeSystem/casing";
        const string upper = "http://example.org/fhir/CodeSystem/CASING";

        await ImportCodeSystemAsync(lower, caseSensitive: true);
        await ImportCodeSystemAsync(upper, caseSensitive: true);

        var distinctSystemIds = await _fixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT SystemId) FROM dbo.System " +
            $"WHERE Value = '{lower}' COLLATE Latin1_General_100_CI_AS", CancellationToken.None);

        distinctSystemIds.ShouldBe(2);

        var service = _fixture.CreateTerminologyService();

        // Both resolve, and neither borrows the other's concepts.
        (await service.LookupCodeAsync(lower, "Gy", null, CancellationToken.None)).Found.ShouldBeTrue();
        (await service.LookupCodeAsync(upper, "Gy", null, CancellationToken.None)).Found.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAConceptMapOverACaseSensitiveCodeSystem_WhenTranslatingAWrongCaseCode_ThenNothingTranslates()
    {
        const string mapUrl = "http://example.org/fhir/ConceptMap/case-sensitive-units";
        const string targetSystemUrl = "http://example.org/fhir/CodeSystem/case-sensitive-targets";

        await ImportCodeSystemAsync(SensitiveSystemUrl, caseSensitive: true);

        var json =
            "{" +
            "\"resourceType\":\"ConceptMap\"," +
            $"\"url\":\"{mapUrl}\"," +
            "\"name\":\"CaseSensitiveUnitsMap\"," +
            "\"version\":\"1.0.0\"," +
            "\"status\":\"active\"," +
            "\"group\":[{" +
            $"\"source\":\"{SensitiveSystemUrl}\"," +
            $"\"target\":\"{targetSystemUrl}\"," +
            "\"element\":[{" +
            "\"code\":\"Gy\",\"display\":\"gray\"," +
            "\"target\":[{\"code\":\"GRAY\",\"display\":\"Gray\",\"equivalence\":\"equivalent\"}]" +
            "}]}]}";

        var packageResource = await _fixture.SeedPackageResourceAsync("ConceptMap", mapUrl, json);
        await _fixture.CreateSqlServerImporter().ImportConceptMapAsync(
            _fixture.SystemPartitionId, packageResource, CancellationToken.None);

        var service = _fixture.CreateTerminologyService();

        var exact = await service.TranslateCodeAsync(
            new TranslateParameters(mapUrl, null, "Gy", SensitiveSystemUrl, null, null, null, null),
            CancellationToken.None);

        exact.Result.ShouldBeTrue();
        exact.Matches.ShouldHaveSingleItem().Concept.Code.ShouldBe("GRAY");

        var wrongCase = await service.TranslateCodeAsync(
            new TranslateParameters(mapUrl, null, "gy", SensitiveSystemUrl, null, null, null, null),
            CancellationToken.None);

        wrongCase.Result.ShouldBeFalse();
        wrongCase.Matches.ShouldBeEmpty();
    }
}
