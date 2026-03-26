// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Medino;

namespace Ignixa.Application.Features.Experimental.GraphQl.Events;

public sealed class PackageLoadedSchemaInvalidationHandler(
    IReadOnlyList<IFhirTypeModule> typeModules) : INotificationHandler<PackageLoadedEvent>
{
    public Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var module in typeModules)
            module.NotifyTypesChanged();
        return Task.CompletedTask;
    }
}
