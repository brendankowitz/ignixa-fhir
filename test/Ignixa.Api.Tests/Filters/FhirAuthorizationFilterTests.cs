// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using Ignixa.Api.Filters;
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
        nextCalled.ShouldBeTrue();
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
        nextCalled.ShouldBeTrue();
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
        result.ShouldNotBeNull();
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
        result.ShouldNotBeNull();
        await next.DidNotReceive().Invoke(Arg.Any<EndpointFilterInvocationContext>());
    }

    // A failure after authorization belongs to FhirExceptionMiddleware, which maps it to the right status.
    [Fact]
    public async Task GivenAuthorizedRequest_WhenTheEndpointThrows_ThenTheExceptionPropagates()
    {
        var (context, next) = CreateFilterContext();
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.Success());
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>())
            .Returns<ValueTask<object?>>(_ => throw new ArgumentException("downstream"));

        await Should.ThrowAsync<ArgumentException>(async () => await _filter.InvokeAsync(context, next));
    }

    public static TheoryData<DataFilter> RestrictingFilters =>
    [
        DataFilter.ForPatient("Patient/123"),
        new DataFilter { SearchFilters = new Dictionary<string, string> { ["category"] = "laboratory" } },
    ];

    // No query path applies a compartment or search constraint, so serving the request would return unrestricted data.
    [Theory]
    [MemberData(nameof(RestrictingFilters))]
    public async Task GivenAGrantLimitedByAFilter_WhenInvoked_ThenItIsForbiddenWithoutCallingNext(DataFilter filter)
    {
        var (context, next) = CreateFilterContext();
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.SuccessWithFilter(filter));

        var result = await _filter.InvokeAsync(context, next);

        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        httpContext.Response.Body = new MemoryStream();
        await ((IResult)result!).ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        await next.DidNotReceive().Invoke(Arg.Any<EndpointFilterInvocationContext>());
    }

    [Theory]
    [InlineData("*", "*")]
    [InlineData(null, "Patient")]
    public async Task GivenDeclaredMetadata_WhenInvoked_ThenItAuthorizesAsTheDeclaredInteraction(
        string? declaredType,
        string expectedType)
    {
        var httpContext = CreateHttpContext("POST", "/tenant/1/Patient/$everything");
        httpContext.Request.RouteValues["resourceType"] = "Patient";
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new FhirAuthorizationMetadata(FhirInteraction.SearchSystem, declaredType)),
            "operation"));
        var (context, next) = CreateFilterContext(httpContext);
        FhirAuthorizationContext? captured = null;
        _authzService.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<FhirAuthorizationContext>();
                return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
            });
        next.Invoke(Arg.Any<EndpointFilterInvocationContext>()).Returns(ValueTask.FromResult<object?>(Results.Ok()));

        await _filter.InvokeAsync(context, next);

        (captured.ShouldNotBeNull().Interaction, captured.ResourceType).ShouldBe((FhirInteraction.SearchSystem, expectedType));
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
        capturedContext.ShouldNotBeNull();
        capturedContext!.Interaction.ShouldBe(FhirInteraction.Read);
        capturedContext.ResourceType.ShouldBe("Patient");
        capturedContext.ResourceId.ShouldBe("123");
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
        capturedContext.ShouldNotBeNull();
        capturedContext!.Interaction.ShouldBe(FhirInteraction.Create);
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
        capturedContext.ShouldNotBeNull();
        capturedContext!.Interaction.ShouldBe(FhirInteraction.SearchType);
    }

    [Fact]
    public async Task GivenAResourceWhoseIdIsMetadata_WhenDeleting_ThenItIsAuthorizedAsADeleteNotAsCapabilities()
    {
        // Arrange
        var httpContext = CreateHttpContext("DELETE", "/tenant/1/Patient/metadata");
        httpContext.Request.RouteValues["resourceType"] = "Patient";
        httpContext.Request.RouteValues["id"] = "metadata";

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
        capturedContext.ShouldNotBeNull();
        (capturedContext!.Interaction, capturedContext.ResourceType).ShouldBe((FhirInteraction.Delete, "Patient"));
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
        capturedContext.ShouldNotBeNull();
        capturedContext!.UserId.ShouldBe("user-123");
        capturedContext.RequestContext.ShouldNotBeNull();
        capturedContext.RequestContext.TenantId.ShouldBe(1);
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
        capturedContext.ShouldNotBeNull();
        capturedContext!.Roles.ShouldContain("Admin");
        capturedContext.Roles.ShouldContain("Clinician");
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
