// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Execution;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Execution;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using NSubstitute;
using Shouldly;

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
        var service = new GraphQlExecutionService(resolver);
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
        var service = new GraphQlExecutionService(resolver);
        var body = new GraphQlRequestBody("{ __typename }", null, null);

        // Act
        var result = await service.ExecuteInstanceAsync(body, FhirVersion.R4, "Patient", "p1", CancellationToken.None);

        // Assert
        result.ShouldBe(expectedResult);
        await executor.Received(1).ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
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
        var service = new GraphQlExecutionService(resolver);
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
        var service = new GraphQlExecutionService(resolver);
        var variables = JsonSerializer.Deserialize<JsonElement>("""{"id":"abc","count":5}""");
        var body = new GraphQlRequestBody("query($id:ID!,$count:Int){__typename}", null, variables);

        // Act
        var result = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);

        // Assert
        result.ShouldBe(expectedResult);
        await executor.Received(1).ExecuteAsync(Arg.Any<IOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNullQuery_WhenExecuteAsync_ThenThrowsArgumentException()
    {
        // Arrange
        var executor = Substitute.For<IRequestExecutor>();
        var schemaName = GraphQlNamingHelper.GetSchemaName(FhirVersion.R4);
        var resolver = BuildResolver(executor, schemaName);
        var service = new GraphQlExecutionService(resolver);
        var body = new GraphQlRequestBody(null, null, null);

        // Act / Assert
        await Should.ThrowAsync<ArgumentException>(
            () => service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None));
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

        var service = new GraphQlExecutionService(resolverMock);
        var body = new GraphQlRequestBody("{ __typename }", null, null);

        // Act
        var r4Actual = await service.ExecuteAsync(body, FhirVersion.R4, CancellationToken.None);
        var r5Actual = await service.ExecuteAsync(body, FhirVersion.R5, CancellationToken.None);

        // Assert
        r4Actual.ShouldBe(r4Result);
        r5Actual.ShouldBe(r5Result);
    }
}
