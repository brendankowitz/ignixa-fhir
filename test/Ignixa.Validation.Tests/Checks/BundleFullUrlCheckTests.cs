// <copyright file="BundleFullUrlCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for BundleFullUrlCheck.
/// </summary>
public class BundleFullUrlCheckTests
{
    private static ValidationResult Validate(string json, ValidationDepth depth = ValidationDepth.Full)
    {
        var node = JsonNode.Parse(json);
        var sourceNode = JsonNodeSourceNode.Create(node);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var check = new BundleFullUrlCheck();

        return check.Validate(element, new ValidationSettings { Depth = depth }, ValidationState.ForRoot(element));
    }

    [Fact]
    public void GivenRelativeFullUrl_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "collection",
            "entry": [
                { "fullUrl": "Patient/1", "resource": { "resourceType": "Patient", "id": "1" } }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Message.Contains("must be an absolute URL", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenAbsoluteFullUrl_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "collection",
            "entry": [
                { "fullUrl": "urn:uuid:212c57ad-45cd-4882-87e7-8415ded3db05", "resource": { "resourceType": "Patient", "id": "1" } }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoFullUrl_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - fullUrl-required is a separate, context-dependent rule this check does not enforce
        var result = Validate("""
        {
            "resourceType": "Bundle",
            "type": "transaction",
            "entry": [
                { "resource": { "resourceType": "Patient", "id": "1" }, "request": { "method": "PUT", "url": "Patient/1" } }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenRelativeFullUrl_WhenValidatingInCompatibilityMode_ThenReturnsSuccess()
    {
        // Arrange - matches CodingStructureCheck's precedent of tolerating relative URIs in
        // Compatibility mode (Microsoft FHIR Server alignment).
        var result = Validate(
            """
            {
                "resourceType": "Bundle",
                "type": "collection",
                "entry": [
                    { "fullUrl": "Patient/1", "resource": { "resourceType": "Patient", "id": "1" } }
                ]
            }
            """,
            ValidationDepth.Compatibility);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
