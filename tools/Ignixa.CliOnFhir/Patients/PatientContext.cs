// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One patient's context as of one instant, for one call: the domains the root digest summarizes and the nodes
/// <c>context_expand</c> opens, each computed on first use. Membership comes from the compartment, order from the in-memory
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

    public const string EncounterRule = "period.start at or before asOf, status not planned, cancelled or entered-in-error; newest first by period.start, then id";

    public const string LabRule =
        "Observations whose date (effective, else issued) is at or before asOf, status not cancelled or entered-in-error, category laboratory, vital-signs or none; one series per first coding with system and code; "
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
    private readonly FhirTemporal _asOf;

    private PatientContext(FolderSession session, PatientCompartment compartment, DateTimeOffset asOf)
    {
        Session = session;
        Compartment = compartment;
        AsOf = asOf;
        _asOf = FhirTemporal.TryParse(asOf.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), FhirPrimitive.Instant, out FhirTemporal? parsed)
            ? parsed
            : throw new InvalidOperationException($"asOf {asOf:O} did not parse as a FHIR instant.");
        Elements = new ResourceElements(session);
        RulesApplied = ClinicalRules.Verified(session.Schema.Version);
        _problems = new(() => ActiveFirst(Newest(Describe(Compartment.OfType("Condition"))), ClinicalRules.ActiveCondition));
        _medications = new(() => ActiveFirst(
            Newest([.. Describe(Compartment.OfType("MedicationRequest")), .. Describe(Compartment.OfType("MedicationStatement"))]),
            ClinicalRules.ActiveMedication));
        _allergies = new(() => Newest(Describe(Compartment.OfType("AllergyIntolerance"))));
        _encounters = new(() => Keep(Newest(Describe(Compartment.OfType("Encounter")).Where(InWindow)), ClinicalRules.RecordedEncounter));
        _labs = new(BuildLabs);
    }

    public FolderSession Session { get; }

    public PatientCompartment Compartment { get; }

    /// <summary>The instant time-dependent domains are read at, to the second.</summary>
    public DateTimeOffset AsOf { get; }

    public ResourceElements Elements { get; }

    /// <summary>Whether the session's FHIR version is one the status and category rules were written for, so they were applied.</summary>
    public bool RulesApplied { get; }

    /// <summary>The encounters domain's rule, as applied.</summary>
    public string EncounterRuleApplied => RulesApplied ? EncounterRule : EncounterRuleUnverified;

    /// <summary>The labs domain's rule, as applied.</summary>
    public string LabRuleApplied => RulesApplied ? LabRule : LabRuleUnverified;

    /// <summary>The patient as summaries name it: <c>Patient/id</c>, the id replaced by its digest when it is not a FHIR id.</summary>
    public string PatientReference => FhirIds.Reference("Patient", Compartment.Patient.Key.Id);

    /// <summary>The patient id as <c>next</c> passes it back (see <see cref="PatientReference"/>).</summary>
    public string PatientId => FhirIds.Safe(Compartment.Patient.Key.Id);

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

    /// <summary>Observations of the compartment in no lab series: outside the window or the category rule, or without a coded code.</summary>
    public int LabsExcluded => _labs.Value.Excluded;

    /// <summary>Opens the context of <c>Patient/<paramref name="patientId"/></c> as of <paramref name="asOf"/> (truncated to the second).</summary>
    /// <exception cref="ToolRequestException">The id is malformed or the folder holds no such Patient.</exception>
    public static PatientContext Open(FolderSession session, string patientId, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(session);

        ClinicalRules.Precompile();
        DateTimeOffset second = new(asOf.UtcTicks - (asOf.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        return new PatientContext(session, PatientCompartment.Load(session, patientId), second);
    }

    /// <summary>The Patient's demographics.</summary>
    public Demographics Demographics()
    {
        IElement? patient = Elements.Get(Compartment.Patient.Entry);
        return patient is null
            ? new Demographics(PatientReference, null, null, null, null)
            : new Demographics(PatientReference, ClinicalValues.PatientName(patient), ClinicalValues.Literal(patient, "gender"), ClinicalValues.Literal(patient, "birthDate"), ClinicalValues.Literal(patient, "deceased"));
    }

    /// <summary>
    /// The issues every patient tool reports: the unsupported-version warning, types left out of the compartment, warnings from
    /// the rule queries, unreadable resources, and the duplicates and diagnostics of every index read (diagnostics beyond the
    /// limit are counted in <paramref name="omitted"/>). Call after the answer is computed, so everything it read is covered.
    /// </summary>
    public List<IssueItem> Issues(List<OmittedItem> omitted)
    {
        List<IssueItem> issues = [];
        if (!RulesApplied)
        {
            issues.Add(UnsupportedVersionIssue(Session.Schema.Version));
        }

        if (Compartment.UnevaluableIssue(PatientReference) is { } unevaluable)
        {
            issues.Add(unevaluable);
        }

        issues.AddRange(Elements.Problems);
        ResourceSearcher.AddDiagnostics(Session, Compartment.Folder, issues, omitted);
        return issues;
    }

    /// <summary>The <see cref="ClinicalRules.UnsupportedVersionCode"/> warning for <paramref name="version"/>.</summary>
    public static IssueItem UnsupportedVersionIssue(FhirVersion version) => new(
        "warning",
        ClinicalRules.UnsupportedVersionCode,
        $"The patient tools' rules (active problems and medications, lab categories, main dates and codes) are written for R4 and this folder is read as {version}: the status and category rules were not applied (every Condition, medication and Observation is listed), and dates and codes are read by R4 element paths, which {version} may name differently.",
        Location: null);

    /// <summary>The summary items of <paramref name="resources"/>, in order; a resource that cannot be read is left out (and reported).</summary>
    public IReadOnlyList<ContextEntry> Describe(IEnumerable<IndexedResource> resources) =>
        [.. resources.Select(Describe).OfType<ContextEntry>()];

    /// <summary>The summary item of <paramref name="resource"/>, or null when it cannot be read (and is reported).</summary>
    public ContextEntry? Describe(IndexedResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string type = resource.Key.ResourceType;
        if (Elements.Get(resource.Entry) is not { } element)
        {
            return null;
        }

        IReadOnlyList<CodedConcept> codes = CodedConcept.MainCodes(type, element);
        CodedConcept? concept = codes.Count > 0 ? codes[0] : null;
        List<CatalogEntry> sources = [resource.Entry];
        List<AbsentItem> absent = [];
        if (concept is null && type is "MedicationRequest" or "MedicationStatement")
        {
            concept = ReferencedMedication(element, sources, absent);
        }

        LabCoding? labCoding = type == "Observation" ? LabCoding.Of(element) : null;
        IElement? quantity = type == "Observation" ? element.Select(ClinicalRules.QuantityValue).FirstOrDefault() : null;
        string? valueType = type == "Observation" && quantity is null ? element.Select("value").FirstOrDefault()?.InstanceType : null;
        FhirTemporal? date = ClinicalValues.MainDate(type, element);
        var item = new ContextItem(
            FhirIds.Reference(type, resource.Key.Id),
            NodeKey.Of(type, resource.Key.Id, labCoding),
            concept?.Key,
            concept?.Display,
            ClinicalValues.Status(type, element),
            date?.Literal,
            Value: quantity?.Scalar("value") as decimal?,
            Comparator: quantity?.Scalar("comparator") as string,
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

        return Elements.Get(entry.Resource.Entry);
    }

    /// <summary>
    /// <paramref name="entries"/> newest first by the date they show (<see cref="FhirTemporal.CompareTo"/>, whose first key is
    /// the earliest instant), undated ones last, then by type and id, so the order is total.
    /// </summary>
    private static IReadOnlyList<ContextEntry> Newest(IEnumerable<ContextEntry> entries) =>
        [.. entries
            .OrderBy(entry => entry.Date is null)
            .ThenByDescending(entry => entry.Date)
            .ThenBy(entry => entry.Resource.Key.ResourceType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Resource.Key.Id, StringComparer.Ordinal)];

    /// <summary>Whether the date <paramref name="entry"/> shows starts at or before <see cref="AsOf"/>; an undated entry is outside.</summary>
    private bool InWindow(ContextEntry entry) => entry.Date is { } date && date.CompareTo(_asOf) <= 0;

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
        return (series, Compartment.OfType("Observation").Count - series.Sum(lab => lab.Observations.Count));
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
