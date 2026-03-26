// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Execution;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Experimental.GraphQl.Pipeline;

public sealed class GraphQlSchemaWarmupService(
    IRequestExecutorResolver executorResolver,
    IOptions<ExperimentalOptions> options,
    ILogger<GraphQlSchemaWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var graphQlOptions = options.Value.Features.GraphQl;
        foreach (var version in graphQlOptions.WarmupVersions)
        {
            try
            {
                var schemaName = GraphQlNamingHelper.GetSchemaName(version);
                await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to warm up GraphQL schema for version {Version}", version);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
