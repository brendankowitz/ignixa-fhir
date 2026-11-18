// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.DeId.Extensions;

namespace Ignixa.DeId.Tests.Extensions;

public class FhirPathExtensionsTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Fact]
    public void GivenAPatient_WhenNavigateWithNodesByType_ThenMatchingNodesShouldBeReturned()
    {
        // Arrange
        var json = """{"resourceType":"Patient","active":true,"address":[{"city":"Test0"}],"contact":[{"address":{"city":"Test1"}}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);

        // Act
        var results = element.Select("descendants().ofType(Address)").ToList();

        // Assert
        results.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenRequestSuppliedExpressions_WhenInspectingTheExtensionClass_ThenItHoldsNoStaticDictionaryCache()
    {
        // Guard (#535): rule paths are request-supplied, so a static dictionary keyed by expression text
        // grows without bound. Expression caching belongs to the FHIRPath library, which bounds it.
        var staticFields = typeof(FhirPathExtensions).GetFields(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        staticFields.ShouldNotContain(f => f.FieldType.Name.StartsWith("ConcurrentDictionary", StringComparison.Ordinal)
            || f.FieldType.Name.StartsWith("Dictionary", StringComparison.Ordinal));
    }
}
