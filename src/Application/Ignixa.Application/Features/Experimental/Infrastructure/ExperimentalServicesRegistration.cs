// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.AspNetCore;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Directives;
using Ignixa.Application.Features.Experimental.GraphQl.Pipeline;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        // MCP server registration is handled in Ignixa.Api/Registrations/BackgroundServicesRegistration.cs

        // Feature: Transform - No additional IServiceCollection registrations needed
        // Handler registrations are done via Autofac

        // Feature: Terminology - No additional IServiceCollection registrations needed
        // Handler registrations are done via Autofac

        // Feature: GraphQL - $graphql operation
        if (options.Features.GraphQl.Enabled)
        {
            services.AddGraphQlServices(options.Features.GraphQl);
        }

        return services;
    }

    private static IServiceCollection AddGraphQlServices(
        this IServiceCollection services,
        GraphQlExperimentalOptions graphQlOptions)
    {
        services.AddSingleton<FhirTypeModuleCatalog>();

        foreach (var version in GraphQlNamingHelper.SupportedVersions)
        {
            var schemaName = GraphQlNamingHelper.GetSchemaName(version);
            var capturedVersion = version;

            var maxDepth = graphQlOptions.MaxQueryDepth;
            var timeout = TimeSpan.FromSeconds(graphQlOptions.ExecutionTimeoutSeconds);
            var enableIntrospection = graphQlOptions.EnableIntrospection;

            services.AddGraphQLServer(schemaName)
                .AddTypeModule(sp => sp.GetRequiredService<FhirTypeModuleCatalog>().GetOrCreate(capturedVersion))
                .AddMaxExecutionDepthRule(maxDepth)
                .DisableIntrospection(!enableIntrospection)
                .ModifyRequestOptions(o => o.ExecutionTimeout = timeout)
                .AddHttpRequestInterceptor<FhirHttpRequestInterceptor>()
                .AddErrorFilter<FhirGraphQlErrorFilter>()
                .AddDirectiveType<FhirFlattenDirectiveType>()
                .AddDirectiveType<FhirFirstDirectiveType>()
                .AddDirectiveType<FhirSingletonDirectiveType>()
                .AddDirectiveType<FhirSliceDirectiveType>();
        }

        services.AddHostedService<GraphQlSchemaWarmupService>();

        return services;
    }
}
