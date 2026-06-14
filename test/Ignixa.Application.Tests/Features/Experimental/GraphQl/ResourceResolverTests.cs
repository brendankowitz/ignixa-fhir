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
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class ResourceResolverTests
{
    private static readonly byte[] ValidPatientJson = Encoding.UTF8.GetBytes(
        """{"resourceType":"Patient","id":"p1","name":[{"text":"John"}]}""");

    private static SearchEntryResult MakeResult(byte[] json, bool isDeleted = false)
        => new SearchEntryResult("Patient", "p1", "1", DateTimeOffset.UtcNow, json)
        {
            IsDeleted = isDeleted,
        };

    [Fact]
    public async Task GivenExistingResource_WhenResolvingById_ThenReturnsJsonElement()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceQuery>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(ValidPatientJson));

        var resolver = new ResourceResolver(mediator, NullLogger<ResourceResolver>.Instance);

        // Act
        var result = await resolver.ResolveByIdAsync("Patient", "p1", CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Value.GetProperty("resourceType").GetString().ShouldBe("Patient");
        result.Value.GetProperty("id").GetString().ShouldBe("p1");
    }

    [Fact]
    public async Task GivenDeletedResource_WhenResolvingById_ThenReturnsNull()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceQuery>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(ValidPatientJson, isDeleted: true));

        var resolver = new ResourceResolver(mediator, NullLogger<ResourceResolver>.Instance);

        // Act
        var result = await resolver.ResolveByIdAsync("Patient", "p1", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenMissingResource_WhenResolvingById_ThenReturnsNull()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceQuery>(), Arg.Any<CancellationToken>())
            .Returns((SearchEntryResult?)null);

        var resolver = new ResourceResolver(mediator, NullLogger<ResourceResolver>.Instance);

        // Act
        var result = await resolver.ResolveByIdAsync("Patient", "p1", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenExistingResource_WhenResolvingById_ThenSendsCorrectQuery()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceQuery>(), Arg.Any<CancellationToken>())
            .Returns(MakeResult(ValidPatientJson));

        var resolver = new ResourceResolver(mediator, NullLogger<ResourceResolver>.Instance);

        // Act
        await resolver.ResolveByIdAsync("Observation", "obs123", CancellationToken.None);

        // Assert
        await mediator.Received(1).SendAsync(
            Arg.Is<GetResourceQuery>(q => q.ResourceType == "Observation" && q.Id == "obs123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenFhirException_WhenResolvingById_ThenThrowsCodedGraphQLException()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceNotFoundException("Patient/p1 was not found"));

        var resolver = new ResourceResolver(mediator, NullLogger<ResourceResolver>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.ResolveByIdAsync("Patient", "p1", CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("FHIR_NOT_FOUND");
    }

    [Fact]
    public async Task GivenNonFhirException_WhenResolvingById_ThenPropagatesUncaught()
    {
        // Arrange — non-FHIR exceptions are left for the error filter to log and mask
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database offline"));

        var resolver = new ResourceResolver(mediator, NullLogger<ResourceResolver>.Instance);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => resolver.ResolveByIdAsync("Patient", "p1", CancellationToken.None));
    }
}
