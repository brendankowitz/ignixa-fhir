// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One patient's context as of one instant, for one call (by default the patient's latest dated resource, see
/// <see cref="Open"/>): the domains the root digest summarizes and the nodes
/// <c>patient_context</c> expand opens, each computed on first use. Membership comes from the compartment, order from the in-memory
/// engine's <c>_sort</c> over the compartment, and every value and rule from FHIRPath over the resources
/// (<see cref="ClinicalRules"/>). Not thread-safe.
/// </summary>
internal sealed class PatientContext
{
    /// <summary>Most items a domain stub shows.</summary>
    public const int TopItems = 5;

    /// <summary>The relative change between the newest and the oldest of the last three values that still counts as flat.</summary>
    public const decimal FlatTolerance = 0.05m;

    // The rules as facts state them: exact but compact. The FHIRPath behind each is in ClinicalRules and the tool README.
    // Every list is ordered by the date its items show (the main date), compared by its earliest instant (a date or a
    // year counts from its first moment; a time without an offset as UTC), newest first, undated last, then by id.
    public const string ActiveProblemRule =
        "active: clinicalStatus (condition-clinical) active, recurrence or relapse, and verificationStatus (condition-ver-status) not refuted or entered-in-error; active first, each newest first by date (onset, else recordedDate), then id";

    public const string ActiveMedicationRule =
        "active: status active and not doNotPerform; MedicationRequests and MedicationStatements newest first by date (authoredOn; effective, else dateAsserted), active first, then id";

    public const string AllergyRule = "every AllergyIntolerance, any status; newest first by date (onset, else recordedDate), then id";

    /// <summary>The information issue that counts the record's resources dated after asOf.</summary>
    public const string AfterAsOfCode = "dated-after-as-of";

    public const string EncounterRule = "period.start at or before asOf (default: the patient's latest dated resource), status not planned, cancelled or entered-in-error; newest first by period.start, then id";

    public const string LabRule =
        "Observations whose date (effective, else issued) is at or before asOf (default: the patient's latest dated resource), status not cancelled or entered-in-error, "
        + "category laboratory, vital-signs, none, or only categories outside observation-category; one series per first coding with system and code; "
        + "most observations first; latest = newest by that date, then id; trend: newest vs oldest of the last 3 values, numeric valueQuantity in one unit without a comparator only (else n/a), flat within 5%";

    public const string EncounterRuleUnverified = "period.start at or before asOf; newest first by period.start, then id";

    public const string LabRuleUnverified = "Observations whose date (effective, else issued) is at or before asOf; one series per first coding with system and code; most observations first; trend as for R4";

    public const string ProblemLinkRule =
        "the Condition, then every MedicationRequest and MedicationStatement whose reasonReference references it (reasonReference) or whose reasonCode shares a system and code with its code (reasonCode)";

    private readonly Lazy<IReadOnlyList<ContextEntry>> _problems;
    private readonly Lazy<IReadOnlyList<ContextEntry>> _medications;
    private readonly Lazy<IReadOnlyList<ContextEntry>> _allergies;
    private readonly Lazy<IReadOnlyList<ContextEntry>> _encounters;
    private readonly Lazy<(IReadOnlyList<LabSeries> Series, int Excluded)> _labs;
    private readonly Lazy<IReadOnlyList<ContextEntry>> _administrations;
    private readonly Lazy<IReadOnlyList<ContextEntry>> _immunizations;
    private readonly Lazy<IReadOnlyList<ContextEntry>> _procedures;
    private readonly Lazy<(DateTimeOffset Instant, FhirTemporal Bound, bool FromClock)> _asOf;
    private readonly Lazy<IReadOnlyList<CatalogEntry>> _members;
    private readonly Lazy<ILookup<string, CatalogEntry>> _membersByType;
    private readonly Lazy<IReadOnlyDictionary<string, int>> _afterAsOf;

    private readonly Clock.AsOf? _stated;

