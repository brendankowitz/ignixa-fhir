// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Base for Temporal-family strategies. Gates application by value shape: only leaves whose current
/// value already matches the FHIR date/dateTime grammar are eligible, so a temporal mutation never
/// lands on a non-date string. Output is required to remain a valid FHIR date/dateTime.
/// </summary>
public abstract partial class TemporalEdgeCaseStrategy : IEdgeCaseStrategy
{
    [GeneratedRegex(@"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?)?)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex FhirDateRegex();

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
        return FhirDateRegex().IsMatch(target.Value);
    }

    /// <inheritdoc />
    public abstract MutationResult Apply(MutationTarget target, Randomizer rng);

    /// <summary>Returns true if the value matches the FHIR date/dateTime grammar.</summary>
    protected static bool IsFhirDate(string value) => FhirDateRegex().IsMatch(value);
}
