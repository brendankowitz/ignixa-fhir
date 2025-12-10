// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Registrations;

namespace Ignixa.Api.Extensions;

/// <summary>
/// Provides extension methods for configuring the Ignixa middleware pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Configures the Ignixa FHIR server middleware pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseIgnixaApi(
        this IApplicationBuilder app,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Configure middleware pipeline
        app.UseIgnixaMiddleware(configuration, environment);

        // OpenAPI (development only)
        if (environment.IsDevelopment())
        {
            ((IEndpointRouteBuilder)app).MapOpenApi();
        }

        return app;
    }
}
