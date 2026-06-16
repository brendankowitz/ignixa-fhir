// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Base for StringBoundary-family strategies. Inherits free-text element gating from
/// <see cref="FreeTextEdgeCaseStrategy"/> so string mutations never land on bound codes,
/// system URLs, references, or ids. Each concrete strategy declares its own
/// <see cref="IEdgeCaseStrategy.Intent"/> because this family intentionally spans validity intents.
/// </summary>
public abstract class StringBoundaryEdgeCaseStrategy : FreeTextEdgeCaseStrategy
{
    /// <inheritdoc />
    public override EdgeCaseFamily Family => EdgeCaseFamily.StringBoundary;

    /// <inheritdoc />
    public abstract override ValidityIntent Intent { get; }

    /// <inheritdoc />
    public abstract override MutationResult Apply(MutationTarget target, Randomizer rng);
}
