// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Models;
using Ignixa.Serialization.SourceNodes;

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
public sealed class ResourceElementOrdering(SerializationOptions options)
{
    /// <summary>
    /// Orders <paramref name="resource"/> for a write that storage will complete. Only call this on
    /// paths that reach repository storage: it writes provisional <c>meta.versionId</c> and
    /// <c>meta.lastUpdated</c> values that storage replaces with the real ones.
    /// </summary>
    public void OrderForStorage(ResourceJsonNode resource, IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(schema);

        if (!options.OrderElementsOnWrite)
        {
            return;
        }

        // Storage stamps meta.versionId and meta.lastUpdated after ordering, and $import resources may lack
        // them. Assigning an existing property keeps its position, so the slots are created now to be
        // ordered with the rest rather than appended by storage.
        // Plain assignment rather than ??= avoids reading client values, which may not be strings.
        Meta meta = resource.Meta;
        meta.VersionId = "1";
        meta.LastUpdatedOffset = DateTimeOffset.UtcNow;

        resource.OrderElements(schema);
    }
}
