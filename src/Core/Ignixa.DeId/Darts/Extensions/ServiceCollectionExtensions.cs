// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DeId.Darts.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ignixa.DeId.Darts.Extensions;

/// <summary>
/// Dependency injection extensions for registering DARTS de-identification services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DARTS de-identification services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDartsDeId(this IServiceCollection services)
    {
        services.TryAddSingleton<LibraryConfigurationLoader>();
        return services;
    }
}
