// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.FhirPath.Parser;
using Ignixa.Search.Infrastructure;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch.Executors;

public class MoveOperationExecutorTests
{
    private readonly MoveOperationExecutor _executor;

    public MoveOperationExecutorTests()
    {
        var logger = Substitute.For<ILogger<MoveOperationExecutor>>();
        var deleteLogger = Substitute.For<ILogger<DeleteOperationExecutor>>();
        var addLogger = Substitute.For<ILogger<AddOperationExecutor>>();

        var evaluator = new Ignixa.FhirPath.Evaluation.FhirPathEvaluator();
        var compiler = new FhirPathParser();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var searchParamOptions = new Ignixa.Search.Definition.SearchParameterResolutionOptions();
        var versionContext = new FhirVersionContext(loggerFactory, searchParamOptions);
        var structureProvider = versionContext.GetBaseSchemaProvider(FhirSpecification.R4);
        var fhirPathHelper = new FhirPathPatchHelper(evaluator, compiler, structureProvider);

        var deleteExecutor = new DeleteOperationExecutor(deleteLogger, fhirPathHelper);
        var addExecutor = new AddOperationExecutor(addLogger, fhirPathHelper);

        _executor = new MoveOperationExecutor(logger, deleteExecutor, addExecutor, fhirPathHelper);
    }

    [Fact]
    public async Task GivenArrayElement_WhenMoving_ThenElementIsMovedToNewArray()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.name[0]",
            Destination = "Patient.address",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var names = result.MutableNode["name"]?.AsArray();
        Assert.Empty(names);

        var addresses = result.MutableNode["address"]?.AsArray();
        Assert.Single(addresses);
    }

    [Fact]
    public async Task GivenNestedArrayElement_WhenMovingWithinArray_ThenElementIsMoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[
            {""given"":[""John""]},
            {""given"":[""Jane""]}
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.name[0].given[0]",
            Destination = "Patient.name[1].given",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var firstName = result.MutableNode["name"]?.AsArray()?[0]?.AsObject();
        Assert.Empty(firstName?["given"]?.AsArray());

        var secondName = result.MutableNode["name"]?.AsArray()?[1]?.AsObject()?["given"]?.AsArray();
        Assert.Equal(2, secondName?.Count);
        Assert.Equal("John", secondName?[1]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenProperty_WhenMovingBetweenObjects_ThenPropertyIsMoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["gender"] = "male";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.gender",
            Destination = "Patient.telecom",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        Assert.Null(result.MutableNode["gender"]);
        var telecom = result.MutableNode["telecom"]?.AsArray();
        Assert.NotNull(telecom);
    }

    [Fact]
    public async Task GivenNullSource_WhenMoving_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = null,
            Destination = "Patient.name",
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullDestination_WhenMoving_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["gender"] = "male";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.gender",
            Destination = null,
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNonExistentSource_WhenMoving_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.nonExistent",
            Destination = "Patient.name",
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenComplexFhirPathSource_WhenMoving_ThenValueIsMoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["address"] = JsonNode.Parse(@"[
            {""use"":""home"",""city"":""Boston""},
            {""use"":""work"",""city"":""Cambridge""}
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.address.where(use='home')",
            Destination = "Patient.contact",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var addresses = result.MutableNode["address"]?.AsArray();
        Assert.Single(addresses);
        Assert.Equal("work", addresses?[0]?.AsObject()?["use"]?.GetValue<string>());

        var contact = result.MutableNode["contact"]?.AsArray();
        Assert.Single(contact);
    }

    [Fact]
    public async Task GivenComplexObject_WhenMoving_ThenEntireObjectIsMoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe"",""given"":[""John""]}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Move,
            Source = "Patient.name[0]",
            Destination = "Patient.link",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var names = result.MutableNode["name"]?.AsArray();
        Assert.Empty(names);

        var link = result.MutableNode["link"]?.AsArray();
        Assert.Single(link);
        Assert.Equal("Doe", link?[0]?.AsObject()?["family"]?.GetValue<string>());
    }
}
