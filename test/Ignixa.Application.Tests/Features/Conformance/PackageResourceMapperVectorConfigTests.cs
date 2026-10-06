using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Shouldly;
using Xunit;
using SearchParameterInfo = Ignixa.Application.Features.Conformance.SearchParameterInfo;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>
/// Coverage for <see cref="PackageResourceMapper"/> parsing the <c>vector-search-config</c> extension
/// off a package-sourced SearchParameter JSON document and carrying it onto the conformance
/// <see cref="SearchParameterInfo"/> DTO - the first hop of the path that eventually reaches the Core
/// <see cref="Ignixa.Search.Models.SearchParameterInfo"/> built by
/// <see cref="Ignixa.Application.Features.Search.CompositeSearchParameterDefinitionManager"/>.
/// </summary>
public class PackageResourceMapperVectorConfigTests
{
    [Fact]
    public void GivenPackageJsonWithExtension_WhenMapped_ThenVectorConfigSurvivesToCoreInfo()
    {
        PackageResource resource = BuildSearchParameterResource(
            """
            {
              "resourceType": "SearchParameter",
              "id": "semantic-text",
              "url": "http://example.org/fhir/SearchParameter/semantic-text",
              "name": "semantic-text",
              "status": "active",
              "code": "semantic-text",
              "base": [ "Patient" ],
              "type": "special",
              "expression": "Patient.name.family",
              "extension": [
                {
                  "url": "http://microsoft.com/fhir/StructureDefinition/vector-search-config",
                  "extension": [
                    { "url": "extractionPolicy", "valueCode": "firstValue" },
                    { "url": "maxInputTokens", "valuePositiveInt": 2048 },
                    { "url": "minimumScore", "valueDecimal": 0.75 },
                    { "url": "chunkSizeTokens", "valuePositiveInt": 128 },
                    { "url": "chunkOverlapTokens", "valueUnsignedInt": 16 },
                    { "url": "distanceMetric", "valueCode": "cosine" }
                  ]
                }
              ]
            }
            """);

        PackageResources mapped = PackageResourceMapper.MapToPackageResources([resource]);

        SearchParameterInfo searchParameter = mapped.SearchParameters.ShouldHaveSingleItem();
        VectorSearchConfig vectorConfig = searchParameter.VectorConfig.ShouldNotBeNull();
        vectorConfig.ExtractionPolicy.ShouldBe(VectorTextExtractionPolicy.FirstValue);
        vectorConfig.MaxInputTokens.ShouldBe(2048);
        vectorConfig.MinimumScore.ShouldBe(0.75m);
        vectorConfig.ChunkSizeTokens.ShouldBe(128);
        vectorConfig.ChunkOverlapTokens.ShouldBe(16);
        searchParameter.Type.ShouldBe(SearchParamType.Special);
    }

    [Fact]
    public void GivenPackageJsonWithoutExtension_WhenMapped_ThenVectorConfigIsNull()
    {
        PackageResource resource = BuildSearchParameterResource(
            """
            {
              "resourceType": "SearchParameter",
              "id": "plain-token",
              "url": "http://example.org/fhir/SearchParameter/plain-token",
              "name": "plain-token",
              "status": "active",
              "code": "plain-token",
              "base": [ "Patient" ],
              "type": "token",
              "expression": "Patient.identifier"
            }
            """);

        PackageResources mapped = PackageResourceMapper.MapToPackageResources([resource]);

        SearchParameterInfo searchParameter = mapped.SearchParameters.ShouldHaveSingleItem();
        searchParameter.VectorConfig.ShouldBeNull();
    }

    private static PackageResource BuildSearchParameterResource(string json) => new()
    {
        PackageId = "test.package",
        PackageVersion = "1.0.0",
        ResourceType = "SearchParameter",
        Canonical = "http://example.org/fhir/SearchParameter/semantic-text",
        ResourceId = "semantic-text",
        ResourceJson = json,
        FhirVersion = "4.0.1",
    };
}
