// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Execution;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ISchema = HotChocolate.ISchema;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class GraphQlExecutionServiceTests
{
    private static IRequestExecutorResolver BuildResolver(IRequestExecutor executor, string schemaName)
    {
        var resolver = Substitute.For<IRequestExecutorResolver>();
        resolver.GetRequestExecutorAsync(schemaName, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IRequestExecutor>(executor));
        return resolver;
    }

    private static ILogger<GraphQlExecutionService> BuildLogger()
        => Substitute.For<ILogger<GraphQlExecutionService>>();

    private static void StubSchemaWithResourceTypes(IRequestExecutor executor, params string[] resourceTypes)
    {
        var named = resourceTypes
            .Select(name =>
            {
                var type = Substitute.For<INamedType>();
                type.Name.Returns(name);
                return type;
            })
            .ToList();

        var schema = Substitute.For<ISchema>();
        schema.Types.Returns(named);

        executor.Schema.Returns(schema);
    }

    [Fact]
    public async Task GivenValidQuery_WhenExecuteAsync_ThenDelegatesToExecutor()
    {
        // Arrange
        var expectedResult = Substitute.For<IExecutionResult>();
        var executor = Substitute.For<IRequestExecutor>();
        executor.ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("{ __typename }", null, null);

        // Act
        var result = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);

        // Assert
        result.ShouldBe(expectedResult);
        await executor.Received(1).ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenInstanceQuery_WhenExecuteInstanceAsync_ThenDelegatesToExecutor()
    {
        // Arrange
        var expectedResult = Substitute.For<IExecutionResult>();
        var executor = Substitute.For<IRequestExecutor>();
        executor.ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        StubSchemaWithResourceTypes(executor, "Patient");
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("{ id }", null, null);

        // Act
        var result = await service.ExecuteInstanceAsync(body, FhirVersion.R4, "Patient", "p1", CancellationToken.None);

        // Assert
        result.ShouldBe(expectedResult);
        await executor.Received(1).ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenUnknownResourceType_WhenExecuteInstanceAsync_ThenReturnsErrorWithoutExecuting()
    {
        // Arrange
        var executor = Substitute.For<IRequestExecutor>();
        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        StubSchemaWithResourceTypes(executor, "Patient");
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("{ id }", null, null);

        // Act
        var result = await service.ExecuteInstanceAsync(body, FhirVersion.R4, "NotAType", "p1", CancellationToken.None);

        // Assert
        var operationResult = result.ShouldBeAssignableTo<IOperationResult>();
        operationResult!.Errors![0].Code.ShouldBe("FHIR_UNKNOWN_RESOURCE_TYPE");
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNonBareInstanceQuery_WhenExecuteInstanceAsync_ThenReturnsErrorWithoutExecuting()
    {
        // Arrange
        var executor = Substitute.For<IRequestExecutor>();
        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        StubSchemaWithResourceTypes(executor, "Patient");
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("query { id }", null, null);

        // Act
        var result = await service.ExecuteInstanceAsync(body, FhirVersion.R4, "Patient", "p1", CancellationToken.None);

        // Assert
        var operationResult = result.ShouldBeAssignableTo<IOperationResult>();
        operationResult!.Errors![0].Code.ShouldBe("FHIR_INVALID_INSTANCE_QUERY");
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenInvalidResourceId_WhenExecuteInstanceAsync_ThenReturnsErrorWithoutExecuting()
    {
        // Arrange
        var executor = Substitute.For<IRequestExecutor>();
        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        StubSchemaWithResourceTypes(executor, "Patient");
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("{ id }", null, null);

        // Act — id contains characters outside the FHIR id grammar
        var result = await service.ExecuteInstanceAsync(body, FhirVersion.R4, "Patient", "bad id\"", CancellationToken.None);

        // Assert
        var operationResult = result.ShouldBeAssignableTo<IOperationResult>();
        operationResult!.Errors![0].Code.ShouldBe("FHIR_INVALID_ID");
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenQueryWithOperationName_WhenExecuteAsync_ThenForwardsOperationName()
    {
        // Arrange
        IOperationRequest? capturedRequest = null;
        var expectedResult = Substitute.For<IExecutionResult>();
        var executor = Substitute.For<IRequestExecutor>();
        executor.ExecuteAsync(Arg.Do<IOperationRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("query MyOp { __typename }", "MyOp", null);

        // Act
        await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);

        // Assert
        capturedRequest.ShouldNotBeNull();
        capturedRequest.OperationName.ShouldBe("MyOp");
    }

    [Fact]
    public async Task GivenQueryWithVariables_WhenExecuteAsync_ThenDelegatesToExecutorWithRequest()
    {
        // Arrange
        var expectedResult = Substitute.For<IExecutionResult>();
        var executor = Substitute.For<IRequestExecutor>();
        executor.ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var variables = JsonSerializer.Deserialize<JsonElement>("""{"id":"abc","count":5}""");
        var body = new GraphQlRequestBody("query($id:ID!,$count:Int){__typename}", null, variables);

        // Act
        var result = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);

        // Assert
        result.ShouldBe(expectedResult);
        await executor.Received(1).ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNullQuery_WhenExecuteAsync_ThenReturnsGraphQlErrorResult()
    {
        // Arrange
        var executor = Substitute.For<IRequestExecutor>();
        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody(null, null, null);

        // Act
        var result = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);

        // Assert — malformed input must surface as a GraphQL error, never an HTTP 500.
        var operationResult = result.ShouldBeAssignableTo<IOperationResult>();
        operationResult!.Errors.ShouldNotBeNull();
        operationResult.Errors!.ShouldNotBeEmpty();
        operationResult.Errors[0].Code.ShouldBe("FHIR_SYNTAX_ERROR");
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMalformedQuery_WhenExecuteAsync_ThenReturnsGraphQlErrorResult()
    {
        // Arrange
        var executor = Substitute.For<IRequestExecutor>();
        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        var service = new GraphQlExecutionService(resolver, BuildLogger());
        var body = new GraphQlRequestBody("{ unterminated", null, null);

        // Act
        var result = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);

        // Assert
        var operationResult = result.ShouldBeAssignableTo<IOperationResult>();
        operationResult!.Errors!.ShouldNotBeEmpty();
        operationResult.Errors[0].Code.ShouldBe("FHIR_SYNTAX_ERROR");
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenDifferentFhirVersions_WhenExecuteAsync_ThenUsesVersionSpecificSchema()
    {
        // Arrange
        var r4Result = Substitute.For<IExecutionResult>();
        var r5Result = Substitute.For<IExecutionResult>();

        var r4Executor = Substitute.For<IRequestExecutor>();
        r4Executor.ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(r4Result);

        var r5Executor = Substitute.For<IRequestExecutor>();
        r5Executor.ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(r5Result);

        var r4SchemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var r5SchemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R5);

        var resolverMock = Substitute.For<IRequestExecutorResolver>();
        resolverMock.GetRequestExecutorAsync(r4SchemaName, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IRequestExecutor>(r4Executor));
        resolverMock.GetRequestExecutorAsync(r5SchemaName, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IRequestExecutor>(r5Executor));

        var service = new GraphQlExecutionService(resolverMock, BuildLogger());
        var body = new GraphQlRequestBody("{ __typename }", null, null);

        // Act
        var r4Actual = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);
        var r5Actual = await service.ExecuteAsync(body, FhirVersion.R5, CancellationToken.None);

        // Assert
        r4Actual.ShouldBe(r4Result);
        r5Actual.ShouldBe(r5Result);
    }
}
