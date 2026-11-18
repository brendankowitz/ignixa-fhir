// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// A capacity-bounded memo keyed by FhirPath expression text, for values that are a pure function of
/// that text (parsed ASTs, compiled delegates).
/// </summary>
/// <typeparam name="TValue">The memoized value. May be a nullable reference type; a stored <c>null</c> is a real cached answer, not a miss.</typeparam>
/// <remarks>
/// <para>
/// Expression text is influenced by user input - a tenant may register custom SearchParameters, and
/// every distinct expression it carries becomes a distinct key - so an unbounded cache is a slow leak
/// that a caller can drive.
/// </para>
/// <para>
/// Eviction is generational rather than the clear-at-capacity used by
/// <c>NegativeLookupCache</c>. That cache can afford the cliff because a lost entry costs one database
/// round trip and its TTL sheds most entries before capacity is ever reached. Here there is no TTL -
/// a parsed expression never goes stale - so capacity is the only bound, and a working set slightly
/// above it would re-parse every expression on every write, on the path that extracts search indexes.
/// Retaining the previous generation removes that cliff: a key evicted from the hot generation is
/// still served from the cold one and promoted on the way past, so steady-state working sets up to
/// the capacity survive indefinitely and memory stays bounded at twice it.
/// </para>
/// <para>
/// Every entry is a pure optimization, which is what makes the approximation safe: dropping one costs
/// a re-parse, never a different answer. For the same reason <paramref name="valueFactory"/> may run
/// more than once for a key under concurrency, exactly as <see cref="ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
/// already allows.
/// </para>
/// </remarks>
internal sealed class BoundedExpressionCache<TValue>
{
    private readonly int _capacity;
    private readonly object _rotationLock = new();

    private ConcurrentDictionary<string, TValue> _hot = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, TValue> _cold = new(StringComparer.Ordinal);

    public BoundedExpressionCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Number of entries in the hot generation. Intended for diagnostics and tests.</summary>
    public int Count => _hot.Count;

    /// <summary>Number of entries in the cold (previous) generation. Intended for diagnostics and tests.</summary>
    internal int ColdCount => _cold.Count;

    public TValue GetOrAdd(string expression, Func<string, TValue> valueFactory)
    {
        if (_hot.TryGetValue(expression, out var hit))
        {
            return hit;
        }

        if (_cold.TryGetValue(expression, out var demoted))
        {
            return Store(expression, demoted);
        }

        return Store(expression, valueFactory(expression));
    }

    /// <summary>
    /// Looks up an entry without supplying a factory, promoting a cold hit exactly as
    /// <see cref="GetOrAdd"/> would.
    /// </summary>
    /// <remarks>
    /// This exists so a caller that already holds the value's dependencies does not have to allocate a
    /// closure to describe how to compute something the cache almost always already has: a lambda
    /// capturing a local costs a display class and a delegate on every call, cache hit included. Callers
    /// that would fall through to a compute step should still prefer <see cref="GetOrAdd"/>.
    /// </remarks>
    public bool TryGetValue(string expression, out TValue value)
    {
        if (_hot.TryGetValue(expression, out var hit))
        {
            value = hit;
            return true;
        }

        if (_cold.TryGetValue(expression, out var demoted))
        {
            value = Store(expression, demoted);
            return true;
        }

        value = default!;
        return false;
    }

    public void Clear()
    {
        lock (_rotationLock)
        {
            _hot = new ConcurrentDictionary<string, TValue>(StringComparer.Ordinal);
            _cold = new ConcurrentDictionary<string, TValue>(StringComparer.Ordinal);
        }
    }

    private TValue Store(string expression, TValue value)
    {
        if (_hot.Count >= _capacity)
        {
            Rotate();
        }

        // Losing a race here costs a re-parse on a later call, never a wrong value: both racers
        // computed the same pure function of the same key.
        _hot[expression] = value;
        return value;
    }

    private void Rotate()
    {
        lock (_rotationLock)
        {
            if (_hot.Count < _capacity)
            {
                return;
            }

            _cold = _hot;
            _hot = new ConcurrentDictionary<string, TValue>(StringComparer.Ordinal);
        }
    }
}
