// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Application.Features.Search;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch.Executors;

/// <summary>
/// Unit tests for ReplaceOperationExecutor.
/// Tests all aspects of the Replace operation including simple paths, complex FHIRPath,
/// error handling, and immutable property protection.
/// </summary>
public class ReplaceOperationExecutorTests
{
    private readonly ILogger<ReplaceOperationExecutor> _logger;
    private readonly FhirPathPatchHelper _fhirPathHelper;
    private readonly ReplaceOperationExecutor _executor;

    public ReplaceOperationExecutorTests()
    {
        _logger = Substitute.For<ILogger<ReplaceOperationExecutor>>();

        // Create real FhirPathPatchHelper with dependencies
        var evaluator = new Ignixa.FhirPath.Evaluation.FhirPathEvaluator();
        var compiler = new FhirPathParser();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        var versionContext = new FhirVersionContext(loggerFactory, searchParamOptions);
        var structureProvider = versionContext.GetBaseSchemaProvider(FhirSpecification.R4);

        _fhirPathHelper = new FhirPathPatchHelper(evaluator, compiler, structureProvider);
        _executor = new ReplaceOperationExecutor(_logger, _fhirPathHelper);
    }

    #region Simple Path Tests

    [Fact]
    public async Task GivenSimplePath_WhenReplacing_ThenValueIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["gender"] = "male";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.gender",
            Value = "female",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        Assert.Equal("female", result.MutableNode["gender"]?.GetValue<string>());
        Assert.Same(resource, result); // In-place mutation
    }

    [Fact]
    public async Task GivenBooleanProperty_WhenReplacing_ThenValueIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["active"] = true;

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.active",
            Value = false,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        Assert.False(result.MutableNode["active"]?.GetValue<bool>());
    }

    [Fact]
    public async Task GivenIntegerProperty_WhenReplacing_ThenValueIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["multipleBirthInteger"] = 1;

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.multipleBirthInteger",
            Value = 3,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        Assert.Equal(3, result.MutableNode["multipleBirthInteger"]?.GetValue<int>());
    }

    #endregion

    #region Array Element Tests

    [Fact]
    public async Task GivenArrayElementPath_WhenReplacing_ThenElementIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["name"] = JsonNode.Parse(@"[
            {
                ""family"": ""Doe"",
                ""given"": [""John""]
            }
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.name[0].family",
            Value = "Smith",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        Assert.NotNull(names);
        var firstName = names[0]?.AsObject();
        Assert.Equal("Smith", firstName?["family"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenNestedArrayPath_WhenReplacing_ThenElementIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["name"] = JsonNode.Parse(@"[
            {
                ""family"": ""Doe"",
                ""given"": [""John"", ""Michael""]
            }
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.name[0].given[1]",
            Value = "William",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        var givenNames = names?[0]?.AsObject()?["given"]?.AsArray();
        Assert.Equal("William", givenNames?[1]?.GetValue<string>());
    }

    #endregion

    #region Complex FHIRPath Tests

    [Fact]
    public async Task GivenComplexFhirPath_WhenReplacing_ThenValueIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["address"] = JsonNode.Parse(@"[
            {
                ""use"": ""home"",
                ""city"": ""Boston""
            },
            {
                ""use"": ""work"",
                ""city"": ""Cambridge""
            }
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.address.where(use='home').city",
            Value = "New York",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var addresses = result.MutableNode["address"]?.AsArray();
        Assert.Equal("New York", addresses?[0]?.AsObject()?["city"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenFirstFunction_WhenReplacing_ThenFirstElementIsUpdated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["telecom"] = JsonNode.Parse(@"[
            {
                ""system"": ""phone"",
                ""value"": ""555-1234""
            },
            {
                ""system"": ""email"",
                ""value"": ""john@example.com""
            }
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.telecom.first().value",
            Value = "555-9999",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var telecom = result.MutableNode["telecom"]?.AsArray();
        Assert.Equal("555-9999", telecom?[0]?.AsObject()?["value"]?.GetValue<string>());
    }

    #endregion

    #region Complex Object Replacement Tests

    [Fact]
    public async Task GivenComplexObject_WhenReplacing_ThenEntireObjectReplaced()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["name"] = JsonNode.Parse(@"[
            {
                ""family"": ""Doe"",
                ""given"": [""John""]
            }
        ]");

        var newName = JsonNode.Parse(@"{
            ""family"": ""Smith"",
            ""given"": [""Jane"", ""Marie""]
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.name[0]",
            Value = newName,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        var firstName = names?[0]?.AsObject();
        Assert.Equal("Smith", firstName?["family"]?.GetValue<string>());
        var givenNames = firstName?["given"]?.AsArray();
        Assert.Equal(2, givenNames?.Count);
        Assert.Equal("Jane", givenNames?[0]?.GetValue<string>());
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task GivenNullValue_WhenReplacing_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.gender",
            Value = null,
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullPath_WhenReplacing_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = null,
            Value = "test",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenEmptyPath_WhenReplacing_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = string.Empty,
            Value = "test",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenImmutablePropertyId_WhenReplacing_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.id",
            Value = "456",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenImmutablePropertyMetaVersionId_WhenReplacing_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.Meta.VersionId = "1";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.meta.versionId",
            Value = "2",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNonExistentPath_WhenReplacing_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Replace,
            Path = "Patient.nonExistentProperty",
            Value = "test",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    #endregion
}
