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
/// once per root within a single expression evaluation, rather than once per <c>resolve()</c> call
/// in that expression. This scope is narrower than it sounds: it does not span separate
/// evaluations. <c>FhirPathInvariantCheck.BuildEvaluationContext</c> - the dominant caller - returns
/// a fresh <see cref="EvaluationContext"/> (and so a fresh cache) per constraint, so validating one
/// resource against M constraints that each call <c>resolve()</c> builds the index M times, not
/// once.
/// </summary>
/// <remarks>
/// Keyed on root reference identity: a call with a different root than the one the cache holds
/// discards the stale index and rebuilds. <see cref="EvaluationContext.WithResource"/> /
/// <see cref="EvaluationContext.WithRootResource"/> would take exactly this discard-and-rebuild
/// path if used to switch to a different resource mid-evaluation (e.g. entering a contained
/// resource) - but neither method has any caller today, so this is defence-in-depth against a
/// future caller, not a live scenario.
/// The lock is likewise defence-in-depth, not a supported concurrency scenario: evaluating the same
/// <see cref="EvaluationContext"/> concurrently from multiple threads is already unsupported for an
/// unrelated reason - <see cref="EvaluationContext.DefinedVariables"/> is a plain dictionary mutated
/// in place by <c>defineVariable()</c>, which is why <c>FhirPathInvariantCheck.BuildEvaluationContext</c>
/// deliberately returns a fresh context per constraint: "sharing one instance would leak variables
/// between constraints and race across threads". The lock only guards the one-time build, so it
/// costs nothing to keep even though the concurrent-use scenario it guards against should never
/// occur.
/// Identity-keying means an <see cref="IElement"/> mutated in place after being indexed would leave
/// this cache serving a stale index for that same reference - safe only because a cache instance is
/// scoped to a single evaluation over what is expected to be a read-only instance, never reused
/// across a mutation of the same root.
/// </remarks>
internal sealed class ReferenceIndexCache
{
    private readonly Lock _lock = new();
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
