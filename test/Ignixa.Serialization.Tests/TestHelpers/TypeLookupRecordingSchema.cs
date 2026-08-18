// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Tests.TestHelpers;

/// <summary>
/// An <see cref="ISchema"/> decorator that counts <see cref="ISchema.GetTypeDefinition"/> calls per
/// type name. Lets a test observe whether a memo actually holds, through the public schema seam
/// rather than by reaching into the element's private fields.
/// Counting is thread-safe so the same instance can back a concurrency test.
/// </summary>
public sealed class TypeLookupRecordingSchema(ISchema inner) : ISchema
{
    private readonly ISchema _inner = inner;
    private readonly ConcurrentDictionary<string, int> _lookups = new(StringComparer.Ordinal);

    public FhirVersion Version => _inner.Version;

    /// <summary>
    /// Gets the number of times a definition was requested for <paramref name="typeName"/>.
    /// </summary>
    public int LookupCount(string typeName) => _lookups.TryGetValue(typeName, out var count) ? count : 0;

    /// <summary>
    /// Gets the total number of definition requests across all type names.
    /// </summary>
    public int TotalLookupCount => _lookups.Values.Sum();

    /// <summary>
    /// Discards all recorded counts, so a test can measure one navigation pass at a time.
    /// </summary>
    public void ResetCounts() => _lookups.Clear();

    public IType GetTypeDefinition(string typeName)
    {
        _lookups.AddOrUpdate(typeName, 1, static (_, count) => count + 1);
        return _inner.GetTypeDefinition(typeName);
    }

    public bool IsKnownType(string typeName) => _inner.IsKnownType(typeName);
}
