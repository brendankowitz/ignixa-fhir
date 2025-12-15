// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AuthorizationFilter = Ignixa.Api.Filters.FhirAuthorizationFilter;
using DataFilter = Ignixa.Application.Features.Authorization.Models.FhirAuthorizationFilter;

namespace Ignixa.Api.Tests.Filters;

public class FhirAuthorizationFilterTests
{
    private readonly IFhirAuthorizationService _authzService;
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly AuthorizationOptions _authzOptions;
    private readonly AuthorizationFilter _filter;

    public FhirAuthorizationFilterTests()
    {
        _authzService = Substitute.For<IFhirAuthorizationService>();
        _fhirContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        _authzOptions = new AuthorizationOptions { Enabled = true };

        var options = Substitute.For<IOptions<AuthorizationOptions>>();
        options.Value.Returns(_authzOptions);

        _filter = new AuthorizationFilter(
            _authzService,
            _fhirContextAccessor,
            options,
            NullLogger<AuthorizationFilter>.Instance);
    }

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_AuthorizationDisabled_SkipsCheck()
    {
        // Arrange
        _authzOptions.Enabled = false;
        var (context, next) = CreateFilterContext();
        var nextCalled = false;
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>()).Returns(_ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        nextCalled.Should().BeTrue();
        await _authzService.DidNotReceive().AuthorizeAsync(
            Arg.Any<FhirAuthorizationContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AuthorizationAllowed_ContinuesToNext()
    {
        // Arrange
        var (context, next) = CreateFilterContext();
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.Success());

        var nextCalled = false;
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>()).Returns(_ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AuthorizationDenied_Returns403()
    {
        // Arrange
        var (context, next) = CreateFilterContext();
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.Denied("Access denied"));

        // Act
        var result = await _filter.InvokeAsync(context, next);

        // Assert
        result.Should().NotBeNull();
        await next.DidNotReceive().Invoke(Arg.Any<EndpointFilterInvocationContext>());
    }

    [Fact]
    public async Task InvokeAsync_ExceptionThrown_Returns500OperationOutcome()
    {
        // Arrange
        var (context, next) = CreateFilterContext();
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns<AuthorizationResult>(_ => throw new InvalidOperationException("Something went wrong"));

        // Act
        var result = await _filter.InvokeAsync(context, next);

        // Assert
        result.Should().NotBeNull();
        await next.DidNotReceive().Invoke(Arg.Any<EndpointFilterInvocationContext>());
    }

    [Fact]
    public async Task InvokeAsync_WithFilter_StoresFilterInHttpContext()
    {
        // Arrange
        var (context, next) = CreateFilterContext();
        var filter = DataFilter.ForPatient("Patient/123");
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.SuccessWithFilter(filter));

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        context.HttpContext.Items.Should().ContainKey("FhirAuthorizationFilter");
        var storedFilter = context.HttpContext.Items["FhirAuthorizationFilter"] as DataFilter;
        storedFilter.Should().NotBeNull();
        storedFilter!.PatientFilter.Should().Be("Patient/123");
    }

    #endregion

    #region Route Parsing Integration Tests

    [Fact]
    public async Task InvokeAsync_GetPatientById_SetsReadInteraction()
    {
        // Arrange
        var httpContext = CreateHttpContext("GET", "/tenant/1/Patient/123");
        httpContext.Request.RouteValues["resourceType"] = "Patient";
        httpContext.Request.RouteValues["id"] = "123";

        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? capturedContext = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Interaction.Should().Be(FhirInteraction.Read);
        capturedContext.ResourceType.Should().Be("Patient");
        capturedContext.ResourceId.Should().Be("123");
    }

    [Fact]
    public async Task InvokeAsync_PostPatient_SetsCreateInteraction()
    {
        // Arrange
        var httpContext = CreateHttpContext("POST", "/tenant/1/Patient");
        httpContext.Request.RouteValues["resourceType"] = "Patient";

        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? capturedContext = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Interaction.Should().Be(FhirInteraction.Create);
    }

    [Fact]
    public async Task InvokeAsync_CompartmentSearch_SetsSearchTypeInteraction()
    {
        // Arrange
        var httpContext = CreateHttpContext("GET", "/tenant/1/Patient/123/*");
        httpContext.Request.RouteValues["compartmentType"] = "Patient";
        httpContext.Request.RouteValues["compartmentId"] = "123";

        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? capturedContext = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Interaction.Should().Be(FhirInteraction.SearchType);
    }

    [Fact]
    public async Task InvokeAsync_Metadata_SetsCapabilitiesInteraction()
    {
        // Arrange
        var httpContext = CreateHttpContext("GET", "/tenant/1/metadata");

        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? capturedContext = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Interaction.Should().Be(FhirInteraction.Capabilities);
    }

    #endregion

    #region Claims Extraction Tests

    [Fact]
    public async Task InvokeAsync_ExtractsUserIdFromClaims()
    {
        // Arrange
        var httpContext = CreateHttpContext("GET", "/tenant/1/Patient/123");
        httpContext.Request.RouteValues["resourceType"] = "Patient";
        httpContext.Request.RouteValues["id"] = "123";

        var claims = new List<Claim>
        {
            new(FhirClaimTypes.Subject, "user-123")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? capturedContext = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.UserId.Should().Be("user-123");
        capturedContext.RequestContext.Should().NotBeNull();
        capturedContext.RequestContext.TenantId.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_ExtractsRolesFromMultipleClaimTypes()
    {
        // Arrange
        var httpContext = CreateHttpContext("GET", "/tenant/1/Patient/123");
        httpContext.Request.RouteValues["resourceType"] = "Patient";
        httpContext.Request.RouteValues["id"] = "123";

        var claims = new List<Claim>
        {
            new(FhirClaimTypes.Role, "Admin"),
            new(FhirClaimTypes.Roles, "Clinician")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? capturedContext = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedContext = callInfo.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });

        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns(ValueTask.FromResult<object?>(Results.Ok()));

        // Act
        await _filter.InvokeAsync(context, next);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Roles.Should().Contain("Admin");
        capturedContext.Roles.Should().Contain("Clinician");
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContext(string method = "GET", string path = "/tenant/1/Patient/123")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        httpContext.Request.RouteValues = new RouteValueDictionary();
        return httpContext;
    }

    private (EndpointFilterInvocationContext context, EndpointFilterDelegate next) CreateFilterContext(HttpContext? httpContext = null)
    {
        httpContext ??= CreateHttpContext();

        var fhirContext = Substitute.For<IFhirRequestContext>();
        fhirContext.TenantId.Returns(1);
        _fhirContextAccessor.RequestContext.Returns(fhirContext);

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);

        var next = Substitute.For<EndpointFilterDelegate>();

        return (context, next);
    }

    #endregion
}
