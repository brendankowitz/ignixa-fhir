using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Definition.BundleNavigators;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// Coverage for parsing the fhir-server <c>vector-search-config</c> extension into
/// <see cref="VectorSearchConfig"/> and for its effect on <see cref="SearchParameterInfo"/> when a
/// <c>special</c>-typed SearchParameter is registered at runtime (<see cref="SearchParameterDefinitionManager.AddNewSearchParameters"/>).
/// </summary>
public class VectorSearchConfigTests
{
    public static TheoryData<FhirVersion> CrossVersionTargets =>
        new(FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5);

    [Fact]
    public void GivenFullExtension_WhenParsed_ThenAllFieldsMatch()
    {
        IElement extension = BuildVectorConfigExtension(
            """
            { "url": "extractionPolicy", "valueCode": "perValueRow" },
            { "url": "maxInputTokens", "valuePositiveInt": 4000 },
            { "url": "minimumScore", "valueDecimal": 0.42 },
            { "url": "chunkSizeTokens", "valuePositiveInt": 256 },
            { "url": "chunkOverlapTokens", "valueUnsignedInt": 32 },
            { "url": "distanceMetric", "valueCode": "cosine" }
            """);

        VectorSearchConfig config = VectorSearchConfig.Parse(extension);

        config.ExtractionPolicy.ShouldBe(VectorTextExtractionPolicy.PerValueRow);
        config.MaxInputTokens.ShouldBe(4000);
        config.MinimumScore.ShouldBe(0.42m);
        config.ChunkSizeTokens.ShouldBe(256);
        config.ChunkOverlapTokens.ShouldBe(32);
    }

    [Fact]
    public void GivenNoSubExtensions_WhenParsed_ThenDefaultsAreConcatenate8000Zero()
    {
        IElement extension = BuildVectorConfigExtension(string.Empty);

        VectorSearchConfig config = VectorSearchConfig.Parse(extension);

        config.ExtractionPolicy.ShouldBe(VectorTextExtractionPolicy.Concatenate);
        config.MaxInputTokens.ShouldBe(8000);
        config.MinimumScore.ShouldBe(0m);
        config.ChunkSizeTokens.ShouldBeNull();
        config.ChunkOverlapTokens.ShouldBeNull();
    }

    [Fact]
    public void GivenMinimumScore1Point5_WhenParsed_ThenFormatException()
    {
        IElement extension = BuildVectorConfigExtension(
            """{ "url": "minimumScore", "valueDecimal": 1.5 }""");

        FormatException error = Should.Throw<FormatException>(() => VectorSearchConfig.Parse(extension));

        error.Message.ShouldContain("minimumScore");
    }

    [Fact]
    public void GivenDistanceMetricL2_WhenParsed_ThenFormatException()
    {
        IElement extension = BuildVectorConfigExtension(
            """{ "url": "distanceMetric", "valueCode": "l2" }""");

        FormatException error = Should.Throw<FormatException>(() => VectorSearchConfig.Parse(extension));

        error.Message.ShouldContain("distanceMetric");
    }

    [Fact]
    public void GivenOverlapNotLessThanChunk_WhenParsed_ThenFormatException()
    {
        IElement extension = BuildVectorConfigExtension(
            """
            { "url": "chunkSizeTokens", "valuePositiveInt": 100 },
            { "url": "chunkOverlapTokens", "valueUnsignedInt": 100 }
            """);

        FormatException error = Should.Throw<FormatException>(() => VectorSearchConfig.Parse(extension));

        error.Message.ShouldContain("chunkOverlapTokens");
    }