    private PatientContext(FolderSession session, PatientCompartment compartment, DateTimeOffset? asOf, Clock.AsOf? stated = null)
    {
        Session = session;
        Compartment = compartment;
        AsOfGiven = asOf is not null;
        _stated = stated;
        _asOf = new(() => asOf is { } given ? Given(given) : Latest());
        Elements = new ResourceElements(session);
        Shapes = new ValueShapes(session.Schema.Version, BindingMembership.IfLoaded(session));
        RulesApplied = ClinicalRules.Verified(session.Schema.Version);
        _problems = new(() => ActiveFirst(Newest(Describe(Compartment.OfType("Condition")).Where(NotAfter)), ClinicalRules.ActiveCondition));
        _medications = new(() => ActiveFirst(
            Newest([.. Describe(Compartment.OfType("MedicationRequest")).Where(NotAfter), .. Describe(Compartment.OfType("MedicationStatement")).Where(NotAfter)]),
            ClinicalRules.ActiveMedication));
        _allergies = new(() => Newest(Describe(Compartment.OfType("AllergyIntolerance")).Where(NotAfter)));
        _encounters = new(() => Keep(Newest(Describe(Compartment.OfType("Encounter")).Where(InWindow)), ClinicalRules.RecordedEncounter));
        _labs = new(BuildLabs);
        _administrations = new(() => Newest(Describe(Compartment.OfType("MedicationAdministration")).Where(NotAfter)));
        _immunizations = new(() => Newest(Describe(Compartment.OfType("Immunization")).Where(NotAfter)));
        _procedures = new(() => Newest(Describe(Compartment.OfType("Procedure")).Where(NotAfter)));
        // Without a stated asOf it is the latest main date of the record, so no member is after it and none is read to tell.
        _members = new(() => AsOfGiven ? [.. Compartment.Members.Where(member => !DatedAfterAsOf(member))] : Compartment.Members);
        _membersByType = new(() => Members.ToLookup(member => member.ResourceType, StringComparer.Ordinal));
        _afterAsOf = new(CountAfterAsOf);
    }

    public FolderSession Session { get; }

    public PatientCompartment Compartment { get; }

    /// <summary>
    /// The instant time-dependent domains are read at, to the second: the one the call gave, else the start of the patient's
    /// latest dated resource (<see cref="Open"/>).
    /// </summary>
    public DateTimeOffset AsOf => _asOf.Value.Instant;

    /// <summary>Whether the call gave <see cref="AsOf"/>, rather than it being the patient's latest dated resource.</summary>
    public bool AsOfGiven { get; }

    /// <summary>
    /// The compartment's members as of <see cref="AsOf"/>: every member but those whose main date (<see cref="ClinicalRules.MainDate"/>)
    /// is after it. Every count the map and the root show is of these; <see cref="PatientCompartment.Members"/> is the whole compartment.
    /// </summary>
    public IReadOnlyList<CatalogEntry> Members => _members.Value;

    /// <summary>Whether any member of the compartment is dated after <see cref="AsOf"/>, so is not in <see cref="Members"/>.</summary>
    public bool AnyAfterAsOf => _afterAsOf.Value.Count > 0;

    /// <summary>The <see cref="Members"/> of type <paramref name="resourceType"/>.</summary>
    public IReadOnlyList<CatalogEntry> MembersOf(string resourceType) =>
        AsOfGiven ? [.. _membersByType.Value[resourceType]] : Compartment.OfType(resourceType);

    /// <summary>Whether <see cref="AsOf"/> is the session clock's, the default for a patient with no dated resource.</summary>
    public bool AsOfFromClock => _asOf.Value.FromClock;

    /// <summary>
    /// <see cref="AsOf"/> as an envelope states it: as stated, when it was (<see cref="Open(FolderSession, string, Clock.AsOf?)"/>:
    /// the call's <c>argument</c> or the <c>session-option</c>, with its precision), else given by the call (<c>argument</c>), else the
    /// patient's latest dated resource (<c>patient-latest</c>), else the session clock (<c>clock</c>).
    /// </summary>
    public Clock.AsOf Anchor => _stated ?? new(AsOf, AsOfGiven ? Clock.AsOfSources.Argument : AsOfFromClock ? Clock.AsOfSources.Clock : Clock.AsOfSources.PatientLatest);

    public ResourceElements Elements { get; }

    /// <summary>The shape checks on the statuses, comparators and demographics this context's summaries copy.</summary>
    public ValueShapes Shapes { get; }

    /// <summary>Whether the session's FHIR version is one the status and category rules were written for, so they were applied.</summary>
    public bool RulesApplied { get; }

    /// <summary>The encounters domain's rule, as applied.</summary>
    public string EncounterRuleApplied => RulesApplied ? EncounterRule : EncounterRuleUnverified;

    /// <summary>The labs domain's rule, as applied.</summary>
    public string LabRuleApplied => RulesApplied ? LabRule : LabRuleUnverified;

