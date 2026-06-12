// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Events;

public sealed class PackageLoadedSchemaInvalidationHandler(
    IReadOnlyList<IFhirTypeModule> typeModules,
    ILogger<PackageLoadedSchemaInvalidationHandler> logger) : INotificationHandler<PackageLoadedEvent>
{
    public Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var module in typeModules)
        {
            try
            {
                module.NotifyTypesChanged();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to invalidate GraphQL schema for module {ModuleType}", module.GetType().Name);
            }
        }

        return Task.CompletedTask;
    }
}
