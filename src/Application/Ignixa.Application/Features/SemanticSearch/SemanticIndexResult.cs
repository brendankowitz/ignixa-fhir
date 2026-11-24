// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// One resource's outcome from <see cref="SemanticIndexer.IndexIndependentlyAsync"/>: either the
/// resource with <see cref="ResourceWrapper.VectorIndices"/> populated, or the exception that resource
/// alone failed with. Exactly one of <see cref="Resource"/>/<see cref="Error"/> is set -- the private
/// constructor and the two factory methods are the only way to build one, so there is no way to observe
/// a result with both or neither populated.
/// </summary>
public readonly record struct SemanticIndexResult
{
    private SemanticIndexResult(ResourceWrapper? resource, Exception? error)
    {
        Resource = resource;
        Error = error;
    }

    /// <summary>The indexed resource, or <see langword="null"/> when <see cref="Error"/> is set.</summary>
    public ResourceWrapper? Resource { get; }

    /// <summary>The failure specific to this resource, or <see langword="null"/> when <see cref="Resource"/> is set.</summary>
    public Exception? Error { get; }

    public static SemanticIndexResult Succeeded(ResourceWrapper resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new SemanticIndexResult(resource, null);
    }

    public static SemanticIndexResult Failed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new SemanticIndexResult(null, error);
    }
}