    /// <summary>The patient as summaries name it: <c>Patient/id</c>, the id replaced by its digest when it is not a FHIR id.</summary>
    public string PatientReference => FhirIds.Reference("Patient", Compartment.Patient.Id);

    /// <summary>The patient id as <c>next</c> passes it back (see <see cref="PatientReference"/>).</summary>
    public string PatientId => FhirIds.Safe(Compartment.Patient.Id);

    /// <summary>The window of the time-dependent domains: everything up to <see cref="AsOf"/>.</summary>
    public ContextWindow Window => new(From: null, AsOf);

    /// <summary>Every Condition; active ones first when the rule was applied.</summary>
    public IReadOnlyList<ContextEntry> Problems => _problems.Value;

    /// <summary>Every MedicationRequest, then every MedicationStatement; active ones first when the rule was applied.</summary>
    public IReadOnlyList<ContextEntry> Medications => _medications.Value;

    public IReadOnlyList<ContextEntry> Allergies => _allergies.Value;

    /// <summary>Encounters in the window, newest first.</summary>
    public IReadOnlyList<ContextEntry> Encounters => _encounters.Value;

    /// <summary>Lab series in the window, most observations first.</summary>
    public IReadOnlyList<LabSeries> Labs => _labs.Value.Series;

    /// <summary>Every MedicationAdministration, any status, newest first.</summary>
    public IReadOnlyList<ContextEntry> Administrations => _administrations.Value;

    /// <summary>Every Immunization, any status, newest first.</summary>
    public IReadOnlyList<ContextEntry> Immunizations => _immunizations.Value;

    /// <summary>Every Procedure, any status, newest first.</summary>
    public IReadOnlyList<ContextEntry> Procedures => _procedures.Value;

    /// <summary>
    /// Observations of <see cref="Members"/> in no lab series: undated, outside the category or status rule, or without a coded
    /// code. Those dated after a stated asOf are not members, and the <see cref="AfterAsOfCode"/> note counts them.
    /// </summary>
    public int LabsExcluded => _labs.Value.Excluded;

    /// <summary>
    /// Opens the context of <c>Patient/<paramref name="patientId"/></c> as of <paramref name="asOf"/> (truncated to the second).
    /// Without one it is read as of the patient's own latest dated resource (the latest main date of any compartment member,
    /// <see cref="ClinicalRules.MainDate"/>), never the clock: a record whose dates are shifted (MIMIC's are in the 2100s) or
    /// that ended years ago is read whole, and a record with no dated resource is read as of the session clock.
    /// </summary>
    /// <exception cref="ToolRequestException">The id is malformed or the folder holds no such Patient.</exception>
    public static PatientContext Open(FolderSession session, string patientId, DateTimeOffset? asOf)
    {
        ArgumentNullException.ThrowIfNull(session);

        ClinicalRules.Precompile();
        return new PatientContext(session, PatientCompartment.Load(session, patientId), asOf);
    }

    /// <summary>
    /// <see cref="Open(FolderSession, string, DateTimeOffset?)"/> as of what was stated (<see cref="Clock.AsOfClock.Stated"/>: the call's
    /// asOf, else the session's <c>--as-of</c>), which <see cref="Anchor"/> then states with its source and precision; as of the
    /// patient's latest dated resource when nothing was.
    /// </summary>
    /// <exception cref="ToolRequestException">The id is malformed or the folder holds no such Patient.</exception>
    public static PatientContext Open(FolderSession session, string patientId, Clock.AsOf? stated)
    {
        ArgumentNullException.ThrowIfNull(session);

        ClinicalRules.Precompile();
        return new PatientContext(session, PatientCompartment.Load(session, patientId), stated?.Moment, stated);
    }

