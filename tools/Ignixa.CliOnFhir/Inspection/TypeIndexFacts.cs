// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Size, origin and cost of one resource type's index.</summary>
/// <param name="ResourceType">The indexed resource type.</param>
/// <param name="ResourceCount">Resources indexed.</param>
/// <param name="IndexEntries">Search index entries across those resources.</param>
/// <param name="Source"><see cref="CacheSource"/> when loaded from the persisted cache, <see cref="BuiltSource"/> when built in this process.</param>
/// <param name="ElapsedMilliseconds">Wall time to build the index, or to load and rebind it from the cache.</param>
/// <param name="CacheWriteMilliseconds">Wall time to persist a built index; null when it came from the cache or was not persisted.</param>
public sealed record TypeIndexFacts(
    string ResourceType,
    int ResourceCount,
    long IndexEntries,
    string Source,
    long ElapsedMilliseconds,
    long? CacheWriteMilliseconds)
{
    public const string CacheSource = "cache";
    public const string BuiltSource = "built";
}
