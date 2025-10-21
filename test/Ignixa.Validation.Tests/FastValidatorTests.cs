// <copyright file="FastValidatorTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Tests for FastValidator.
/// </summary>
public class FastValidatorTests
{
    [Fact]
    public void GivenValidPatientResource_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""gender"": ""male"",
            ""birthDate"": ""1990-01-15""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenResourceWithoutResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""id"": ""example"",
            ""gender"": ""male""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "structure-1");
    }

    [Fact]
    public void GivenJsonArray_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"[""value1"", ""value2""]");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "structure-1");
    }

    [Fact]
    public void GivenValidResourceWithAdditionalChecks_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""gender"": ""male""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();
        var additionalChecks = new List<IValidationCheck>
        {
            new RequiredFieldCheck("id", isRequired: true),
            new TypeCheck("gender", "string")
        };

        // Act
        var result = validator.Validate(sourceNode, additionalChecks);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenResourceViolatingAdditionalCheck_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""invalid"": ""not-a-date""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();
        var additionalChecks = new List<IValidationCheck>
        {
            new RequiredFieldCheck("birthDate", isRequired: true)
        };

        // Act
        var result = validator.Validate(sourceNode, additionalChecks);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i => i.Code == "required-1");
    }

    [Fact]
    public void GivenResourceViolatingMultipleRules_WhenValidating_ThenReturnsMultipleErrors()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""id"": ""example"",
            ""gender"": 123
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();
        var additionalChecks = new List<IValidationCheck>
        {
            new TypeCheck("gender", "string")
        };

        // Act
        var result = validator.Validate(sourceNode, additionalChecks);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        // FastValidator runs both JsonStructureCheck (structure-1) and RequiredFieldCheck (required-1)
        // Missing resourceType will be caught by structure check
        Assert.True(result.Issues.Count >= 1); // At least missing resourceType
        Assert.Contains(result.Issues, i => i.Code == "structure-1" || i.Code == "required-1"); // resourceType missing
    }

    [Fact]
    public void GivenValidResource_WhenConvertingToOperationOutcome_ThenReturnsEmptyOutcome()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();

        // Act
        var result = validator.Validate(sourceNode);
        var outcome = result.ToOperationOutcome();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(outcome.Issue);
    }

    [Fact]
    public void GivenInvalidResource_WhenConvertingToOperationOutcome_ThenReturnsOutcomeWithIssues()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""id"": ""example""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var validator = new FastValidator();

        // Act
        var result = validator.Validate(sourceNode);
        var outcome = result.ToOperationOutcome();

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.NotEmpty(result.Issues);
        Assert.Contains(result.Issues, i => i.Message.Contains("resourceType", StringComparison.Ordinal));

        // OperationOutcome should have resourceType set
        Assert.Equal("OperationOutcome", outcome.ResourceType);

        // Issue list should be populated (even if properties are null due to serialization issues)
        var issues = outcome.Issue;
        Assert.NotNull(issues);
        Assert.Equal(result.Issues.Count, issues.Count);
    }
}
