// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Tests.Definition;

/// <summary>The outcome of <see cref="CompositeComponentConsistency.Check"/>.</summary>
internal enum CompositeConsistencyVerdict
{
    /// <summary>Every component selects elements that index as its declared kind.</summary>
    Consistent,

    /// <summary>A component's elements never index as its declared kind, so no query can match.</summary>
    Inconsistent,

    /// <summary>A component's definition URL did not resolve, so the composite is not indexed at all.</summary>
    Unresolved,

    /// <summary>The analyzer could not type a component's expression; not evidence either way.</summary>
    Unknown,
}
