// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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

public class InsertOperationExecutorTests
{
    private readonly InsertOperationExecutor _executor;

    public InsertOperationExecutorTests()
    {
        var logger = Substitute.For<ILogger<InsertOperationExecutor>>();
        var evaluator = new Ignixa.FhirPath.Evaluation.FhirPathEvaluator();
        var compiler = new FhirPathParser();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        var versionContext = new FhirVersionContext(loggerFactory, searchParamOptions);
        var structureProvider = versionContext.GetBaseSchemaProvider(FhirSpecification.R4);
        var fhirPathHelper = new FhirPathPatchHelper(evaluator, compiler, structureProvider);
        _executor = new InsertOperationExecutor(logger, fhirPathHelper);
    }

    [Fact]
    public async Task GivenIndex0_WhenInsertingIntoArray_ThenValueIsInsertedAtBeginning()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"": ""Doe""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name",
            Index = 0,
            Value = JsonNode.Parse(@"{""family"": ""Smith""}"),
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var names = result.MutableNode["name"]?.AsArray();
        Assert.Equal(2, names?.Count);
        Assert.Equal("Smith", names?[0]?.AsObject()?["family"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenIndexInMiddle_WhenInserting_ThenValueIsInsertedAtIndex()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""given"":[""John"",""William""]}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name[0].given",
            Index = 1,
            Value = "Michael",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var givenNames = result.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["given"]?.AsArray();
        Assert.Equal("Michael", givenNames?[1]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenIndexAtEnd_WhenInserting_ThenValueIsAppended()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""given"":[""John""]}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name[0].given",
            Index = 1,
            Value = "Doe",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var givenNames = result.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["given"]?.AsArray();
        Assert.Equal(2, givenNames?.Count);
        Assert.Equal("Doe", givenNames?[1]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenNonExistentArray_WhenInserting_ThenArrayIsCreated()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name",
            Index = 0,
            Value = JsonNode.Parse(@"{""family"": ""Doe""}"),
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var names = result.MutableNode["name"]?.AsArray();
        Assert.Single(names);
    }

    [Fact]
    public async Task GivenNullPath_WhenInserting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = null,
            Index = 0,
            Value = "test",
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullValue_WhenInserting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name",
            Index = 0,
            Value = null,
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullIndex_WhenInserting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name",
            Index = null,
            Value = JsonNode.Parse(@"{""family"":""Smith""}"),
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNegativeIndex_WhenInserting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name",
            Index = -1,
            Value = JsonNode.Parse(@"{""family"":""Smith""}"),
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenIndexOutOfRange_WhenInserting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.name",
            Index = 5,
            Value = JsonNode.Parse(@"{""family"":""Smith""}"),
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNonArrayProperty_WhenInserting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["gender"] = "male";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.gender",
            Index = 0,
            Value = "female",
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenComplexObject_WhenInsertingAtIndex_ThenObjectIsInserted()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["address"] = JsonNode.Parse(@"[{""city"":""Boston""},{""city"":""New York""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Insert,
            Path = "Patient.address",
            Index = 1,
            Value = JsonNode.Parse(@"{""city"":""Cambridge"",""state"":""MA""}"),
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var addresses = result.MutableNode["address"]?.AsArray();
        Assert.Equal(3, addresses?.Count);
        Assert.Equal("Cambridge", addresses?[1]?.AsObject()?["city"]?.GetValue<string>());
    }
}
