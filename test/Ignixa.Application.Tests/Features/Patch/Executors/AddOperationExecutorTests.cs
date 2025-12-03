// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Application.Features.Search;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch.Executors;

/// <summary>
/// Unit tests for AddOperationExecutor.
/// Tests adding elements to arrays, creating new arrays, and validation.
/// </summary>
public class AddOperationExecutorTests
{
    private readonly ILogger<AddOperationExecutor> _logger;
    private readonly IJsonNodeMutator _mutator;
    private readonly AddOperationExecutor _executor;

    public AddOperationExecutorTests()
    {
        _logger = Substitute.For<ILogger<AddOperationExecutor>>();

        var evaluator = new Ignixa.FhirPath.Evaluation.FhirPathEvaluator();
        var compiler = new FhirPathParser();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        var versionContext = new FhirVersionContext(loggerFactory, searchParamOptions);

        // Schema provider factory for tests (always returns R4)
        var schemaProviderFactory = () => versionContext.GetBaseSchemaProvider(FhirVersion.R4);
        _mutator = new JsonNodeMutator(evaluator, compiler, schemaProviderFactory);

        _executor = new AddOperationExecutor(_logger, _mutator);
    }

    #region Add to Existing Array Tests

    [Fact]
    public async Task GivenExistingArray_WhenAdding_ThenValueIsAppended()
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
            ""given"": [""Jane""]
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.name",
            Value = newName,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        Assert.NotNull(names);
        Assert.Equal(2, names.Count);
        Assert.Equal("Smith", names[1]?.AsObject()?["family"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenExistingTelecomArray_WhenAddingPhone_ThenPhoneIsAppended()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };
        resource.MutableNode["telecom"] = JsonNode.Parse(@"[
            {
                ""system"": ""email"",
                ""value"": ""john@example.com""
            }
        ]");

        var newPhone = JsonNode.Parse(@"{
            ""system"": ""phone"",
            ""value"": ""555-1234""
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.telecom",
            Value = newPhone,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var telecom = result.MutableNode["telecom"]?.AsArray();
        Assert.NotNull(telecom);
        Assert.Equal(2, telecom.Count);
        Assert.Equal("phone", telecom[1]?.AsObject()?["system"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenNestedArray_WhenAddingToGivenNames_ThenValueIsAppended()
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
            Type = FhirPatchOperationType.Add,
            Path = "Patient.name[0].given",
            Value = "Michael",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        var givenNames = names?[0]?.AsObject()?["given"]?.AsArray();
        Assert.NotNull(givenNames);
        Assert.Equal(2, givenNames.Count);
        Assert.Equal("Michael", givenNames[1]?.GetValue<string>());
    }

    #endregion

    #region Create New Array Tests

    [Fact]
    public async Task GivenNonExistentArray_WhenAdding_ThenArrayIsCreatedWithValue()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var newName = JsonNode.Parse(@"{
            ""family"": ""Doe"",
            ""given"": [""John""]
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.name",
            Value = newName,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        Assert.NotNull(names);
        Assert.Single(names);
        Assert.Equal("Doe", names[0]?.AsObject()?["family"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenNonExistentTelecom_WhenAdding_ThenArrayIsCreated()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var newPhone = JsonNode.Parse(@"{
            ""system"": ""phone"",
            ""value"": ""555-1234""
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.telecom",
            Value = newPhone,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var telecom = result.MutableNode["telecom"]?.AsArray();
        Assert.NotNull(telecom);
        Assert.Single(telecom);
    }

    [Fact]
    public async Task GivenIdentifierArray_WhenAddingIdentifier_ThenArrayIsCreatedOrAppended()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var newIdentifier = JsonNode.Parse(@"{
            ""system"": ""http://example.org/mrn"",
            ""value"": ""12345""
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.identifier",
            Value = newIdentifier,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var identifiers = result.MutableNode["identifier"]?.AsArray();
        Assert.NotNull(identifiers);
        Assert.Single(identifiers);
        Assert.Equal("12345", identifiers[0]?.AsObject()?["value"]?.GetValue<string>());
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task GivenNullPath_WhenAdding_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = null,
            Value = "test",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullValue_WhenAdding_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.name",
            Value = null,
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNonArrayProperty_WhenAdding_ThenThrowsException()
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
            Type = FhirPatchOperationType.Add,
            Path = "Patient.gender",
            Value = "female",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenInvalidParentPath_WhenAdding_ThenThrowsException()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "123",
        };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.nonExistent.property",
            Value = "test",
        };

        // Act & Assert
        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    #endregion

    #region Complex Value Tests

    [Fact]
    public async Task GivenComplexObjectValue_WhenAdding_ThenObjectIsAppended()
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
            }
        ]");

        var newAddress = JsonNode.Parse(@"{
            ""use"": ""work"",
            ""city"": ""Cambridge"",
            ""state"": ""MA""
        }");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = "Patient.address",
            Value = newAddress,
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var addresses = result.MutableNode["address"]?.AsArray();
        Assert.NotNull(addresses);
        Assert.Equal(2, addresses.Count);
        Assert.Equal("Cambridge", addresses[1]?.AsObject()?["city"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenPrimitiveValue_WhenAddingToStringArray_ThenValueIsAppended()
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
            Type = FhirPatchOperationType.Add,
            Path = "Patient.name[0].given",
            Value = "William",
        };

        // Act
        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        // Assert
        var names = result.MutableNode["name"]?.AsArray();
        var givenNames = names?[0]?.AsObject()?["given"]?.AsArray();
        Assert.NotNull(givenNames);
        Assert.Equal(2, givenNames.Count);
        Assert.Equal("William", givenNames[1]?.GetValue<string>());
    }

    #endregion
}
