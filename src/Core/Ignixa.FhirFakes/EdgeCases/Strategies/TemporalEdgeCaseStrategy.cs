// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Base for Temporal-family strategies. Gates application by FHIR type: only <c>date</c>/<c>dateTime</c>
/// leaves are eligible, so a temporal mutation never lands on a non-date string (and a free-text
/// value that merely looks like a date is not mis-grabbed). Output remains a valid FHIR date/dateTime.
/// </summary>
public abstract class TemporalEdgeCaseStrategy : IEdgeCaseStrategy
{
    /// <inheritdoc />
    public abstract string Category { get; }

    /// <inheritdoc />
    public EdgeCaseFamily Family => EdgeCaseFamily.Temporal;

    /// <inheritdoc />
    public ValidityIntent Intent => ValidityIntent.PreservesValidity;

    /// <inheritdoc />
    public bool CanApply(MutationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.InstanceType is "date" or "dateTime";
    }

    /// <inheritdoc />
    public abstract MutationResult Apply(MutationTarget target, Randomizer rng);
}
