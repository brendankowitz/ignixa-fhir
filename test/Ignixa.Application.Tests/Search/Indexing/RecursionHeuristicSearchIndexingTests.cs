// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Falsification tests for issue #454's search-indexing impact. <c>SchemaAwareElement</c>'s
/// name-equality recursion heuristic mistyped every <c>X.y.y</c> path as the backbone <c>X.Y</c> -
/// <c>Encounter.location.location</c> arrived as <c>Encounter.Location</c> rather than
/// <c>Reference</c> - so the converter pipeline never found a converter and the parameter produced an
/// empty index for every instance of its resource, on every FHIR version.
/// </summary>
/// <remarks>
/// One test per shipped search parameter the defect silenced, because the pinned skip dictionary in
/// <c>ResourceBackedKnownDivergences</c> proves only that the rows stopped being recorded - it cannot
/// show that an entry with the right value now takes their place. Each parameter is exercised on a
/// version whose published expression actually reaches the nested element, which is not every version
/// that ships the parameter: R4B's own <c>Ingredient-manufacturer</c> expression names the backbone
/// one level short of the <c>Reference</c>, a gap in the published definition rather than in the
/// element model, so R5 carries that case here.
/// <para>
/// The three search value types are deliberate. A <c>Reference</c> under a mistyped node was the
/// reported symptom, but the same node also feeds token and string converters, and each resolves its
/// value differently - asserting only references would leave two of the three paths unguarded.
/// </para>
/// </remarks>
public class RecursionHeuristicSearchIndexingTests
{
    [Fact]
    public void GivenEncounterWithPopulatedLocationLocation_WhenIndexed_ThenEncounterLocationProducesAReferenceSearchValue()
    {
        var encounterJson = """
            {"resourceType":"Encounter","id":"enc1","status":"in-progress",
             "class":{"code":"AMB"},
             "location":[{"location":{"reference":"Location/loc1"},"status":"active"}]}
            """;

        var reference = ExtractSingle<ReferenceSearchValue>(new R4CoreSchemaProvider(), encounterJson, "location");

        reference.ResourceType.ShouldBe("Location");
        reference.ResourceId.ShouldBe("loc1");
    }

    [Fact]
    public void GivenIngredientWithPopulatedManufacturerManufacturer_WhenIndexed_ThenIngredientManufacturerProducesAReferenceSearchValue()
    {
        // R5 rather than R4B: R4B publishes this parameter as "Ingredient.manufacturer", which stops at
        // the backbone and so cannot reach the nested Reference whatever the element model does.
        var ingredientJson = """
            {"resourceType":"Ingredient","id":"ing1","status":"active",
             "role":{"coding":[{"code":"active"}]},
             "manufacturer":[{"manufacturer":{"reference":"Organization/org1"}}]}
            """;

        var reference = ExtractSingle<ReferenceSearchValue>(new R5CoreSchemaProvider(), ingredientJson, "manufacturer");

        reference.ResourceType.ShouldBe("Organization");
        reference.ResourceId.ShouldBe("org1");
    }

    [Fact]
    public void GivenMedicinalProductDefinitionWithPopulatedContactContact_WhenIndexed_ThenContactProducesAReferenceSearchValue()
    {
        var definitionJson = """
            {"resourceType":"MedicinalProductDefinition","id":"mpd1",
             "contact":[{"contact":{"reference":"Organization/org2"}}]}
            """;

        var reference = ExtractSingle<ReferenceSearchValue>(new R4BCoreSchemaProvider(), definitionJson, "contact");

        reference.ResourceType.ShouldBe("Organization");
        reference.ResourceId.ShouldBe("org2");
    }

    [Fact]
    public void GivenSubstanceDefinitionWithPopulatedCodeCode_WhenIndexed_ThenSubstanceDefinitionCodeProducesATokenSearchValue()
    {
        var definitionJson = """
            {"resourceType":"SubstanceDefinition","id":"sd1",
             "code":[{"code":{"coding":[{"system":"http://example.org/substances","code":"ABC-123"}]}}]}
            """;

        var token = ExtractSingle<TokenSearchValue>(new R4BCoreSchemaProvider(), definitionJson, "code");

        token.System.ShouldBe("http://example.org/substances");
        token.Code.ShouldBe("ABC-123");
    }

    [Fact]
    public void GivenSubstanceDefinitionWithPopulatedNameName_WhenIndexed_ThenSubstanceDefinitionNameProducesAStringSearchValue()
    {
        var definitionJson = """
            {"resourceType":"SubstanceDefinition","id":"sd2",
             "name":[{"name":"Acetaminophen"}]}
            """;

        var name = ExtractSingle<StringSearchValue>(new R4BCoreSchemaProvider(), definitionJson, "name");

        name.String.ShouldBe("Acetaminophen");
    }

    [Fact]
    public void GivenSubstanceSpecificationWithPopulatedCodeCode_WhenIndexed_ThenSubstanceSpecificationCodeProducesATokenSearchValue()
    {
        var specificationJson = """
            {"resourceType":"SubstanceSpecification","id":"ss1",
             "code":[{"code":{"coding":[{"system":"http://example.org/substances","code":"XYZ-9"}]}}]}
            """;

        var token = ExtractSingle<TokenSearchValue>(new R4CoreSchemaProvider(), specificationJson, "code");

        token.System.ShouldBe("http://example.org/substances");
        token.Code.ShouldBe("XYZ-9");
    }

    /// <summary>
    /// Indexes <paramref name="resourceJson"/> with the production indexer and returns the one
    /// <typeparamref name="TValue"/> the parameter produced. Filtering by value type as well as code
    /// keeps the assertion honest: before the fix these parameters produced no entry at all, so a
    /// bare count would have been satisfied by an entry of any other type.
    /// </summary>
    private static TValue ExtractSingle<TValue>(IFhirSchemaProvider schemaProvider, string resourceJson, string code)
        where TValue : ISearchValue
    {
        var indexer = SearchIndexerFactory.CreateInstance(
            schemaProvider,
            NullLoggerFactory.Instance,
            new SearchParameterDefinitionManager(schemaProvider, new NullLogger<SearchParameterDefinitionManager>()),
            NullFhirBaseUriProvider.Instance);

        var element = JsonSourceNodeFactory.Parse(resourceJson).ToElement(schemaProvider);

        return indexer.Extract(element)
            .Where(entry => entry.SearchParameter.Code == code)
            .Select(entry => entry.Value)
            .OfType<TValue>()
            .ToArray()
            .ShouldHaveSingleItem();
    }
}
