// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.Configuration;

namespace Ignixa.Application.Features.Experimental.Infrastructure;

/// <summary>
/// Extension methods for registering experimental services with IServiceCollection.
/// </summary>
public static class ExperimentalServicesRegistration
{
    /// <summary>
    /// Adds experimental services to the service collection.
    /// Respects the master switch and per-feature configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExperimentalServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ExperimentalOptions.SectionName)
            .Get<ExperimentalOptions>() ?? new ExperimentalOptions();

        // Master switch check - if disabled, return early
        if (!options.Enabled)
        {
            return services;
        }

        // Register options for DI
        services.Configure<ExperimentalOptions>(
            configuration.GetSection(ExperimentalOptions.SectionName));

        // Feature: MCP - Model Context Protocol server
        if (options.Features.Mcp.Enabled)
        {
            services.AddExperimentalMcpServices(configuration);
        }

        // Feature: Transform - No additional IServiceCollection registrations needed
        // Handler registrations are done via Autofac

        // Feature: Terminology - No additional IServiceCollection registrations needed
        // Handler registrations are done via Autofac

        return services;
    }

    private static IServiceCollection AddExperimentalMcpServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MCP server is already configured in the main application
        // This is a placeholder for future MCP-specific service registrations
        // when MCP is fully migrated to the experimental library

        return services;
    }
}
