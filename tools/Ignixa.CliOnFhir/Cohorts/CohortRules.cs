// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Patients;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// The rules that decide whether one resource counts for a cohort criterion at <c>asOf</c>, read with FHIRPath by R4 element
/// paths. The searches that find the candidates already matched the codes; these rules add what a search cannot say as of a
/// point in time (statuses, onset, abatement, dates, values). The help topic <c>cohorts</c> states each rule; keep them equal.
/// </summary>
internal static class CohortRules
{
    private const string EnteredInError = "entered-in-error";

    private static readonly string[] InactiveClinicalStatuses = ["inactive", "resolved", "remission"];
    private static readonly string[] UncountableOrderStatuses = [EnteredInError, "cancelled", "draft"];
    private static readonly string[] ResultStatuses = ["final", "amended", "corrected"];
    private static readonly string[] ObservationInstant = ["effective.ofType(dateTime)", "effective.ofType(Period).start", "effective.ofType(instant)", "issued"];

    /// <summary>
    /// A Condition counts when it is not entered in error, its verification status is confirmed or absent (unless any is
    /// asked), its onset is at or before asOf or not recorded as a date and, for active, it is active at asOf: clinical status
    /// not inactive, resolved or remission, and no abatement at or before asOf (abatementBoolean true, abatementDateTime or
    /// abatementPeriod.end at or before asOf, or an abatement given only as an age, range or text).
    /// </summary>
    public static bool Condition(IElement condition, ConditionCriterion criterion, DateTimeOffset asOf)
    {
        List<string> verification = [.. ClinicalValues.Strings(condition, "verificationStatus.coding.code")];
        if (verification.Contains(EnteredInError, StringComparer.Ordinal)
            || (criterion.ConfirmedOnly && verification.Count > 0 && !verification.Contains("confirmed", StringComparer.Ordinal)))
        {
            return false;
        }

        if (Instant(condition, "onset.ofType(dateTime)", "onset.ofType(Period).start") > asOf)
        {
            return false;
        }

        return !criterion.ActiveOnly || ActiveAt(condition, asOf);
    }

    /// <summary>
    /// A MedicationRequest counts when its status is active (or, for any, not entered-in-error, cancelled or draft), it is
    /// not an order not to give the drug (doNotPerform), and it was authored at or before asOf or has no authoredOn.
    /// </summary>
    public static bool MedicationRequest(IElement order, MedicationCriterion criterion, DateTimeOffset asOf)
    {
        string? status = ClinicalValues.Literal(order, "status");
        bool statusCounts = criterion.ActiveOnly ? status == "active" : status is not null && !UncountableOrderStatuses.Contains(status, StringComparer.Ordinal);
        return statusCounts
            && ClinicalValues.Literal(order, "doNotPerform") != "true"
            && !(Instant(order, "authoredOn") > asOf);
    }

    /// <summary>
    /// An Immunization counts when it is completed and its occurrenceDateTime is in the window; with no window, at or before
    /// asOf, or not recorded as a date.
    /// </summary>
    public static bool Immunization(IElement immunization, ImmunizationCriterion criterion, DateTimeOffset asOf)
    {
        if (ClinicalValues.Literal(immunization, "status") != "completed")
        {
            return false;
        }

        DateTimeOffset? occurred = Instant(immunization, "occurrence.ofType(dateTime)");
        return criterion.Window is { } window ? occurred is { } instant && window.Contains(instant) : !(occurred > asOf);
    }

