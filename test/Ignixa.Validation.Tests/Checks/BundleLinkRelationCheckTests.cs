// <copyright file="BundleLinkRelationCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for BundleLinkRelationCheck.
/// </summary>
public class BundleLinkRelationCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var node = JsonNode.Parse(json);
        var sourceNode = JsonNodeSourceNode.Create(node);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var check = new BundleLinkRelationCheck();

        return check.Validate(element, new ValidationSettings(), ValidationState.ForRoot(element));
    }

    [Fact]
    public void GivenDuplicateSelfRelation_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "searchset",
            "link": [
                { "url": "base/Patient?name=test", "relation": "self" },
                { "url": "base/Patient?name=test", "relation": "self" }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Message.Contains("'self' can only occur once", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenDuplicateNonSelfRelation_WhenValidating_ThenReturnsError()
    {
        // Arrange - the underlying rule is general: no relation type may repeat, not just "self".
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "searchset",
            "link": [
                { "url": "base/Patient?name=test", "relation": "first" },
                { "url": "base/Patient?name=test", "relation": "first" }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Message.Contains("'first' can only occur once", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenDistinctRelations_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "searchset",
            "link": [
                { "url": "base/Patient?name=test", "relation": "self" },
                { "url": "base/Patient?name=test&page=2", "relation": "next" }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoLinks_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "collection"
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
