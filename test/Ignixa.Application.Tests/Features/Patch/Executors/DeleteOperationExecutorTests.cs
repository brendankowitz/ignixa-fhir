// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Application.Infrastructure;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch.Executors;

public class DeleteOperationExecutorTests
{
    private readonly DeleteOperationExecutor _executor;

    public DeleteOperationExecutorTests()
    {
        var logger = Substitute.For<ILogger<DeleteOperationExecutor>>();
        var evaluator = new Ignixa.FhirPath.Evaluation.FhirPathEvaluator();
        var compiler = new Ignixa.FhirPath.FhirPathCompiler();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var versionContext = new FhirVersionContext(loggerFactory);
        var structureProvider = versionContext.GetSchemaProvider(Ignixa.SourceNodeSerialization.FhirSpecification.R4);
        var fhirPathHelper = new FhirPathPatchHelper(evaluator, compiler, structureProvider);
        _executor = new DeleteOperationExecutor(logger, fhirPathHelper);
    }

    [Fact]
    public async Task GivenSimpleProperty_WhenDeleting_ThenPropertyIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["gender"] = "male";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.gender",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        Assert.Null(result.MutableNode["gender"]);
    }

    [Fact]
    public async Task GivenArrayElement_WhenDeleting_ThenElementIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe""},{""family"":""Smith""}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.name[0]",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var names = result.MutableNode["name"]?.AsArray();
        Assert.Single(names);
        Assert.Equal("Smith", names?[0]?.AsObject()?["family"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenNestedProperty_WhenDeleting_ThenPropertyIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe"",""given"":[""John""]}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.name[0].family",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var firstName = result.MutableNode["name"]?.AsArray()?[0]?.AsObject();
        Assert.Null(firstName?["family"]);
        Assert.NotNull(firstName?["given"]);
    }

    [Fact]
    public async Task GivenNestedArrayElement_WhenDeleting_ThenElementIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""given"":[""John"",""Michael""]}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.name[0].given[0]",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var givenNames = result.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["given"]?.AsArray();
        Assert.Single(givenNames);
        Assert.Equal("Michael", givenNames?[0]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenComplexFhirPath_WhenDeleting_ThenMatchingElementIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["address"] = JsonNode.Parse(@"[
            {""use"":""home"",""city"":""Boston""},
            {""use"":""work"",""city"":""Cambridge""}
        ]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.address.where(use='home')",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var addresses = result.MutableNode["address"]?.AsArray();
        Assert.Single(addresses);
        Assert.Equal("work", addresses?[0]?.AsObject()?["use"]?.GetValue<string>());
    }

    [Fact]
    public async Task GivenNullPath_WhenDeleting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var operation = new FhirPatchOperation { Type = FhirPatchOperationType.Delete, Path = null };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenImmutablePropertyId_WhenDeleting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var operation = new FhirPatchOperation { Type = FhirPatchOperationType.Delete, Path = "Patient.id" };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenImmutablePropertyMetaVersionId_WhenDeleting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.Meta.VersionId = "1";

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.meta.versionId",
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNonExistentPath_WhenDeleting_ThenThrowsException()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.nonExistent",
        };

        await Assert.ThrowsAsync<FhirPatchException>(
            () => _executor.ExecuteAsync(resource, operation, CancellationToken.None));
    }

    [Fact]
    public async Task GivenComplexObject_WhenDeleting_ThenEntireObjectIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"":""Doe"",""given"":[""John""]}]");

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.name[0]",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        var names = result.MutableNode["name"]?.AsArray();
        Assert.Empty(names);
    }

    [Fact]
    public async Task GivenBooleanProperty_WhenDeleting_ThenPropertyIsRemoved()
    {
        var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
        resource.MutableNode["active"] = true;

        var operation = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = "Patient.active",
        };

        var result = await _executor.ExecuteAsync(resource, operation, CancellationToken.None);

        Assert.Null(result.MutableNode["active"]);
    }
}
