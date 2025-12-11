using System.Security.Claims;
using Ignixa.Api.OpenIddict.Services;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Ignixa.Api.OpenIddict.Endpoints;

/// <summary>
/// OAuth 2.0 token endpoints for OpenIddict server.
/// </summary>
public static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/connect/token", HandleTokenRequest)
            .WithName("OpenIddict_Token")
            .WithTags("OAuth")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> HandleTokenRequest(
        HttpContext httpContext,
        [FromServices] DevelopmentUserService userService,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Features.Get<OpenIddictServerAspNetCoreFeature>()?.Transaction?.Request
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsGrantAsync(httpContext, request, cancellationToken);
        }

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordGrantAsync(httpContext, request, userService);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenGrantAsync(httpContext, request);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    private static async Task<IResult> HandleClientCredentialsGrantAsync(
        HttpContext httpContext,
        OpenIddictRequest request,
        CancellationToken cancellationToken)
    {
        var applicationManager = httpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applicationManager.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException($"Application '{request.ClientId}' not found.");

        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        // Add client_id as subject
        identity.AddClaim(
            OpenIddictConstants.Claims.Subject,
            await applicationManager.GetClientIdAsync(application, cancellationToken)
                ?? throw new InvalidOperationException("The client ID cannot be retrieved."));

        // Add display name
        var displayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken);
        if (!string.IsNullOrEmpty(displayName))
        {
            identity.AddClaim(OpenIddictConstants.Claims.Name, displayName);
        }

        // Add scopes
        identity.SetScopes(request.GetScopes());
        identity.SetResources("fhir-api");
        identity.SetDestinations(GetDestinations);

        var principal = new ClaimsPrincipal(identity);

        return Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static Task<IResult> HandlePasswordGrantAsync(
        HttpContext httpContext,
        OpenIddictRequest request,
        DevelopmentUserService userService)
    {
        var username = request.Username ?? throw new InvalidOperationException("Username is required.");
        var password = request.Password ?? throw new InvalidOperationException("Password is required.");

        if (!userService.ValidateCredentials(username, password))
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username or password is invalid."
            });

            return Task.FromResult(Results.Forbid(properties, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]));
        }

        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(OpenIddictConstants.Claims.Subject, username);
        identity.AddClaim(OpenIddictConstants.Claims.Name, username);

        // Add user-specific claims
        var userClaims = userService.GetUserClaims(username);
        foreach (var claim in userClaims)
        {
            identity.AddClaim(claim);
        }

        identity.SetScopes(request.GetScopes());
        identity.SetResources("fhir-api");
        identity.SetDestinations(GetDestinations);

        var principal = new ClaimsPrincipal(identity);

        // Allow offline_access for refresh tokens
        if (request.GetScopes().Contains(OpenIddictConstants.Scopes.OfflineAccess))
        {
            principal.SetRefreshTokenLifetime(TimeSpan.FromDays(7));
        }

        return Task.FromResult(Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }

    private static async Task<IResult> HandleRefreshTokenGrantAsync(
        HttpContext httpContext,
        OpenIddictRequest request)
    {
        var result = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        // Retrieve the claims principal stored in the refresh token
        var principal = result.Principal
            ?? throw new InvalidOperationException("The refresh token is no longer valid.");

        // Create new identity to ensure fresh claims
        var identity = new ClaimsIdentity(principal.Identity);

        identity.SetScopes(request.GetScopes());
        identity.SetResources("fhir-api");
        identity.SetDestinations(GetDestinations);

        var newPrincipal = new ClaimsPrincipal(identity);

        return Results.SignIn(newPrincipal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Always include in access token
        yield return OpenIddictConstants.Destinations.AccessToken;

        // Include certain claims in identity token
        if (claim.Type is OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.Subject or "fhirUser" or "patient")
        {
            yield return OpenIddictConstants.Destinations.IdentityToken;
        }
    }
}
