// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A named, single-concern edge-case mutation. Strategies are the extensibility surface of the
/// edge-case subsystem: built-ins ship by default and consumers can register their own.
/// </summary>
public interface IEdgeCaseStrategy
{
    /// <summary>The hierarchical category name (e.g. "unicode.rtl", "temporal.leap-year").</summary>
    string Category { get; }

    /// <summary>The family this strategy belongs to (used for coarse selection).</summary>
    EdgeCaseFamily Family { get; }

    /// <summary>The strategy's claim about whether its mutation preserves spec validity.</summary>
    ValidityIntent Intent { get; }

    /// <summary>Returns true if this strategy can safely apply to the given target.</summary>
    bool CanApply(MutationTarget target);

    /// <summary>Applies the mutation to the target, using <paramref name="rng"/> for any internal choice.</summary>
    /// <returns>The new value written and a short description of the change.</returns>
    MutationResult Apply(MutationTarget target, Randomizer rng);
}
