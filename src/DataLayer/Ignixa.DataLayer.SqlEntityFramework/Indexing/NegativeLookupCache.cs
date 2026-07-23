// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Ignixa.DataLayer.SqlEntityFramework.Indexing;

/// <summary>
/// Remembers reference-data keys a read-only lookup has already proven absent, so repeated searches for
/// unindexed terminology answer from memory instead of taking the shared ingest lock and issuing a
/// database round trip for every occurrence.
/// </summary>
/// <remarks>
/// <para>
/// Kept deliberately separate from the positive ID caches: those are shared with the get-or-create write
/// path, which treats every cached integer as a real surrogate key, so a "missing" sentinel cannot live
/// there without corrupting writes.
/// </para>
/// <para>
/// Bounded and time-limited by design. The TTL bounds staleness from rows created by another process or
/// server instance, which no in-process invalidation can observe; the capacity bounds memory against a
/// hostile or merely careless caller enumerating distinct systems (<c>?identifier=urn:x:{n}|a</c> in a
/// loop). Both limits are safe to breach in the losing direction because every entry is a pure
/// optimization: forgetting one costs a single database round trip, never a wrong answer.
/// </para>
/// <para>
/// In-process creation must call <see cref="Forget"/> so a search cannot keep reporting "missing" for
/// terminology the write path has since created. Two writers create <c>dbo.System</c> rows --
/// <c>SearchIndexReferenceDataCache.GetOrCreateSystemIdAsync</c> and
/// <c>SqlSystemRepository.GetOrCreateAsync</c> (CodeSystem import) -- and both invalidate. This is a
/// discipline enforced by review, not by the type: a third writer, or a <c>SqlSystemRepository</c>
/// constructed without a reference-data cache to notify, would leave the entry standing until its TTL.
/// That is the failure mode the TTL exists to bound, not one it exists to excuse.
/// </para>
/// </remarks>
public sealed class NegativeLookupCache
{
    /// <summary>Roughly a megabyte of URIs; far above any realistic terminology working set, far below memory pressure.</summary>
    private const int DefaultCapacity = 10_000;

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, long> _expiryTicks = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly int _capacity;

    public NegativeLookupCache(TimeProvider? timeProvider = null, TimeSpan? lifetime = null, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? DefaultLifetime;
        _capacity = capacity;
    }

    /// <summary>Number of unexpired entries currently retained. Intended for diagnostics and tests.</summary>
    public int Count => _expiryTicks.Count;

    /// <summary>
    /// Returns true when <paramref name="key"/> was recorded absent and that record has not yet expired.
    /// A false result means "ask the database", never "the key exists".
    /// </summary>
    public bool IsKnownMissing(string key)
    {
        if (!_expiryTicks.TryGetValue(key, out var expiry))
        {
            return false;
        }

        if (expiry > _timeProvider.GetUtcNow().UtcTicks)
        {
            return true;
        }

        _expiryTicks.TryRemove(key, out _);
        return false;
    }

    /// <summary>Records that <paramref name="key"/> has no row, evicting older entries when at capacity.</summary>
    public void RecordMiss(string key)
    {
        if (_expiryTicks.Count >= _capacity)
        {
            Evict();
        }

        _expiryTicks[key] = _timeProvider.GetUtcNow().Add(_lifetime).UtcTicks;
    }

    /// <summary>
    /// Drops any "missing" record for <paramref name="key"/>. Called by the write path the moment a row is
    /// created or found, so a concurrent search cannot keep answering from a record the write just falsified.
    /// </summary>
    public void Forget(string key) => _expiryTicks.TryRemove(key, out _);

    private void Evict()
    {
        var now = _timeProvider.GetUtcNow().UtcTicks;
        foreach (var entry in _expiryTicks)
        {
            if (entry.Value <= now)
            {
                _expiryTicks.TryRemove(entry.Key, out _);
            }
        }

        if (_expiryTicks.Count >= _capacity)
        {
            _expiryTicks.Clear();
        }
    }
}
