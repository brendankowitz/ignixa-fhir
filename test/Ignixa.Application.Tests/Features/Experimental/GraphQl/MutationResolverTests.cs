// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using HotChocolate;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using AbstractionsResourceKey = Ignixa.Abstractions.ResourceKey;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class MutationResolverTests
{
    private const string ValidPatientJson =
        """{"resourceType":"Patient","id":"p1","name":[{"text":"John"}]}""";

    private static UpdateResult MakeUpdateResult(string json)
        => new(
            new AbstractionsResourceKey("Patient", "p1", "1"),
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json)),
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task GivenValidJson_WhenCreating_ThenSendsCreateCommand()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpdateResult(ValidPatientJson));

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        var result = await resolver.CreateAsync("Patient", ValidPatientJson, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Value.GetProperty("resourceType").GetString().ShouldBe("Patient");

        await mediator.Received(1).SendAsync(
            Arg.Is<CreateOrUpdateResourceCommand>(c =>
                c.ResourceType == "Patient" &&
                c.HttpMethod == System.Net.Http.HttpMethod.Post),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenValidJson_WhenCreating_ThenGeneratesServerId()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpdateResult(ValidPatientJson));

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        await resolver.CreateAsync("Patient", ValidPatientJson, CancellationToken.None);

        // Assert — ID should be a non-empty server-assigned value
        await mediator.Received(1).SendAsync(
            Arg.Is<CreateOrUpdateResourceCommand>(c =>
                !string.IsNullOrWhiteSpace(c.Id) &&
                c.JsonNode != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNullResult_WhenCreating_ThenReturnsNull()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns((UpdateResult?)null);

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        var result = await resolver.CreateAsync("Patient", ValidPatientJson, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenValidJson_WhenUpdating_ThenSendsUpdateCommand()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpdateResult(ValidPatientJson));

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        var result = await resolver.UpdateAsync("Patient", "p1", ValidPatientJson, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Value.GetProperty("id").GetString().ShouldBe("p1");

        await mediator.Received(1).SendAsync(
            Arg.Is<CreateOrUpdateResourceCommand>(c =>
                c.ResourceType == "Patient" &&
                c.Id == "p1" &&
                c.HttpMethod == System.Net.Http.HttpMethod.Put),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNullResult_WhenUpdating_ThenReturnsNull()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns((UpdateResult?)null);

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        var result = await resolver.UpdateAsync("Patient", "p1", ValidPatientJson, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenResourceId_WhenDeleting_ThenSendsDeleteCommand()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<DeleteResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        var result = await resolver.DeleteAsync("Patient", "p1", CancellationToken.None);

        // Assert
        result.ShouldBeTrue();

        await mediator.Received(1).SendAsync(
            Arg.Is<DeleteResourceCommand>(c =>
                c.ResourceType == "Patient" &&
                c.Id == "p1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenObservationType_WhenCreating_ThenUsesCorrectResourceType()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var obsJson = """{"resourceType":"Observation","id":"obs1","status":"final"}""";
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .Returns(MakeUpdateResult(obsJson));

        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act
        await resolver.CreateAsync("Observation", obsJson, CancellationToken.None);

        // Assert
        await mediator.Received(1).SendAsync(
            Arg.Is<CreateOrUpdateResourceCommand>(c =>
                c.ResourceType == "Observation" &&
                c.HttpMethod == System.Net.Http.HttpMethod.Post),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMalformedJson_WhenCreating_ThenThrowsGraphQLExceptionWithInvalidResourceCode()
    {
        // Arrange — "not-json" fails at ResourceJsonNode.Parse before the mediator is reached
        var mediator = Substitute.For<IMediator>();
        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.CreateAsync("Patient", "not-json", CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("INVALID_RESOURCE");

        await mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenValidationException_WhenUpdating_ThenThrowsGraphQLExceptionWithInvalidResourceCode()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BadRequestException("Resource validation failed"));
        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.UpdateAsync("Patient", "p1", ValidPatientJson, CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("INVALID_RESOURCE");
        ex.Errors[0].Message.ShouldBe("Resource validation failed");
    }

    [Fact]
    public async Task GivenPreconditionFailedException_WhenUpdating_ThenThrowsGraphQLExceptionWithVersionConflictCode()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PreconditionFailedException("Version conflict"));
        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.UpdateAsync("Patient", "p1", ValidPatientJson, CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("FHIR_VERSION_CONFLICT");
    }

    [Fact]
    public async Task GivenResourceVersionConflictException_WhenCreating_ThenThrowsGraphQLExceptionWithVersionConflictCode()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceVersionConflictException("Patient", "p1", 2, 1));
        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.CreateAsync("Patient", ValidPatientJson, CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("FHIR_VERSION_CONFLICT");
    }

    [Fact]
    public async Task GivenResourceNotFoundException_WhenDeleting_ThenThrowsGraphQLExceptionWithNotFoundCode()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<DeleteResourceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceNotFoundException("Patient/p1 was not found"));
        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.DeleteAsync("Patient", "p1", CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("FHIR_NOT_FOUND");
    }

    [Fact]
    public async Task GivenUnexpectedException_WhenUpdating_ThenPropagatesToErrorFilter()
    {
        // Arrange — non-FHIR exceptions are not caught; the error filter logs and masks them
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database offline"));
        var resolver = new MutationResolver(mediator, NullLogger<MutationResolver>.Instance);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => resolver.UpdateAsync("Patient", "p1", ValidPatientJson, CancellationToken.None));
    }
}
