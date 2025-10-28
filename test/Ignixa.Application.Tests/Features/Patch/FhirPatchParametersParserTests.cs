// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Patch;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.Models;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch;

public class FhirPatchParametersParserTests
{
    private readonly FhirPatchParametersParser _parser = new();

    [Fact]
    public void GivenValidParametersJson_WhenParsing_ThenReturnsOperations()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""replace""},
                        {""name"": ""path"", ""valueString"": ""Patient.name[0].family""},
                        {""name"": ""value"", ""valueString"": ""NewLastName""}
                    ]
                }
            ]
        }";

        // Act
        var parsedJson = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);
        var operations = _parser.Parse(parsedJson);

        // Assert
        Assert.NotNull(operations);
        Assert.Single(operations);

        var op = operations[0];
        Assert.Equal(FhirPatchOperationType.Replace, op.Type);
        Assert.Equal("Patient.name[0].family", op.Path);
        Assert.NotNull(op.Value);
        Assert.Equal("NewLastName", op.Value?.ToString());
    }

    [Fact]
    public void GivenMultipleOperations_WhenParsing_ThenReturnsAllOperations()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""add""},
                        {""name"": ""path"", ""valueString"": ""Patient.telecom""},
                        {""name"": ""value"", ""valueString"": ""555-1234""}
                    ]
                },
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""delete""},
                        {""name"": ""path"", ""valueString"": ""Patient.gender""}
                    ]
                }
            ]
        }";

        // Act
        var parsedJson = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);
        var operations = _parser.Parse(parsedJson);

        // Assert
        Assert.NotNull(operations);
        Assert.Equal(2, operations.Length);
        Assert.Equal(FhirPatchOperationType.Add, operations[0].Type);
        Assert.Equal(FhirPatchOperationType.Delete, operations[1].Type);
    }

    [Fact]
    public void GivenMoveOperation_WhenParsing_ThenReturnsMoveOperation()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""operation"",
                    ""part"": [
                        {""name"": ""type"", ""valueCode"": ""move""},
                        {""name"": ""source"", ""valueString"": ""Patient.name[0]""},
                        {""name"": ""destination"", ""valueString"": ""Patient.name[1]""}
                    ]
                }
            ]
        }";

        // Act
        var parsedJson = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);
        var operations = _parser.Parse(parsedJson);

        // Assert
        Assert.NotNull(operations);
        Assert.Single(operations);

        var op = operations[0];
        Assert.Equal(FhirPatchOperationType.Move, op.Type);
        Assert.Equal("Patient.name[0]", op.Source);
        Assert.Equal("Patient.name[1]", op.Destination);
    }

    [Fact]
    public void GivenNoOperationParameters_WhenParsing_ThenThrowsException()
    {
        // Arrange
        var parametersJson = @"{
            ""resourceType"": ""Parameters"",
            ""parameter"": [
                {
                    ""name"": ""someOtherParameter"",
                    ""valueString"": ""value""
                }
            ]
        }";

        // Act & Assert
        var parsedJson = JsonSourceNodeFactory.Parse<ParametersJsonNode>(parametersJson);
        var ex = Assert.Throws<FhirPatchException>(() => _parser.Parse(parsedJson));
        Assert.Contains("must contain at least one 'operation' parameter", ex.Message);
    }

    [Fact]
    public void GivenNullJson_WhenParsing_ThenThrowsException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => _parser.Parse(null));
        Assert.Contains("Parameters resource cannot be null", ex.Message);
    }
}
