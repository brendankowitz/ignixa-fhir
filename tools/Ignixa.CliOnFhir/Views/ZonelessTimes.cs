// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Counts the dateTime and instant values of resources that have a time but no time zone (not valid FHIR, but common: Synthea
/// writes <c>2026-01-01T00:00:00.0000000</c>). FHIRPath does not compare such a value with one that has a zone, as
/// <c>%asOf</c> always does: it answers empty, so a <c>where</c> on it matches nothing and a column is null, unseen. Not thread-safe.
/// </summary>
internal sealed partial class ZonelessTimes
{
    /// <summary>Values found.</summary>
    public int Values { get; private set; }

    /// <summary>Resources with at least one.</summary>
    public int Resources { get; private set; }

    /// <summary>Counts <paramref name="resource"/>'s values, walking its elements without recursion.</summary>
    public void Count(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        int found = 0;
        var pending = new Stack<IElement>();
        pending.Push(resource);
        while (pending.TryPop(out IElement? element))
        {
            if (element.HasPrimitiveValue && IsZoneless(element))
            {
                found++;
            }

            foreach (IElement child in element.Children())
            {
                pending.Push(child);
            }
        }

        Values += found;
        Resources += found > 0 ? 1 : 0;
    }

    private static bool IsZoneless(IElement element) => element.Value switch
    {
        FhirTemporal { Kind: FhirPrimitive.DateTime or FhirPrimitive.Instant, HasTimezone: false, Precision: > FhirTemporalPrecision.Day } => true,
        string text when element.InstanceType is "dateTime" or "instant" => TimeWithoutZone().IsMatch(text),
        _ => false,
    };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeWithoutZone();
}
