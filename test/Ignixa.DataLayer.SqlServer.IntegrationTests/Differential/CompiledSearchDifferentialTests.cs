using Ignixa.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

/// <summary>
/// Proves the compiler-driven <see cref="Ignixa.DataLayer.SqlServer.Search.SqlServerCompiledSearchService"/>
/// (Tasks 8-10) agrees with the legacy EF-based <see cref="Ignixa.DataLayer.SqlEntityFramework.Search.SqlEntityFrameworkSearchService"/>
/// on every leaf/composite search-parameter type, <c>:missing</c>, and count -- the first of 3
/// differential-search harness tasks (chain/include/compartment is Task 12, sort/paging is Task 13).
/// Every test below uses a hand-built <see cref="SearchIndexEntry"/> (never a real
/// <c>ISearchIndexer</c>), matching <c>SqlServerCompiledSearchServiceSortTests.cs</c>'s established
/// pattern -- both write paths key row generation off <see cref="ResourceWrapper.SearchIndices"/>
/// directly, so this is what production indexing produces too, just without needing FHIRPath
/// evaluation over real resource content for a test that only cares about the search-index tables.
/// </summary>
public class CompiledSearchDifferentialTests
{
    // Pure, I/O-free lookup structure over the pre-generated R4 catalog (matches every
    // FhirVersion.R4-hardcoded fixture elsewhere in this project) -- shared across every test in this
    // class purely for its resolved SearchParameterInfo/Component data, never for any per-test state.
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private static async Task CreateResourceAsync(
        DifferentialTestHarness harness,
        string resourceType,
        string resourceId,
        IReadOnlyList<object>? searchIndices,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceWrapper(
            resourceType,
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"{resourceType}/{resourceId}"))
        {
            SearchIndices = searchIndices,
        };

