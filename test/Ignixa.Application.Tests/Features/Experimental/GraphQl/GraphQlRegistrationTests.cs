// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Pipeline;
using Ignixa.Application.Features.Experimental.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

/// <summary>
/// GraphQL is opt-in because building a version's schema costs hundreds of megabytes of heap,
/// and the warmup service builds one at startup whenever GraphQL is registered.
/// </summary>
public class GraphQlRegistrationTests
{
    private static IServiceCollection AddExperimentalServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddExperimentalServices(configuration);
    }

    [Fact]
    public void GivenExperimentalEnabledWithoutGraphQlSetting_WhenAddingServices_ThenGraphQlIsNotRegistered()
    {
        var services = AddExperimentalServices(new() { ["Experimental:Enabled"] = "true" });

        services.ShouldNotContain(d => d.ImplementationType == typeof(GraphQlSchemaWarmupService));
        services.ShouldNotContain(d => d.ServiceType == typeof(IFhirTypeModule));
    }

    [Fact]
    public void GivenGraphQlEnabled_WhenAddingServices_ThenSchemaWarmupIsRegistered()
    {
        var services = AddExperimentalServices(new()
        {
            ["Experimental:Enabled"] = "true",
            ["Experimental:Features:GraphQl:Enabled"] = "true",
        });

        services.ShouldContain(d => d.ImplementationType == typeof(GraphQlSchemaWarmupService));
        services.ShouldContain(d => d.ServiceType == typeof(IFhirTypeModule));
    }
}
