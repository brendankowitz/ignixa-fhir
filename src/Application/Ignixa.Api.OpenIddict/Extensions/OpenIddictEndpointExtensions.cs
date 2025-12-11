using Ignixa.Api.OpenIddict.Configuration;
using Ignixa.Api.OpenIddict.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.OpenIddict.Extensions;

/// <summary>
/// Extension methods for mapping OpenIddict endpoints.
/// </summary>
public static class OpenIddictEndpointExtensions
{
    /// <summary>
    /// Maps all OpenIddict OAuth 2.0 endpoints if OpenIddict is enabled.
    /// </summary>
    public static IEndpointRouteBuilder MapIgnixaOpenIddictEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Only map endpoints if OpenIddict is enabled
        var options = endpoints.ServiceProvider.GetService<IOptions<OpenIddictServerOptions>>();
        if (options?.Value?.Enabled is not true)
        {
            return endpoints;
        }

        endpoints.MapTokenEndpoints();
        endpoints.MapAuthorizationEndpoints();

        return endpoints;
    }
}