    private static (DateTimeOffset Instant, FhirTemporal Bound, bool FromClock) Given(DateTimeOffset asOf)
    {
        DateTimeOffset second = new(asOf.UtcTicks - (asOf.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        return FhirTemporal.TryParse(second.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), FhirPrimitive.Instant, out FhirTemporal? parsed)
            ? (second, parsed, false)
            : throw new InvalidOperationException($"asOf {asOf:O} did not parse as a FHIR instant.");
    }

    /// <summary>
    /// The latest main date among the compartment's members, as the window's bound (so every dated resource is inside it)
    /// and as the instant it starts at; the session clock when no member has one.
    /// </summary>
    private (DateTimeOffset Instant, FhirTemporal Bound, bool FromClock) Latest()
    {
        FhirTemporal? latest = Compartment.Members
            .Where(member => ClinicalRules.MainDate.ContainsKey(member.ResourceType))
            .Select(member => Elements.Get(member) is { } element ? ClinicalValues.MainDate(member.ResourceType, element) : null)
            .OfType<FhirTemporal>()
            .Max();
        if (latest is null || CohortInstant.Parse(latest.Literal) is not { } start)
        {
            return Given(Session.UtcNow()) with { FromClock = true };
        }

        return (new DateTimeOffset(start.UtcTicks - (start.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero), latest, false);
    }

    /// <summary>The Patient's demographics.</summary>
    public Demographics Demographics()
    {
        IElement? patient = Elements.Get(Compartment.Patient);
        return patient is null
            ? new Demographics(PatientReference, null, null, null, null)
            : new Demographics(
                PatientReference,
                ClinicalValues.PatientName(patient),
                Shapes.Gender("Patient", ClinicalValues.Literal(patient, "gender")),
                Shapes.Date("Patient", "birthDate", ClinicalValues.Literal(patient, "birthDate")),
                Deceased(patient));
    }

    /// <summary>
    /// <c>deceased[x]</c> as written: <c>true</c>/<c>false</c>, or the date of death (a dateTime literal, so a valid date by the
    /// parser), or null; anything else is shape-checked as a date.
    /// </summary>
    private string? Deceased(IElement patient) => patient.Select("deceased").Select(element => element.Value).FirstOrDefault() switch
    {
        bool flag => flag ? "true" : "false",
        FhirTemporal date => date.Literal,
        null => null,
        object other => Shapes.Date("Patient", "deceased", other.ToString()),
    };

    /// <summary>
    /// The issues every patient tool reports: the unsupported-version warning, types left out of the compartment, warnings from
    /// the rule queries, unreadable resources, and the duplicates and diagnostics of every index read (diagnostics beyond the
    /// limit are counted in <paramref name="omitted"/>), and the explanation of names given by the tools for the Patient and the
    /// <paramref name="shown"/> resources. Call after the answer is computed, so everything it read is covered.
    /// </summary>
    public List<IssueItem> Issues(List<OmittedItem> omitted, IEnumerable<CatalogEntry> shown)
    {
        List<IssueItem> issues = [];
        if (_afterAsOf.Value is { Count: > 0 } after)
        {
            issues.Add(new IssueItem(
                "information",
                AfterAsOfCode,
                $"{after.Values.Sum()} resources of the record are dated after asOf {CohortInstant.Format(AsOf)} ({string.Join(", ", after.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"))}): they are in the data but in the future relative to asOf, so no list, latest value or count includes them (record.resources, other, kinds and labs.excluded too).",
                Location: null));
        }

        if (!RulesApplied)
        {
            issues.Add(UnsupportedVersionIssue(Session.Schema.Version));
        }

        if (Compartment.UnevaluableIssue(PatientReference) is { } unevaluable)
        {
            issues.Add(unevaluable);
        }

        issues.AddRange(Elements.Problems);
        issues.AddRange(Shapes.Issues());
        Compartment.AddDiagnostics(Session, issues, omitted);
        issues.AddRange(NameIssues.For([Compartment.Patient, .. shown]));
        return issues;
    }

    /// <summary>The <see cref="ClinicalRules.UnsupportedVersionCode"/> warning for <paramref name="version"/>.</summary>
    public static IssueItem UnsupportedVersionIssue(FhirVersion version) => new(
        "warning",
        ClinicalRules.UnsupportedVersionCode,
        $"The patient tools' rules (active problems and medications, lab categories, main dates and codes) are written for R4 and this folder is read as {version}: the status and category rules were not applied (every Condition, medication and Observation is listed), and dates and codes are read by R4 element paths, which {version} may name differently.",
        Location: null);

    /// <summary>The summary items of <paramref name="resources"/>, in order; a resource that cannot be read is left out (and reported).</summary>
    public IReadOnlyList<ContextEntry> Describe(IEnumerable<CatalogEntry> resources) =>
        [.. resources.Select(Describe).OfType<ContextEntry>()];

    /// <summary>The summary item of <paramref name="resource"/>, or null when it cannot be read (and is reported).</summary>
    public ContextEntry? Describe(CatalogEntry resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string type = resource.ResourceType;
        if (Elements.Get(resource) is not { } element)
        {
            return null;
        }

        IReadOnlyList<CodedConcept> codes = CodedConcept.MainCodes(type, element);
        CodedConcept? concept = codes.Count > 0 ? codes[0] : null;
        List<CatalogEntry> sources = [resource];
        List<AbsentItem> absent = [];
        if (concept is null && type is "MedicationRequest" or "MedicationStatement" or "MedicationAdministration")
        {
            concept = ReferencedMedication(element, sources, absent);
        }

        LabCoding? labCoding = type == "Observation" ? LabCoding.Of(element) : null;
        IElement? quantity = type == "Observation" ? element.Select(ClinicalRules.QuantityValue).FirstOrDefault() : null;
        string? valueType = type == "Observation" && quantity is null ? element.Select("value").FirstOrDefault()?.InstanceType : null;
        FhirTemporal? date = ClinicalValues.MainDate(type, element);
        var item = new ContextItem(
            FhirIds.Reference(type, resource.Id),
            NodeKey.Of(type, resource.Id, labCoding),
            concept?.Key,
            concept?.Display,
            Shapes.Code(type, "status", ClinicalValues.Status(type, element)),
            date?.Literal,
            Value: quantity?.Scalar("value") as decimal?,
            Comparator: Shapes.Comparator(type, quantity?.Scalar("comparator") as string),
            Unit: quantity is null ? null : (quantity.Scalar("unit") ?? quantity.Scalar("code")) as string,
            ValueType: valueType);
        return new ContextEntry(resource, item, sources, absent, date);
    }

    /// <summary>
    /// <paramref name="entries"/> with each Observation's lab key kept only when that series exists in <see cref="Labs"/> (an
    /// observation outside the window or the category rule has none), so every key handed out can be expanded.
    /// </summary>
    public IReadOnlyList<ContextEntry> Navigable(IEnumerable<ContextEntry> entries)
    {
        HashSet<string> labKeys = [.. Labs.Select(series => series.NodeKey)];
        return [.. entries.Select(entry => entry.Item.NodeKey is { } key && key.StartsWith(NodeKey.LabPrefix, StringComparison.Ordinal) && !labKeys.Contains(key)
            ? entry with { Item = entry.Item with { NodeKey = null } }
            : entry)];
    }

    /// <summary>The element of <paramref name="entry"/>, or null when it cannot be read (and is reported).</summary>
    public IElement? Element(ContextEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Elements.Get(entry.Resource);
    }

    /// <summary>
    /// <paramref name="entries"/> newest first by the date they show (<see cref="FhirTemporal.CompareTo"/>, whose first key is
    /// the earliest instant), undated ones last, then by type and id, so the order is total.
    /// </summary>
    private static IReadOnlyList<ContextEntry> Newest(IEnumerable<ContextEntry> entries) =>
        [.. entries
            .OrderBy(entry => entry.Date is null)
            .ThenByDescending(entry => entry.Date)
            .ThenBy(entry => entry.Resource.ResourceType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Resource.Id, StringComparer.Ordinal)];

    /// <summary>Whether the date <paramref name="entry"/> shows starts at or before <see cref="AsOf"/>; an undated entry is outside.</summary>
    private bool InWindow(ContextEntry entry) => entry.Date is { } date && date.CompareTo(_asOf.Value.Bound) <= 0;

    /// <summary>Whether <paramref name="entry"/> is not dated after <see cref="AsOf"/>: undated, or starting at or before it.</summary>
    private bool NotAfter(ContextEntry entry) => entry.Date is not { } date || date.CompareTo(_asOf.Value.Bound) <= 0;

    // A member whose type has a main date and whose main date starts after asOf.
    private bool DatedAfterAsOf(CatalogEntry member) =>
        ClinicalRules.MainDate.ContainsKey(member.ResourceType) && Describe(member) is { } entry && !NotAfter(entry);

    // The members dated after asOf, by type: in the data, but in the future relative to asOf, so in no list or count.
    private IReadOnlyDictionary<string, int> CountAfterAsOf() => Compartment.Members.Count == Members.Count
        ? new Dictionary<string, int>(StringComparer.Ordinal)
        : Compartment.Members.Except(Members)
            .GroupBy(member => member.ResourceType, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    /// <summary>The entries <paramref name="rule"/> keeps, when rules are applied; all of them otherwise.</summary>
    private IReadOnlyList<ContextEntry> Keep(IReadOnlyList<ContextEntry> entries, string rule) =>
        RulesApplied ? [.. entries.Where(entry => Element(entry)!.IsTrue(rule))] : entries;

    /// <summary>The entries with <see cref="ContextItem.Active"/> set by <paramref name="rule"/>, active ones first (stable); unchanged when rules are not applied.</summary>
    private IReadOnlyList<ContextEntry> ActiveFirst(IReadOnlyList<ContextEntry> entries, string rule)
    {
        if (!RulesApplied)
        {
            return entries;
        }

        List<ContextEntry> marked = [.. entries.Select(entry => entry with { Item = entry.Item with { Active = Element(entry)!.IsTrue(rule) } })];
        return [.. marked.Where(entry => entry.Item.Active == true), .. marked.Where(entry => entry.Item.Active != true)];
    }

    /// <summary>
    /// The code of the Medication a medication resource references (contained, or <c>Medication/id</c> in the folder), adding
    /// the Medication to <paramref name="sources"/>; null, with an entry in <paramref name="absent"/> for a Medication the folder
    /// does not hold.
    /// </summary>
    private CodedConcept? ReferencedMedication(IElement medicationResource, List<CatalogEntry> sources, List<AbsentItem> absent)
    {
        if (medicationResource.Scalar(ClinicalRules.MedicationReference) is not string reference)
        {
            return null;
        }

        if (reference.StartsWith('#'))
        {
            IElement? contained = medicationResource.Select("contained").FirstOrDefault(resource => resource.Scalar("id") as string == reference[1..]);
            return contained?.Select("code").Select(CodedConcept.Read).FirstOrDefault();
        }

        if (reference.Split('/') is not ["Medication", string id] || !Session.Schema.ResourceTypeNames.Contains("Medication"))
        {
            return null;
        }

        if (Session.FindEntry("Medication", id) is not { } entry)
        {
            int medications = Session.Catalog.Entries.Count(candidate => candidate.ResourceType == "Medication");
            absent.Add(new AbsentItem(FhirIds.Reference("Medication", id), $"catalog of {Session.RootPath}: {medications} Medication resources", Session.Catalog.BuiltAt));
            return null;
        }

        sources.Add(entry);
        return Elements.Get(entry)?.Select("code").Select(CodedConcept.Read).FirstOrDefault();
    }

    private (IReadOnlyList<LabSeries> Series, int Excluded) BuildLabs()
    {
        IReadOnlyList<ContextEntry> windowed = Keep(Newest(Describe(Compartment.OfType("Observation")).Where(InWindow)), ClinicalRules.Recorded);
        IEnumerable<ContextEntry> labs = RulesApplied ? windowed.Where(entry => Element(entry)!.IsTrue(ClinicalRules.LabCategory)) : windowed;
        List<LabSeries> series = [.. labs
            .Where(entry => entry.Item.NodeKey is not null)
            .GroupBy(entry => entry.Item.NodeKey!, StringComparer.Ordinal)
            .Select(group => Series(group.Key, [.. group]))
            .OrderByDescending(lab => lab.Observations.Count)
            .ThenBy(lab => lab.NodeKey, StringComparer.Ordinal)];
        return (series, MembersOf("Observation").Count - series.Sum(lab => lab.Observations.Count));
    }

    private static LabSeries Series(string nodeKey, IReadOnlyList<ContextEntry> observations)
    {
        ContextEntry latest = observations[0];
        ContextItem summary = latest.Item with { NodeKey = nodeKey, Count = observations.Count, Trend = Trend(observations) };
        return new LabSeries(nodeKey, latest with { Item = summary }, observations);
    }

    /// <summary>Newest against oldest of the last three values (see <see cref="LabRule"/>).</summary>
    private static string Trend(IReadOnlyList<ContextEntry> newestFirst)
    {
        List<ContextItem> last = [.. newestFirst.Take(3).Select(entry => entry.Item)];
        if (last.Count < 2
            || last.Any(item => item.Value is null || item.Comparator is not null)
            || last.Select(item => item.Unit).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            return "n/a";
        }

        decimal newest = last[0].Value!.Value;
        decimal oldest = last[^1].Value!.Value;
        decimal change = newest - oldest;
        if (Math.Abs(change) <= Math.Abs(oldest) * FlatTolerance)
        {
            return "flat";
        }

        return change > 0 ? "up" : "down";
    }
}
