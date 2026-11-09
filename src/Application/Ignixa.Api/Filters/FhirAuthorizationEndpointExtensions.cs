using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Filters;

public static class FhirAuthorizationEndpointExtensions
{
    /// <summary>Runs <see cref="FhirAuthorizationFilter"/> on the endpoints and marks them as authorized.</summary>
    public static TBuilder RequireFhirAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder
            .AddEndpointFilter<TBuilder, FhirAuthorizationFilter>()
            .WithMetadata(FhirAuthorizationRequiredMetadata.Instance);

    /// <summary>
    /// Requires the <c>SystemAdmin</c> role while more than one tenant is active, for operations whose effect spans
    /// every tenant (conformance definitions are shared by all tenants). Must follow
    /// <see cref="RequireFhirAuthorization{TBuilder}"/>; skipped while authorization is disabled.
    /// </summary>
    public static TBuilder RequireServerWideAuthority<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var services = httpContext.RequestServices;
            if (!services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.Enabled ||
                !await HasMultipleActiveTenantsAsync(services, httpContext.RequestAborted))
            {
                return await next(context);
            }

            var requestContext = services.GetRequiredService<IFhirRequestContextAccessor>().RequestContext
                ?? throw new InvalidOperationException(
                    "FhirRequestContext not set. Ensure TenantResolutionMiddleware runs before authorization.");
            var caller = FhirAuthorizationFilter.CreateAuthorizationContext(
                httpContext,
                requestContext,
                default,
                resourceType: null,
                resourceId: null);
            return caller.IsInRole(TenantIsolationHandler.SystemAdminRole)
                ? await next(context)
                : FhirAuthorizationFilter.CreateForbiddenResponse(
                    $"This operation affects every tenant and requires the {TenantIsolationHandler.SystemAdminRole} role while more than one tenant is active.");
        });

    private static async Task<bool> HasMultipleActiveTenantsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken) =>
        (await services.GetRequiredService<ITenantConfigurationStore>().GetAllTenantsAsync(cancellationToken))
            .Count(tenant => tenant.IsActive && tenant.TenantId != SystemConstants.SystemPartitionId) > 1;
}
