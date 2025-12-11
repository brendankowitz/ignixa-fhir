using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Ignixa.Api.OpenIddict.Endpoints;

/// <summary>
/// OAuth 2.0 authorization endpoints for interactive flows.
/// </summary>
public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/connect/authorize", HandleAuthorizeRequest)
            .WithName("OpenIddict_Authorize")
            .WithTags("OAuth")
            .AllowAnonymous();

        endpoints.MapPost("/connect/authorize", HandleAuthorizeRequest)
            .WithName("OpenIddict_Authorize_Post")
            .WithTags("OAuth")
            .AllowAnonymous();

        return endpoints;
    }

    private static Task<IResult> HandleAuthorizeRequest(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Features.Get<OpenIddictServerAspNetCoreFeature>()?.Transaction?.Request
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // For development: auto-approve all authorization requests
        // In production, this would redirect to a login/consent page
        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        // Use a test user for development
        var subject = "dev-user";
        identity.AddClaim(OpenIddictConstants.Claims.Subject, subject);
        identity.AddClaim(OpenIddictConstants.Claims.Name, subject);

        identity.SetScopes(request.GetScopes());
        identity.SetResources("fhir-api");
        identity.SetDestinations(GetDestinations);

        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
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
