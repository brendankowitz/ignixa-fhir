// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class FhirAuthorizationServiceTests
{
    [Fact]
    public async Task AuthorizeAsync_AllHandlersPass_ReturnsSuccess()
    {
        // Arrange
        var handler1 = CreateMockHandler(10, AuthorizationResult.Success());
        var handler2 = CreateMockHandler(20, AuthorizationResult.Success());
        var handler3 = CreateMockHandler(30, AuthorizationResult.Success());

        var service = CreateService([handler1, handler2, handler3]);
        var context = CreateContext();

        // Act
        var result = await service.AuthorizeAsync(context);

        // Assert
        result.Allowed.ShouldBeTrue();
        result.DenialReason.ShouldBeNull();
    }

    [Fact]
    public async Task AuthorizeAsync_FirstHandlerDenies_StopsAndReturnsDenial()
    {
        // Arrange
        var handler1 = CreateMockHandler(10, AuthorizationResult.Denied("Authentication required"));
        var handler2 = CreateMockHandler(20, AuthorizationResult.Success());

        var service = CreateService([handler1, handler2]);
        var context = CreateContext();

        // Act
        var result = await service.AuthorizeAsync(context);

        // Assert
        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldBe("Authentication required");

        // Verify second handler was not called (fail-fast)
        await handler2.DidNotReceive().HandleAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_MiddleHandlerDenies_StopsAndReturnsDenial()
    {
        // Arrange
        var handler1 = CreateMockHandler(10, AuthorizationResult.Success());
        var handler2 = CreateMockHandler(20, AuthorizationResult.Denied("Tenant access denied"));
        var handler3 = CreateMockHandler(30, AuthorizationResult.Success());

        var service = CreateService([handler1, handler2, handler3]);
        var context = CreateContext();

        // Act
        var result = await service.AuthorizeAsync(context);

        // Assert
        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldBe("Tenant access denied");

        // Verify third handler was not called
        await handler3.DidNotReceive().HandleAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_MultipleFilters_MergesFilters()
    {
        // Arrange
        var filter1 = FhirAuthorizationFilter.ForPatient("Patient/123");
        var filter2 = FhirAuthorizationFilter.ForPractitioner("Practitioner/456");

        var handler1 = CreateMockHandler(10, AuthorizationResult.SuccessWithFilter(filter1));
        var handler2 = CreateMockHandler(20, AuthorizationResult.SuccessWithFilter(filter2));

        var service = CreateService([handler1, handler2]);
        var context = CreateContext();

        // Act
        var result = await service.AuthorizeAsync(context);

        // Assert
        result.Allowed.ShouldBeTrue();
        result.Filter.ShouldNotBeNull();
        result.Filter!.PatientFilter.ShouldBe("Patient/123");
        result.Filter!.PractitionerFilter.ShouldBe("Practitioner/456");
    }

    [Fact]
    public async Task AuthorizeAsync_HandlersExecuteInPriorityOrder()
    {
        // Arrange
        var executionOrder = new List<int>();

        var handler1 = Substitute.For<IAuthorizationHandler>();
        handler1.Priority.Returns(30);
        handler1.HandleAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executionOrder.Add(30);
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        var handler2 = Substitute.For<IAuthorizationHandler>();
        handler2.Priority.Returns(10);
        handler2.HandleAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executionOrder.Add(10);
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        var handler3 = Substitute.For<IAuthorizationHandler>();
        handler3.Priority.Returns(20);
        handler3.HandleAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executionOrder.Add(20);
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        // Pass handlers in random order
        var service = CreateService([handler1, handler2, handler3]);
        var context = CreateContext();

        // Act
        await service.AuthorizeAsync(context);

        // Assert - should execute in priority order: 10, 20, 30
        executionOrder.ShouldBe([10, 20, 30]);
    }

    [Fact]
    public async Task AuthorizeAsync_NoHandlers_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService([]);
        var context = CreateContext();

        // Act
        var result = await service.AuthorizeAsync(context);

        // Assert
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_SingleFilter_ReturnsWithFilter()
    {
        // Arrange
        var filter = FhirAuthorizationFilter.ForPatient("Patient/123");
        var handler = CreateMockHandler(10, AuthorizationResult.SuccessWithFilter(filter));

        var service = CreateService([handler]);
        var context = CreateContext();

        // Act
        var result = await service.AuthorizeAsync(context);

        // Assert
        result.Allowed.ShouldBeTrue();
        result.Filter.ShouldNotBeNull();
        result.Filter!.PatientFilter.ShouldBe("Patient/123");
    }

    #region Helper Methods

    private static IAuthorizationHandler CreateMockHandler(int priority, AuthorizationResult result)
    {
        var handler = Substitute.For<IAuthorizationHandler>();
        handler.Priority.Returns(priority);
        handler.HandleAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<AuthorizationResult>(result));
        return handler;
    }

    private static FhirAuthorizationService CreateService(IEnumerable<IAuthorizationHandler> handlers)
    {
        return new FhirAuthorizationService(
            handlers,
            NullLogger<FhirAuthorizationService>.Instance);
    }

    private static FhirAuthorizationContext CreateContext()
    {
        var httpContext = Substitute.For<HttpContext>();
        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.TenantId.Returns(1);

        return new FhirAuthorizationContext
        {
            RequestContext = requestContext,
            UserId = "user123",
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            ResourceId = "123",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
