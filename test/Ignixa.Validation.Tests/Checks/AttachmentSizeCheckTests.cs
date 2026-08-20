// <copyright file="AttachmentSizeCheckTests.cs" company="Microsoft Corporation">
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
/// Tests for AttachmentSizeCheck.
/// </summary>
public class AttachmentSizeCheckTests
{
    private static ValidationResult ValidateAttachment(string attachmentJson)
    {
        var json = JsonNode.Parse($"{{\"resourceType\":\"Media\",\"content\":{attachmentJson}}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var attachmentElement = element.Children("content")[0];
        var check = new AttachmentSizeCheck();

        return check.Validate(attachmentElement, new ValidationSettings(), ValidationState.ForRoot(attachmentElement));
    }

    [Fact]
    public void GivenSizeMatchingDecodedDataLength_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - "help i'm a bug" is 14 bytes
        var result = ValidateAttachment(
            "{\"contentType\":\"application/octet-stream\",\"data\":\"aGVscCBpJ20gYSBidWc=\",\"size\":14}");

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSizeNotMatchingDecodedDataLength_WhenValidating_ThenReturnsError()
    {
        // Arrange - "help i'm a bug" is 14 bytes, stated size is 100
        var result = ValidateAttachment(
            "{\"contentType\":\"application/octet-stream\",\"data\":\"aGVscCBpJ20gYSBidWc=\",\"size\":100}");

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Message.Contains("does not match actual attachment size", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenNoSize_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = ValidateAttachment(
            "{\"contentType\":\"application/octet-stream\",\"data\":\"aGVscCBpJ20gYSBidWc=\"}");

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSizeButNoData_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - nothing to cross-check against; cardinality/other checks handle a missing url/data
        var result = ValidateAttachment("{\"contentType\":\"application/octet-stream\",\"size\":14}");

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSizeWithMalformedData_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - malformed base64 is reported by TypeCheck; this check must not pile on a
        // misleading size-mismatch message for content that's already invalid.
        var result = ValidateAttachment(
            "{\"contentType\":\"application/octet-stream\",\"data\":\"%%%2@()()\",\"size\":14}");

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
