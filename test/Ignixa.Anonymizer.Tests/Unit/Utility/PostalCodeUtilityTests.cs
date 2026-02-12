// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using Ignixa.Anonymizer.Processors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Tools;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Utility;

public class PostalCodeUtilityTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    public static IEnumerable<object[]> GetPostalCodeDataForRedact()
    {
        yield return new object[] { "98052" };
        yield return new object[] { "10104" };
        yield return new object[] { "00000" };
        yield return new object[] { "98028-1830" };
    }

    public static IEnumerable<object[]> GetPostalCodeDataForPartialRedact()
    {
        yield return new object[] { "98052", "98000" };
        yield return new object[] { "10104", "10100" };
        yield return new object[] { "20301", "00000" };
        yield return new object[] { "55602", "00000" };
        yield return new object[] { "98028-1830", "98000-0000" };
        yield return new object[] { "20301-1830", "00000-0000" };
    }

    [Theory]
    [MemberData(nameof(GetPostalCodeDataForRedact))]
    public void GivenAPostalCode_WhenRedact_ThenDigitsShouldBeRedacted(string postalCode)
    {
        var json = $$$"""{"resourceType":"Patient","address":[{"postalCode":"{{{postalCode}}}"}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("address").First().Children("postalCode").First();
        var result = PostalCodeTool.RedactPostalCode(node, false, null);

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("address").First().Children("postalCode").FirstOrDefault();
        Assert.Null(updatedNode?.Value);
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Redact, result.OperationType);
    }

    [Theory]
    [MemberData(nameof(GetPostalCodeDataForPartialRedact))]
    public void GivenAPostalCode_WhenPartialRedact_ThenPartialDigitsShouldBeRedacted(string postalCode, string expectedPostalCode)
    {
        var json = $$$"""{"resourceType":"Patient","address":[{"postalCode":"{{{postalCode}}}"}]}""";
        var resourceNode = ResourceJsonNode.Parse(json);
        var element = resourceNode.ToElement(_schema);
        var node = element.Children("address").First().Children("postalCode").First();
        var result = PostalCodeTool.RedactPostalCode(node, true, new List<string>() { "203", "556" });

        resourceNode.InvalidateCaches();
        var updated = resourceNode.ToElement(_schema);
        var updatedNode = updated.Children("address").First().Children("postalCode").First();
        Assert.Equal(expectedPostalCode, updatedNode.Value.ToString());
        Assert.True(result.WasModified);
        Assert.Equal(AnonymizationOperations.Abstract, result.OperationType);
    }
}
