// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Sets a free-text value to the empty string <c>""</c>. FHIR <c>string</c> requires at least one
/// character, so an empty-but-present primitive is unconditionally rejected by the validator
/// (depth-independent). This mutation therefore always produces an invalid resource.
/// </summary>
public sealed class EmptyPresentStringStrategy : StringBoundaryEdgeCaseStrategy
{
    /// <inheritdoc />
    public override string Category => "string.empty-present";

    /// <inheritdoc />
    public override ValidityIntent Intent => ValidityIntent.AlwaysInvalid;

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        return new MutationResult(string.Empty, "Set free-text value to empty string");
    }
}