        await harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, cancellationToken);
        await harness.NewRepository.CreateOrUpdateAsync(resource with { }, cancellationToken);
    }

    private static async Task<List<SearchEntryResult>> CollectAsync(IAsyncEnumerable<SearchEntryResult> results)
    {
        var list = new List<SearchEntryResult>();
        await foreach (var result in results)
        {
            list.Add(result);
        }

        return list;
    }

    private static void AssertSameResults(IReadOnlyList<SearchEntryResult> legacy, IReadOnlyList<SearchEntryResult> @new)
    {
        legacy.Count.ShouldBe(@new.Count);
        var legacyIds = legacy.Select(r => (r.ResourceType, r.ResourceId)).OrderBy(x => x).ToList();
        var newIds = @new.Select(r => (r.ResourceType, r.ResourceId)).OrderBy(x => x).ToList();
        legacyIds.ShouldBe(newIds);
    }

    [Fact]
    public async Task GivenAStringSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!], CancellationToken.None);

        var matchId = $"diff-string-match-{Guid.NewGuid():N}";
        var otherId = $"diff-string-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Anderson"))], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Baker"))], CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                familyParam,
                new SearchParameterPredicateExpression(familyParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Anderson"))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenATokenSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var identifierParam = ParameterManager.GetSearchParameter("Patient", "identifier");
        await harness.SeedSearchParameterCatalogAsync([identifierParam.Url!], CancellationToken.None);

        var matchId = $"diff-token-match-{Guid.NewGuid():N}";
        var otherId = $"diff-token-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchId,
            [new SearchIndexEntry(identifierParam, new TokenSearchValue(system: "http://example.org/mrn", code: "12345", text: null))], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherId,
            [new SearchIndexEntry(identifierParam, new TokenSearchValue(system: "http://example.org/mrn", code: "67890", text: null))], CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                identifierParam,
                new SearchParameterPredicateExpression(identifierParam, SearchComparator.Eq, modifier: null,
                    new TokenSearchValue(system: "http://example.org/mrn", code: "12345", text: null))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenAReferenceSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        await harness.SeedSearchParameterCatalogAsync([subjectParam.Url!], CancellationToken.None);

        var patientId = $"diff-ref-patient-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-ref-other-patient-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", patientId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId, null, CancellationToken.None);

        var matchObservationId = $"diff-ref-match-{Guid.NewGuid():N}";
        var otherObservationId = $"diff-ref-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: patientId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: otherPatientId))],
            CancellationToken.None);

        // ReferenceKind.InternalOrExternal on the query side (not .Internal, which the index side used
        // above) -- legacy's SearchValueExpressionBuilderHelper.Visit(ReferenceSearchValue) lowers
        // Kind == Internal into Expression.And(Expression.Missing(FieldName.ReferenceBaseUri), ...), and
        // SearchParameterQueryGenerator.ProcessExpressionAsync has no case for the resulting
        // MissingFieldExpression node -- a genuine pre-existing legacy gap (confirmed: SearchValueExpressionBuilderHelper.cs:168-173
        // vs. SearchParameterQueryGenerator.cs's MissingFieldExpression handling, which only special-cases
        // FieldName.TokenSystem, never FieldName.ReferenceBaseUri), unrelated to Ignixa.Search.Sql.
        // InternalOrExternal emits no BaseUri predicate at all on either engine (ReferenceColumnEquality.cs's
        // "or vice versa" reconciliation), so it still matches the Internal-kind indexed row above correctly.
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                subjectParam,
                new SearchParameterPredicateExpression(subjectParam, SearchComparator.Eq, modifier: null,
                    new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: "Patient", resourceId: patientId))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchObservationId]);
    }

    [Fact]
    public async Task GivenAUriSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var urlParam = ParameterManager.GetSearchParameter("ValueSet", "url");
        await harness.SeedSearchParameterCatalogAsync([urlParam.Url!], CancellationToken.None);

        var matchId = $"diff-uri-match-{Guid.NewGuid():N}";
        var otherId = $"diff-uri-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "ValueSet", matchId,
            [new SearchIndexEntry(urlParam, new UriSearchValue("http://example.org/fhir/ValueSet/match", separateCanonicalComponents: false))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "ValueSet", otherId,
            [new SearchIndexEntry(urlParam, new UriSearchValue("http://example.org/fhir/ValueSet/other", separateCanonicalComponents: false))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "ValueSet",
            Expression = new SearchParameterExpression(
                urlParam,
                new SearchParameterPredicateExpression(urlParam, SearchComparator.Eq, modifier: null,
                    new UriSearchValue("http://example.org/fhir/ValueSet/match", separateCanonicalComponents: false))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenANumberSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var probabilityParam = ParameterManager.GetSearchParameter("RiskAssessment", "probability");
        await harness.SeedSearchParameterCatalogAsync([probabilityParam.Url!], CancellationToken.None);

        var matchId = $"diff-number-match-{Guid.NewGuid():N}";
        var otherId = $"diff-number-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "RiskAssessment", matchId,
            [new SearchIndexEntry(probabilityParam, new NumberSearchValue(0.75m))], CancellationToken.None);
        await CreateResourceAsync(harness, "RiskAssessment", otherId,
            [new SearchIndexEntry(probabilityParam, new NumberSearchValue(0.25m))], CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "RiskAssessment",
            Expression = new SearchParameterExpression(
                probabilityParam,
                new SearchParameterPredicateExpression(probabilityParam, SearchComparator.Eq, modifier: null, new NumberSearchValue(0.75m))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenAQuantitySearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var quantityParam = ParameterManager.GetSearchParameter("Observation", "value-quantity");
        await harness.SeedSearchParameterCatalogAsync([quantityParam.Url!], CancellationToken.None);

        var matchId = $"diff-quantity-match-{Guid.NewGuid():N}";
        var otherId = $"diff-quantity-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchId,
            [new SearchIndexEntry(quantityParam, new QuantitySearchValue(system: "http://unitsofmeasure.org", code: "mg", 120m))], CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherId,
            [new SearchIndexEntry(quantityParam, new QuantitySearchValue(system: "http://unitsofmeasure.org", code: "mg", 60m))], CancellationToken.None);

        // Ge (not Eq): legacy's SearchValueExpressionBuilderHelper.Visit(QuantitySearchValue) builds Eq as
        // Expression.And(system, code, Expression.And(GreaterThanOrEqual, LessThanOrEqual)) -- the value
        // range is itself a NESTED MultiaryExpression. SearchParameterQueryGenerator.IsQuantityAndExpression/
        // GenerateQuantityAndQueryAsync only look for a direct BinaryExpression child with FieldName.Quantity
        // to extract the value bound; a nested MultiaryExpression child doesn't match, so the value
        // constraint is silently dropped and only system+code are applied -- a genuine pre-existing legacy
        // bug (confirmed empirically: Eq matched both a 120mg and a 60mg row here), unrelated to
        // Ignixa.Search.Sql. Ge's GenerateNumberExpression case returns a single flat BinaryExpression, so
        // it takes the special-case path correctly and isn't affected.
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                quantityParam,
                new SearchParameterPredicateExpression(quantityParam, SearchComparator.Ge, modifier: null,
                    new QuantitySearchValue(system: "http://unitsofmeasure.org", code: "mg", 120m))),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenADateSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var birthDateParam = ParameterManager.GetSearchParameter("Patient", "birthdate");
        await harness.SeedSearchParameterCatalogAsync([birthDateParam.Url!], CancellationToken.None);

        var matchId = $"diff-date-match-{Guid.NewGuid():N}";
        var otherId = $"diff-date-other-{Guid.NewGuid():N}";
        var matchDate = new DateTimeSearchValue(new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var otherDate = new DateTimeSearchValue(new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await CreateResourceAsync(harness, "Patient", matchId,
            [new SearchIndexEntry(birthDateParam, matchDate)], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherId,
            [new SearchIndexEntry(birthDateParam, otherDate)], CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                birthDateParam,
                new SearchParameterPredicateExpression(birthDateParam, SearchComparator.Eq, modifier: null, matchDate)),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenATokenTokenCompositeSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-concept");
        var codeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var valueParam = compositeParam.Component[1].ResolvedSearchParameter!;
        await harness.SeedSearchParameterCatalogAsync([compositeParam.Url!, codeParam.Url!, valueParam.Url!], CancellationToken.None);

        var matchId = $"diff-tt-match-{Guid.NewGuid():N}";
        var otherId = $"diff-tt-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new TokenSearchValue(system: null, code: "high", text: null)],
            ]))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new TokenSearchValue(system: null, code: "low", text: null)],
            ]))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(codeParam, 0,
                        new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                    new CompositeComponentExpression(valueParam, 1,
                        new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "high", text: null))),
                ])),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenATokenNumberNumberCompositeSearchParameter_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
    {
        // Arrange -- documented divergence (confirmed via SearchParameterQueryGenerator.ProcessCompositeExpressionAsync's
        // switch on CompositeType: TokenToken/TokenQuantity/TokenDateTime/TokenString/ReferenceToken all have a
        // case, TokenNumberNumber does not -- CompositeSearchParameterQueryGenerator.DetermineCompositeType
        // correctly classifies it (CompositeSearchParameterQueryGenerator.cs:110), but the switch's default arm
        // unconditionally returns Enumerable.Empty<long>() for it. Legacy therefore returns zero results for
        // EVERY TokenNumberNumber composite query regardless of data -- not a specific-data edge case, a total,
        // unconditional absence of support for this composite type, the same character of gap as the composite
        // :missing divergence below. Ignixa.Search.Sql's TokenNumberNumberLoweringRule has no equivalent gap
        // (confirmed: this same predicate against this same data returns the correct match on the compiled side).
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("MolecularSequence", "chromosome-window-coordinate");
        var chromosomeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var startParam = compositeParam.Component[1].ResolvedSearchParameter!;
        var endParam = compositeParam.Component[2].ResolvedSearchParameter!;
        await harness.SeedSearchParameterCatalogAsync(
            [compositeParam.Url!, chromosomeParam.Url!, startParam.Url!, endParam.Url!], CancellationToken.None);

        // Genuine ranges (Low != High), not single-point NumberSearchValue(100m)-style values: both
        // RowGenerators' TokenNumberNumberCompositeRowGenerator (Ignixa.DataLayer.SqlEntityFramework AND
        // Ignixa.DataLayer.SqlServer, confirmed byte-for-byte identical) store a Low==High value in
        // SingleValue2/SingleValue3 ONLY, leaving LowValue2/HighValue2/LowValue3/HighValue3 NULL --
        // unlike TokenQuantityCompositeRowGenerator's sibling, which duplicates into Low/High for exactly
        // this case. Ignixa.Search.Sql's TokenNumberNumberLoweringRule (matching NumberLoweringRule's own
        // leaf convention, where the schema's NOT NULL constraint forces the row generator to always
        // duplicate) queries only LowValue/HighValue, never SingleValue, so it can never match a
        // single-point composite number component -- a genuine write-path (RowGenerator) bug, confirmed
        // present on BOTH engines identically (not a compiler divergence -- see task report). Using a
        // real range here exercises the column pair both engines actually populate, isolating this test
        // to the TokenNumberNumber-composite-support gap this test documents.
        var matchId = $"diff-tnn-match-{Guid.NewGuid():N}";
        var otherId = $"diff-tnn-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "MolecularSequence", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "1", text: null)],
                [new NumberSearchValue(low: 100m, high: 101m)],
                [new NumberSearchValue(low: 199m, high: 200m)],
            ]))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "MolecularSequence", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "1", text: null)],
                [new NumberSearchValue(low: 500m, high: 501m)],
                [new NumberSearchValue(low: 599m, high: 600m)],
            ]))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "MolecularSequence",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(chromosomeParam, 0,
                        new SearchParameterPredicateExpression(chromosomeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1", text: null))),
                    new CompositeComponentExpression(startParam, 1,
                        new SearchParameterPredicateExpression(startParam, SearchComparator.Ge, modifier: null, new NumberSearchValue(100m))),
                    new CompositeComponentExpression(endParam, 2,
                        new SearchParameterPredicateExpression(endParam, SearchComparator.Le, modifier: null, new NumberSearchValue(200m))),
                ])),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- documented divergence: legacy has no CompositeType.TokenNumberNumber case at all
        // (unconditionally empty), the compiler correctly matches the resource whose range satisfies
        // every component.
        legacyResults.ShouldBeEmpty();
        newResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenATokenStringCompositeSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-string");
        var codeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var valueParam = compositeParam.Component[1].ResolvedSearchParameter!;
        await harness.SeedSearchParameterCatalogAsync([compositeParam.Url!, codeParam.Url!, valueParam.Url!], CancellationToken.None);

        var matchId = $"diff-ts-match-{Guid.NewGuid():N}";
        var otherId = $"diff-ts-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new StringSearchValue("Elevated")],
            ]))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new StringSearchValue("Normal")],
            ]))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(codeParam, 0,
                        new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                    new CompositeComponentExpression(valueParam, 1,
                        new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated"))),
                ])),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenATokenQuantityCompositeSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-quantity");
        var codeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var valueParam = compositeParam.Component[1].ResolvedSearchParameter!;
        await harness.SeedSearchParameterCatalogAsync([compositeParam.Url!, codeParam.Url!, valueParam.Url!], CancellationToken.None);

        var matchId = $"diff-tq-match-{Guid.NewGuid():N}";
        var otherId = $"diff-tq-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new QuantitySearchValue(system: null!, code: null!, 120m)],
            ]))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new QuantitySearchValue(system: null!, code: null!, 60m)],
            ]))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(codeParam, 0,
                        new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                    new CompositeComponentExpression(valueParam, 1,
                        new SearchParameterPredicateExpression(valueParam, SearchComparator.Ge, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m))),
                ])),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenATokenDateCompositeSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-date");
        var codeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var dateParam = compositeParam.Component[1].ResolvedSearchParameter!;
        await harness.SeedSearchParameterCatalogAsync([compositeParam.Url!, codeParam.Url!, dateParam.Url!], CancellationToken.None);

        var matchId = $"diff-td-match-{Guid.NewGuid():N}";
        var otherId = $"diff-td-other-{Guid.NewGuid():N}";
        var matchDate = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var otherDate = new DateTimeSearchValue(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await CreateResourceAsync(harness, "Observation", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [matchDate],
            ]))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [otherDate],
            ]))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(codeParam, 0,
                        new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                    new CompositeComponentExpression(dateParam, 1,
                        new SearchParameterPredicateExpression(dateParam, SearchComparator.Ge, modifier: null, matchDate)),
                ])),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenAReferenceTokenCompositeSearchParameter_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("DocumentReference", "relationship");
        var relatesToParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var relationParam = compositeParam.Component[1].ResolvedSearchParameter!;
        await harness.SeedSearchParameterCatalogAsync([compositeParam.Url!, relatesToParam.Url!, relationParam.Url!], CancellationToken.None);

        var targetId = $"diff-rt-target-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "DocumentReference", targetId, null, CancellationToken.None);

        var matchId = $"diff-rt-match-{Guid.NewGuid():N}";
        var otherId = $"diff-rt-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "DocumentReference", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: targetId)],
                [new TokenSearchValue(system: null, code: "replaces", text: null)],
            ]))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "DocumentReference", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: targetId)],
                [new TokenSearchValue(system: null, code: "transforms", text: null)],
            ]))],
            CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "DocumentReference",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(relatesToParam, 0,
                        new SearchParameterPredicateExpression(relatesToParam, SearchComparator.Eq, modifier: null,
                            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: targetId))),
                    new CompositeComponentExpression(relationParam, 1,
                        new SearchParameterPredicateExpression(relationParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "replaces", text: null))),
                ])),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    [Fact]
    public async Task GivenALeafParametersMissingModifier_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!], CancellationToken.None);

        var withFamilyId = $"diff-missing-leaf-present-{Guid.NewGuid():N}";
        var withoutFamilyId = $"diff-missing-leaf-absent-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", withFamilyId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Carter"))], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", withoutFamilyId, null, CancellationToken.None);

        var missingTrueOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new MissingSearchParameterExpression(familyParam, isMissing: true),
        };
        var missingFalseOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new MissingSearchParameterExpression(familyParam, isMissing: false),
        };

        // Act
        var legacyMissingTrue = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(missingTrueOptions, CancellationToken.None));
        var newMissingTrue = await CollectAsync(harness.NewSearchService.SearchStreamAsync(missingTrueOptions, CancellationToken.None));
        var legacyMissingFalse = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(missingFalseOptions, CancellationToken.None));
        var newMissingFalse = await CollectAsync(harness.NewSearchService.SearchStreamAsync(missingFalseOptions, CancellationToken.None));

        // Assert
        AssertSameResults(legacyMissingTrue, newMissingTrue);
        legacyMissingTrue.Select(r => r.ResourceId).ShouldBe([withoutFamilyId]);

        AssertSameResults(legacyMissingFalse, newMissingFalse);
        legacyMissingFalse.Select(r => r.ResourceId).ShouldBe([withFamilyId]);
    }

    [Fact]
    public async Task GivenACompositeParametersMissingModifier_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
    {
        // Arrange -- a resource missing a composite parameter's value entirely.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-concept");
        await harness.SeedSearchParameterCatalogAsync([compositeParam.Url!], CancellationToken.None);

        var missingId = $"diff-missing-composite-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", missingId, null, CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new MissingSearchParameterExpression(compositeParam, isMissing: true),
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- documented divergence per the design doc: legacy has no Composite arm (returns empty
        // with a warning log), the compiler returns real results.
        legacyResults.ShouldBeEmpty();
        newResults.ShouldNotBeEmpty();
        newResults.Select(r => r.ResourceId).ShouldContain(missingId);
    }

    [Fact]
    public async Task GivenAMatchingSetOfResources_WhenCountedOnBothEngines_ThenReturnsTheSameCount()
    {
        // Arrange -- 3 matching resources plus 1 non-matching, proving Count isn't just "everything".
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!], CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            await CreateResourceAsync(harness, "Patient", $"diff-count-match-{i}-{Guid.NewGuid():N}",
                [new SearchIndexEntry(familyParam, new StringSearchValue("Donovan"))], CancellationToken.None);
        }

        await CreateResourceAsync(harness, "Patient", $"diff-count-other-{Guid.NewGuid():N}",
            [new SearchIndexEntry(familyParam, new StringSearchValue("Ellis"))], CancellationToken.None);

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                familyParam,
                new SearchParameterPredicateExpression(familyParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Donovan"))),
        };

        // Act
        var legacyCount = await harness.LegacySearchService.CountAsync(options, CancellationToken.None);
        var newCount = await harness.NewSearchService.CountAsync(options, CancellationToken.None);

        // Assert
        legacyCount.ShouldBe(newCount);
        legacyCount.ShouldBe(3);
    }
}
