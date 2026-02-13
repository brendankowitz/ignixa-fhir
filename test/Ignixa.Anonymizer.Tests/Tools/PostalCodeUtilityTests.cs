// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Processors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Tools;

namespace Ignixa.Anonymizer.Tests.Tools;

public class PostalCodeToolTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetPostalCodeDataForRedact()
    {
        yield return ["98052"];
        yield return ["10104"];
        yield return ["00000"];
        yield return ["98028-1830"];
    }

    public static IEnumerable<object[]> GetPostalCodeDataForPartialRedact()
    {
        yield return ["98052", "98000"];
        yield return ["10104", "10100"];
        yield return ["20301", "00000"];
        yield return ["55602", "00000"];
        yield return ["98028-1830", "98000-0000"];
        yield return ["20301-1830", "00000-0000"];
    }

    [Theory]
    [MemberData(nameof(GetPostalCodeDataForRedact))]
    public void GivenAPostalCode_WhenRedact_ThenValueShouldBeNull(string postalCode)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Patient","address":[{"postalCode":"{{{postalCode}}}"}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("address").First().Children("postalCode").First();

        // Act
        var result = PostalCodeTool.RedactPostalCode(node, false, null);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("address").First().Children("postalCode").FirstOrDefault();
        updatedNode?.Value.ShouldBeNull();
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(AnonymizationOperations.Redact);
    }

    [Theory]
    [MemberData(nameof(GetPostalCodeDataForPartialRedact))]
    public void GivenAPostalCode_WhenPartialRedact_ThenPartialDigitsShouldBeRedacted(string postalCode, string expectedPostalCode)
    {
        // Arrange
        var json = $$$"""{"resourceType":"Patient","address":[{"postalCode":"{{{postalCode}}}"}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("address").First().Children("postalCode").First();

        // Act
        var result = PostalCodeTool.RedactPostalCode(node, true, ["203", "556"]);

        // Assert
        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("address").First().Children("postalCode").First();
        updatedNode.Value.ToString().ShouldBe(expectedPostalCode);
        result.WasModified.ShouldBeTrue();
        result.OperationType.ShouldBe(AnonymizationOperations.Abstract);
    }
}
