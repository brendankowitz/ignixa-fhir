// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;

namespace Ignixa.Anonymizer.Tests.Extensions;

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
}
