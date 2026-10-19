// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Terminology.DrugClasses;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>A checked <c>patient_cohort</c> request: every criterion a patient must meet, all ANDed.</summary>
/// <param name="AsOf">The instant the question is asked at; nothing after it counts.</param>
/// <param name="LivingOnly">Only patients alive at <paramref name="AsOf"/> count.</param>
/// <param name="AgeMin">Youngest age in whole years at <paramref name="AsOf"/>, inclusive.</param>
/// <param name="AgeMax">Oldest age in whole years at <paramref name="AsOf"/>, inclusive.</param>
/// <param name="Gender">Patient.gender, when asked.</param>
/// <param name="AsOfSource">
/// Where <paramref name="AsOf"/> came from (<see cref="AsOfSources"/>, <c>facts.asOfSource</c>): <c>argument</c> when the request
/// gave it, <c>clock</c> when it defaulted to the session clock's now (V15 named that <c>default-now</c>).
/// </param>
/// <param name="AsOfPrecision">The given asOf's precision when it is not an instant (<c>year</c>, <c>month</c>, <c>date</c>); null otherwise.</param>
/// <param name="AsOfZoneAssumed">Whether the given asOf was a date and time without a zone, read as UTC.</param>
internal sealed record CohortRequest(
    DateTimeOffset AsOf,
    bool LivingOnly,
    int? AgeMin,
    int? AgeMax,
    string? Gender,
    IReadOnlyList<ConditionCriterion> Conditions,
    IReadOnlyList<MedicationCriterion> Medications,
    IReadOnlyList<ImmunizationCriterion> Immunizations,
    IReadOnlyList<ObservationCriterion> Observations,
    string AsOfSource,
    string? AsOfPrecision = null,
    bool AsOfZoneAssumed = false)
{
    public const string Active = "active";
    public const string AnyStatus = "any";
    public const string ConfirmedOrUnspecified = "confirmed-or-unspecified";
    public const string Include = "include";
    public const string Exclude = "exclude";

    private static readonly string[] Genders = ["male", "female", "other", "unknown"];
    private static readonly string[] Statuses = [Active, AnyStatus];
    private static readonly string[] Verifications = [ConfirmedOrUnspecified, AnyStatus];
    private static readonly string[] Picks = ["latest", "earliest", "any"];
    private static readonly string[] Comparators = ["gt", "ge", "lt", "le", "eq"];
    private static readonly string[] Missing = [Exclude, Include];
    /// <summary>The longest valueSet a condition criterion takes: a canonical url with its version, or isa/&lt;sctid&gt;.</summary>
    public const int MaxValueSetLength = 1024;

    private static readonly string[] ConditionProperties = ["codes", "valueSet", "status", "verification"];
    private static readonly string[] MedicationProperties = ["codes", "classId", "source", "status"];
    private static readonly string[] ImmunizationProperties = ["codes", "window", "ever"];
    private static readonly string[] ObservationProperties = ["codes", "window", "pick", "comparator", "value", "unit", "missing"];
    private static readonly string[] WindowProperties = ["after", "before"];

    /// <summary>Whether asOf defaulted to the clock, the request having stated none.</summary>
    public bool AsOfDefaulted => AsOfSource == AsOfSources.Clock;

    /// <summary>
    /// Checks <paramref name="arguments"/>; <paramref name="stated"/> is the asOf the call or the session stated (their <c>asOf</c>
    /// read by <see cref="AsOfClock.Stated"/>, a date as that day's first instant in UTC), and <paramref name="now"/> is asOf when
    /// neither states one.
    /// </summary>
    /// <exception cref="ToolRequestException">
    /// An argument or criterion is malformed, contradictory or incomplete (invalid-parameter); asOf is not a FHIR date or instant
    /// (<see cref="AsOfClock.InvalidCode"/>).
    /// </exception>
    public static CohortRequest Parse(CohortArguments arguments, AsOf? stated, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // To the second, as facts.asOf and the envelope state it, so the instant counted at is the one both show.
        DateTimeOffset given = (stated?.Moment ?? now).ToUniversalTime();
        DateTimeOffset asOf = new(given.Ticks - (given.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        if (arguments.AgeMin is < 0 || arguments.AgeMax is < 0 || arguments.AgeMax < arguments.AgeMin)
        {
            throw CriterionObject.Invalid($"ageMin ({arguments.AgeMin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}) and ageMax ({arguments.AgeMax?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}) are whole years, at least 0, with ageMin <= ageMax.");
        }

        if (arguments.Gender is { } gender && !Genders.Contains(gender, StringComparer.Ordinal))
        {
            throw CriterionObject.Invalid($"gender is {string.Join(", ", Genders)}, not '{QueryEcho.Value(gender)}'.");
        }

        return new CohortRequest(
            asOf,
            arguments.Living ?? true,
            arguments.AgeMin,
            arguments.AgeMax,
            arguments.Gender,
            [.. CriterionObject.List(arguments.Conditions, "conditions", ConditionProperties).Select(Condition)],
            [.. CriterionObject.List(arguments.Medications, "medications", MedicationProperties).Select(Medication)],
            [.. CriterionObject.List(arguments.Immunizations, "immunizations", ImmunizationProperties).Select(criterion => Immunization(criterion, asOf))],
            [.. CriterionObject.List(arguments.Observations, "observations", ObservationProperties).Select(criterion => Observation(criterion, asOf))],
            stated?.Source ?? AsOfSources.Clock,
            stated?.ShownPrecision,
            stated?.ZoneAssumed ?? false);
    }

    private static ConditionCriterion Condition(CriterionObject criterion)
    {
        IReadOnlyList<CohortCode> codes = Codes(criterion, required: false);
        string? valueSet = criterion.String("valueSet")?.Trim();
        if (valueSet is { Length: > MaxValueSetLength })
        {
            throw CriterionObject.Invalid($"{criterion.Where}.valueSet '{QueryEcho.Value(valueSet)}' is longer than {MaxValueSetLength} characters; give a ValueSet url (url|version) or isa/<sctid>.");
        }
        if (codes.Count == 0 && string.IsNullOrEmpty(valueSet))
        {
            throw CriterionObject.Invalid($"{criterion.Where} needs 'codes' (['http://snomed.info/sct|44054006']) or 'valueSet' (a ValueSet url, or isa/<sctid> for a SNOMED CT concept and its descendants).");
        }

        return new ConditionCriterion(
            codes,
            string.IsNullOrEmpty(valueSet) ? null : valueSet,
            criterion.Choice("status", Statuses, Active) == Active,
            criterion.Choice("verification", Verifications, ConfirmedOrUnspecified) == ConfirmedOrUnspecified);
    }

    private static MedicationCriterion Medication(CriterionObject criterion)
    {
        IReadOnlyList<CohortCode> codes = Codes(criterion, required: false);
        string? classId = criterion.String("classId")?.Trim();
        string? source = criterion.String("source");
        if (codes.Count == 0 && string.IsNullOrEmpty(classId))
        {
            throw CriterionObject.Invalid($"{criterion.Where} needs 'codes' (RxNorm, for example ['http://www.nlm.nih.gov/research/umls/rxnorm|314231']) or 'classId' (an RxClass class such as C10AA; find one with terminology class-search).");
        }

        if (source is not null && string.IsNullOrEmpty(classId))
        {
            throw CriterionObject.Invalid($"{criterion.Where}.source names the classification of 'classId', which is not given.");
        }

        DrugClassSource? classSource = null;
        if (!string.IsNullOrEmpty(classId))
        {
            if (!RxClassJson.IsClassId(classId))
            {
                throw CriterionObject.Invalid($"{criterion.Where}.classId '{QueryEcho.Value(classId)}' is not an RxClass class id (letters and digits, for example C10AA).");
            }

            IReadOnlyList<DrugClassSource> named = DrugClassSource.Parse(source, [DrugClassSource.Atc]);
            classSource = named.Count == 1 ? named[0] : throw CriterionObject.Invalid($"{criterion.Where}.source is one of {DrugClassSource.Names}.");
        }

        return new MedicationCriterion(codes, string.IsNullOrEmpty(classId) ? null : classId, classSource, criterion.Choice("status", Statuses, Active) == Active);
    }

    private static ImmunizationCriterion Immunization(CriterionObject criterion, DateTimeOffset asOf)
    {
        IReadOnlyList<CohortCode> codes = Codes(criterion, required: true);
        CohortWindow? window = Window(criterion, asOf);
        bool? ever = criterion.Boolean("ever");
        if (ever == true && window is not null)
        {
            throw CriterionObject.Invalid($"{criterion.Where} has both ever=true and a window; give one of them.");
        }

        if (ever == false && window is null)
        {
            throw CriterionObject.Invalid($"{criterion.Where} has ever=false and no window; give the window the immunization must fall in, or leave both out for ever.");
        }

        return new ImmunizationCriterion(codes, window);
    }

    private static ObservationCriterion Observation(CriterionObject criterion, DateTimeOffset asOf)
    {
        IReadOnlyList<CohortCode> codes = Codes(criterion, required: true);
        CohortWindow window = Window(criterion, asOf) ?? new CohortWindow(null, asOf);
        string? comparatorText = criterion.String("comparator") is null ? null : criterion.Choice("comparator", Comparators, "gt");
        decimal? value = criterion.Number("value");
        if ((comparatorText is null) != (value is null))
        {
            throw CriterionObject.Invalid($"{criterion.Where} needs 'comparator' and 'value' together (comparator 'gt' and value 6.0 for 'above 6.0'), or neither to ask only for a reading in the window.");
        }

        bool includeMissing = criterion.Choice("missing", Missing, Exclude) == Include;
        if (includeMissing && comparatorText is null)
        {
            throw CriterionObject.Invalid($"{criterion.Where} has missing='include' without a comparison, so every patient would meet it; add comparator and value, or leave missing out.");
        }

        string? unit = criterion.String("unit");
        if (unit is not null && comparatorText is null)
        {
            throw CriterionObject.Invalid($"{criterion.Where}.unit is the unit of 'value', which is not given.");
        }

        ReadingPick pick = criterion.Choice("pick", Picks, "latest") switch
        {
            "earliest" => ReadingPick.Earliest,
            "any" => ReadingPick.Any,
            _ => ReadingPick.Latest,
        };
        ValueComparator? comparator = comparatorText switch
        {
            null => null,
            "ge" => ValueComparator.Ge,
            "lt" => ValueComparator.Lt,
            "le" => ValueComparator.Le,
            "eq" => ValueComparator.Eq,
            _ => ValueComparator.Gt,
        };
        return new ObservationCriterion(codes, window, pick, comparator, value, string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), includeMissing);
    }

    private static IReadOnlyList<CohortCode> Codes(CriterionObject criterion, bool required)
    {
        IReadOnlyList<string> texts = criterion.Strings("codes") ?? [];
        if (required && texts.Count == 0)
        {
            throw CriterionObject.Invalid($"{criterion.Where} needs 'codes': a list of 'system|code' (or bare codes, which match in any system).");
        }

        return [.. texts.Select((text, index) => CohortCode.Parse(text, $"{criterion.Where}.codes[{index}]")).Distinct()];
    }

    /// <summary>The criterion's window, never reaching past <paramref name="asOf"/>; null when it has none.</summary>
    private static CohortWindow? Window(CriterionObject criterion, DateTimeOffset asOf)
    {
        if (criterion.Object("window", WindowProperties) is not { } window)
        {
            return null;
        }

        DateTimeOffset? after = window.String("after") is { } afterText ? CohortInstant.Argument(afterText, $"{window.Where}.after") : null;
        DateTimeOffset before = window.String("before") is { } beforeText ? CohortInstant.Argument(beforeText, $"{window.Where}.before") : asOf;
        if (before > asOf)
        {
            before = asOf;
        }

        if (after >= before)
        {
            throw CriterionObject.Invalid($"{window.Where} is empty: 'after' (exclusive) must be earlier than 'before' (inclusive, at most asOf {CohortInstant.Format(asOf)}).");
        }

        return new CohortWindow(after, before);
    }
}