    /// <summary>
    /// The Observation's reading for the criterion, or null when it has none: its status must be final, amended or corrected
    /// and its instant (effectiveDateTime, else effectivePeriod.start, else effectiveInstant, else issued) in the window. The
    /// reading is Observation.code's when a listed code is there with a numeric valueQuantity, else the first component's
    /// with a listed code and a numeric valueQuantity. Without a comparison any Observation with a listed code is a reading.
    /// </summary>
    public static CohortReading? Reading(IElement observation, ObservationCriterion criterion)
    {
        if (ClinicalValues.Literal(observation, "status") is not { } status || !ResultStatuses.Contains(status, StringComparer.Ordinal))
        {
            return null;
        }

        if (Instant(observation, ObservationInstant) is not { } instant || !criterion.Window.Contains(instant))
        {
            return null;
        }

        bool needsValue = criterion.Comparator is not null;
        IEnumerable<IElement> carriers = [observation, .. observation.Select("component")];
        foreach (IElement carrier in carriers)
        {
            if (!Coded(carrier, criterion.Codes))
            {
                continue;
            }

            IElement? quantity = carrier.Select("value.ofType(Quantity)").FirstOrDefault();
            decimal? value = quantity is null ? null : Number(quantity.Select("value").FirstOrDefault()?.Value);
            if (value is not null || !needsValue)
            {
                string? unit = quantity is null ? null : ClinicalValues.Literal(quantity, "code") ?? ClinicalValues.Literal(quantity, "unit");
                return new CohortReading(instant, value, unit);
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="value"/> meets the comparison.</summary>
    public static bool Compare(decimal value, ValueComparator comparator, decimal threshold) => comparator switch
    {
        ValueComparator.Gt => value > threshold,
        ValueComparator.Ge => value >= threshold,
        ValueComparator.Lt => value < threshold,
        ValueComparator.Le => value <= threshold,
        _ => value == threshold,
    };

    /// <summary>
    /// A Patient is alive at asOf unless deceasedBoolean is true or deceasedDateTime is at or before asOf; a deceasedDateTime
    /// that is not a date still records a death.
    /// </summary>
    public static bool Living(IElement patient, DateTimeOffset asOf)
    {
        if (ClinicalValues.Literal(patient, "deceased.ofType(boolean)") == "true")
        {
            return false;
        }

        return ClinicalValues.Literal(patient, "deceased.ofType(dateTime)") is not { } died || CohortInstant.Parse(died) > asOf;
    }

    /// <summary>The Patient's age in whole years at <paramref name="asOf"/>'s UTC date; null without a full birthDate.</summary>
    public static int? Age(IElement patient, DateTimeOffset asOf)
    {
        if (ClinicalValues.Literal(patient, "birthDate") is not { Length: 10 } literal
            || !DateOnly.TryParseExact(literal, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly born))
        {
            return null;
        }

        DateOnly on = DateOnly.FromDateTime(asOf.UtcDateTime);
        int age = on.Year - born.Year;
        return on < born.AddYears(age) ? age - 1 : age;
    }

    private static bool ActiveAt(IElement condition, DateTimeOffset asOf)
    {
        if (ClinicalValues.Strings(condition, "clinicalStatus.coding.code").Any(code => InactiveClinicalStatuses.Contains(code, StringComparer.Ordinal)))
        {
            return false;
        }

        bool abated = ClinicalValues.Literal(condition, "abatement.ofType(boolean)") == "true"
            || Instant(condition, "abatement.ofType(dateTime)") <= asOf
            || Instant(condition, "abatement.ofType(Period).end") <= asOf
            || condition.Select("abatement.ofType(Age) | abatement.ofType(Range) | abatement.ofType(string)").Any();
        return !abated;
    }

    private static bool Coded(IElement carrier, IReadOnlyList<CohortCode> codes) =>
        carrier.Select("code.coding").Any(coding =>
        {
            string? system = ClinicalValues.Literal(coding, "system");
            string? code = ClinicalValues.Literal(coding, "code");
            return codes.Any(wanted => wanted.Matches(system, code));
        });

    /// <summary>The first of <paramref name="expressions"/> that yields a date or dateTime, as an instant.</summary>
    private static DateTimeOffset? Instant(IElement resource, params string[] expressions) =>
        expressions.Select(expression => CohortInstant.Parse(ClinicalValues.Literal(resource, expression))).FirstOrDefault(instant => instant is not null);

    private static decimal? Number(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        long number => number,
        double number => (decimal)number,
        _ => null,
    };
}
