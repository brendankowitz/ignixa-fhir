// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Puts a resource into StructureDefinition element order before it is stored, when
/// <see cref="SerializationOptions.OrderElementsOnWrite"/> is enabled; otherwise does nothing.
/// </summary>
/// <remarks>
/// Applied where resources are prepared for storage rather than by wrapping the repository: callers
/// detect storage capabilities (atomic transactions, versioned reads) by type-testing the repository,
/// which a wrapper would hide.
/// </remarks>
public sealed class ResourceElementOrdering(IOptions<SerializationOptions> options)
{
    public void OrderForStorage(ResourceJsonNode resource, IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(schema);

        if (!options.Value.OrderElementsOnWrite)
        {
            return;
        }

        // Storage stamps meta.versionId and meta.lastUpdated after this point. Assigning an existing
        // property keeps its position, so create both slots now to have them ordered with the rest;
        // otherwise storage would append them (and a missing meta) to the end. Storage overwrites
        // these values with the real version and timestamp.
        Meta meta = resource.Meta;
        meta.VersionId ??= "1";
        meta.LastUpdatedOffset ??= DateTimeOffset.UtcNow;

        resource.OrderElements(schema);
    }
}
