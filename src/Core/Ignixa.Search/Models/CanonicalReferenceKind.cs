// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Models;

/// <summary>What a reference search parameter's expression selects besides references, from <c>KnownCanonicalReferenceParameters</c>.</summary>
internal enum CanonicalReferenceKind
{
    /// <summary>No canonical: only references, or a parameter the table does not list.</summary>
    None,

    /// <summary>Canonicals beside references (R5 <c>RequestOrchestration-participant</c>): its references can be followed, its canonicals cannot.</summary>
    Mixed,

    /// <summary>Only canonicals (R5 <c>_profile</c>): nothing it selects can be followed to a resource without resolving a canonical.</summary>
    Only,
}
