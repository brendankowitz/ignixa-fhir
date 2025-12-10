// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Middleware;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Provides extension methods for configuring middleware in the request pipeline.
/// </summary>
public static class MiddlewareRegistration
{
    /// <summary>
    /// Configures the Ignixa middleware pipeline for FHIR server operation.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseIgnixaMiddleware(
        this IApplicationBuilder app,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);

        // FHIR exception handler (converts exceptions to OperationOutcome)
        app.UseFhirExceptionHandler();

        // ForwardedHeaders for Docker/container deployments
        if (string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
        {
            app.UseForwardedHeaders();
        }

        // Authentication/Authorization middleware
        // Must be added BEFORE tenant resolution to populate HttpContext.User.Claims
        var authEnabled = configuration.GetValue<bool>("Authorization:Enabled", true);
        if (authEnabled)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        // Multi-tenancy middleware (extracts tenantId from route)
        app.UseMiddleware<TenantResolutionMiddleware>();

        // FHIR request context middleware (creates centralized context)
        app.UseMiddleware<FhirRequestContextMiddleware>();

        // Development validation for middleware ordering
        if (environment.IsDevelopment())
        {
            app.UseMiddlewareOrderingValidation();
        }

        return app;
    }

    /// <summary>
    /// Adds development-only middleware to validate middleware ordering.
    /// </summary>
    private static IApplicationBuilder UseMiddlewareOrderingValidation(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<TenantResolutionMiddleware>>();
            var fhirContextAccessor = context.RequestServices
                .GetRequiredService<Ignixa.Application.Infrastructure.IFhirRequestContextAccessor>();

            if (context.GetEndpoint() != null &&
                !context.Items.ContainsKey("TenantId") &&
                fhirContextAccessor.RequestContext?.TenantId == 0)
            {
                // Safe: Using structured logging with placeholders prevents log injection.
                // The Path value is passed as a parameter, not concatenated into the message.
                logger.LogWarning(
                    "TenantResolutionMiddleware may not have run before FhirRequestContextMiddleware. " +
                    "Route: {Path}, TenantId in context: {TenantId}",
                    context.Request.Path.ToString(),
                    fhirContextAccessor.RequestContext?.TenantId);
            }

            await next();
        });
    }

    /// <summary>
    /// Configures OpenAPI endpoints for development.
    /// </summary>
    public static IApplicationBuilder UseIgnixaDevelopmentFeatures(
        this IApplicationBuilder app,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            // Map OpenAPI endpoint
            ((IEndpointRouteBuilder)app).MapOpenApi();
        }

        return app;
    }
}
