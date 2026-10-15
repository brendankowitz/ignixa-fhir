// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.InMemory;

/// <summary>
/// Search features beyond filtering a single resource type, each opted into by the caller because it reads
/// other resource types through an <see cref="IInMemorySearchSource"/>. A feature that is not enabled fails with
/// <see cref="Indexing.SearchOperationNotSupportedException"/> rather than being skipped, since skipping would
/// return results that look filtered, ordered or expanded when they are not.
/// </summary>
[Flags]
public enum InMemorySearchFeatures
{
    /// <summary>Filtering only.</summary>
    None = 0,

    /// <summary>Forward chains such as <c>subject:Patient.name=Smith</c>, to any depth.</summary>
    Chains = 1,

    /// <summary>Reverse chains: <c>_has:Observation:subject:code=...</c>, to any depth.</summary>
    ReverseChains = 2,

    /// <summary><c>_sort</c>, on one or more parameters.</summary>
    Sort = 4,

    /// <summary><c>_include</c>, including <c>:iterate</c>.</summary>
    Include = 8,

    /// <summary><c>_revinclude</c>, including <c>:iterate</c>.</summary>
    RevInclude = 16,

    /// <summary>Every feature.</summary>
    All = Chains | ReverseChains | Sort | Include | RevInclude,
}
