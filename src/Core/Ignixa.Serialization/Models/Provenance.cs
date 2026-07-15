// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Models;

public partial class Provenance
{
    /// <summary>
    /// Adds a versioned target reference (<c>resourceType/id/_history/versionId</c>). Used when the
    /// server auto-fills <c>target</c> after persisting the resource an X-Provenance header/template
    /// describes.
    /// </summary>
    public void AddTarget(string resourceType, string resourceId, string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        Target.Add(new Reference { Reference2 = $"{resourceType}/{resourceId}/_history/{versionId}" });
    }
}