    [Theory]
    [MemberData(nameof(CrossVersionTargets))]
    public void GivenSpecialSearchParameterWithExtension_WhenBuilt_ThenIsSemanticTrue(FhirVersion version)
    {
        IFhirSchemaProvider schema = version.GetSchemaProvider();
        var manager = new SearchParameterDefinitionManager(schema, NullLogger<SearchParameterDefinitionManager>.Instance);

        manager.AddNewSearchParameters(new[]
        {
            BuildSpecialSearchParameter(
                schema,
                id: $"semantic-text-{version}",
                subExtensionsJson: """{ "url": "extractionPolicy", "valueCode": "concatenate" }""")
        });

        SearchParameterInfo registered = manager.GetSearchParameter("Patient", "semantic-text");

        registered.Type.ShouldBe(SearchParamType.Special);
        registered.VectorConfig.ShouldNotBeNull();
        registered.IsSemantic.ShouldBeTrue();
        registered.IsSupported.ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidExtension_WhenBuilt_ThenIsSupportedFalse()
    {
        IFhirSchemaProvider schema = FhirVersion.R4.GetSchemaProvider();
        var manager = new SearchParameterDefinitionManager(schema, NullLogger<SearchParameterDefinitionManager>.Instance);

        manager.AddNewSearchParameters(new[]
        {
            BuildSpecialSearchParameter(
                schema,
                id: "semantic-text-invalid",
                subExtensionsJson: """{ "url": "minimumScore", "valueDecimal": 7 }""")
        });

        SearchParameterInfo registered = manager.GetSearchParameter("Patient", "semantic-text");

        registered.IsSupported.ShouldBeFalse();
        registered.VectorConfig.ShouldBeNull();
        registered.IsSemantic.ShouldBeFalse();
    }

    [Fact]
    public void GivenSpecialSearchParameterWithoutExtension_WhenBuilt_ThenUnchanged()
    {
        IFhirSchemaProvider schema = FhirVersion.R4.GetSchemaProvider();
        var manager = new SearchParameterDefinitionManager(schema, NullLogger<SearchParameterDefinitionManager>.Instance);

        manager.AddNewSearchParameters(new[] { BuildSpecialSearchParameter(schema, id: "plain-special", subExtensionsJson: null) });

        SearchParameterInfo registered = manager.GetSearchParameter("Patient", "semantic-text");

        registered.VectorConfig.ShouldBeNull();
        registered.IsSemantic.ShouldBeFalse();
        registered.IsSupported.ShouldBeTrue();
    }

    /// <summary>
    /// Builds a minimal <c>special</c>-typed SearchParameter on Patient, optionally carrying a
    /// <c>vector-search-config</c> extension whose sub-extensions are <paramref name="subExtensionsJson"/>
    /// (comma-separated JSON objects, or null/empty to omit the extension entirely).
    /// </summary>
    private static IElement BuildSpecialSearchParameter(IFhirSchemaProvider schema, string id, string? subExtensionsJson)
    {
        string extensionBlock = subExtensionsJson is null
            ? string.Empty
            : $$"""
                ,
                "extension": [
                  {
                    "url": "{{VectorSearchConfig.ExtensionUrl}}",
                    "extension": [ {{subExtensionsJson}} ]
                  }
                ]
                """;

        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{id}}",
              "url": "http://example.org/fhir/SearchParameter/{{id}}",
              "name": "{{id}}",
              "status": "active",
              "code": "semantic-text",
              "base": [ "Patient" ],
              "type": "special",
              "expression": "Patient.name.family"
              {{extensionBlock}}
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(schema);
    }

    /// <summary>
    /// Builds a <c>vector-search-config</c> extension IElement by wrapping it on a throwaway SearchParameter
    /// and navigating back down, matching the real parsing path (<see cref="SearchParameterNavigator.VectorConfigExtension"/>)
    /// rather than constructing the element some other way.
    /// </summary>
    private static IElement BuildVectorConfigExtension(string subExtensionsJson)
    {
        IFhirSchemaProvider schema = FhirVersion.R4.GetSchemaProvider();
        IElement resourceElement = BuildSpecialSearchParameter(schema, "vector-config-fixture", subExtensionsJson);
        var navigator = new SearchParameterNavigator(resourceElement);

        return navigator.VectorConfigExtension;
    }
}
