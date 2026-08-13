// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Mutable holder that lazily builds and caches the in-instance <see cref="ReferenceIndex"/> used
/// by <c>resolve()</c>. Carried as a single shared instance across every immutable
/// <c>with</c>-derived copy of an <see cref="EvaluationContext"/> (records copy property values,
/// not the referenced object), so the O(contained + bundle entries) index build happens at most
/// once per root per evaluation instead of once per <c>resolve()</c> call.
/// </summary>
/// <remarks>
/// Keyed on root reference identity: a call with a different root than the one the cache holds
/// discards the stale index and rebuilds, so <see cref="EvaluationContext.WithResource"/> /
/// <see cref="EvaluationContext.WithRootResource"/> switching to a different resource (e.g.
/// entering a contained resource) can never observe a stale index for the previous root.
/// Locked because a caller can legitimately evaluate the same <see cref="EvaluationContext"/>
/// concurrently from multiple threads (e.g. a cached compiled expression reused across requests);
/// the lock only guards the one-time build, so steady-state contention is negligible.
/// </remarks>
internal sealed class ReferenceIndexCache
{
    private readonly object _lock = new();
    private IElement? _root;
    private ReferenceIndex? _index;

    /// <summary>
    /// Returns the cached <see cref="ReferenceIndex"/> for <paramref name="root"/>, building and
    /// caching one if none exists yet or the cached index was built from a different root.
    /// </summary>
    /// <param name="root">The resource element to index, or null when no root is available.</param>
    /// <returns>The index for <paramref name="root"/>, or null when <paramref name="root"/> is null.</returns>
    public ReferenceIndex? GetOrBuild(IElement? root)
    {
        if (root is null)
        {
            return null;
        }

        lock (_lock)
        {
            if (_index is not null && ReferenceEquals(_root, root))
            {
                return _index;
            }

            var index = ReferenceIndex.Build(root);
            _root = root;
            _index = index;
            return index;
        }
    }
}
