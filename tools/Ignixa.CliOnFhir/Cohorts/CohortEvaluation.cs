// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// Applies a cohort request's criteria in order to the folder's patients, keeping those that meet each one. Each criterion is
/// one or two FHIR searches over the whole folder (the search engine matches the codes, following chains, Bundle and
/// conditional references), whose matches are grouped by the Patient compartments they are in, as <c>perPatient</c> and
/// <c>compartment</c> do; only matches of patients still in the cohort are read, and <see cref="CohortRules"/> decides on
/// each. Created for one call; not thread-safe.
/// </summary>
internal sealed class CohortEvaluation
{
    /// <summary>Most resources one call reads; each is parsed and held for the call.</summary>
    public const int MaxResourcesRead = 50_000;

    private const string PatientType = "Patient";

    private readonly FolderSession _session;
    private readonly CohortRequest _request;
    private readonly List<IssueItem> _issues;
    private readonly List<OmittedItem> _omitted;
    private readonly CancellationToken _cancellationToken;
    private readonly ResourceElements _elements;
    private readonly int _maxRead;
    private readonly List<CatalogEntry> _patients = [];
    private readonly HashSet<string> _remaining = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CatalogEntry>> _deciding = new(StringComparer.Ordinal);
    private readonly List<CohortStep> _steps = [];
    private DateTimeOffset _builtAt;
    private int _read;

    /// <param name="session">The folder.</param>
    /// <param name="request">The checked request.</param>
    /// <param name="issues">Receives the searches' warnings, the indexes' diagnostics and the evaluation's own warnings.</param>
    /// <param name="omitted">Receives diagnostics beyond those listed.</param>
    /// <param name="maxRead">Most resources read; <see cref="MaxResourcesRead"/> but in tests.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    public CohortEvaluation(FolderSession session, CohortRequest request, List<IssueItem> issues, List<OmittedItem> omitted, int maxRead, CancellationToken cancellationToken)
    {
        _session = session;
        _request = request;
        _issues = issues;
        _omitted = omitted;
        _maxRead = maxRead;
        _cancellationToken = cancellationToken;
        _elements = new ResourceElements(session);
    }

    /// <summary>Evaluates every criterion in request order: demographics, conditions, medications, immunizations, observations.</summary>
    /// <exception cref="ToolRequestException">A search fails, or more than the most resources would be read.</exception>
    public CohortOutcome Run(IReadOnlyList<CriterionCodes> conditionCodes, IReadOnlyList<CriterionCodes> medicationCodes)
    {
        Demographics();
        for (int index = 0; index < _request.Conditions.Count; index++)
        {
            Condition(index, _request.Conditions[index], conditionCodes[index]);
        }

        for (int index = 0; index < _request.Medications.Count; index++)
        {
            Medication(index, _request.Medications[index], medicationCodes[index]);
        }

        for (int index = 0; index < _request.Immunizations.Count; index++)
        {
            Immunization(index, _request.Immunizations[index]);
        }

        for (int index = 0; index < _request.Observations.Count; index++)
        {
            Observation(index, _request.Observations[index]);
        }

        _issues.AddRange(_elements.Problems);
        List<CatalogEntry> counted = [.. _patients.Where(patient => _remaining.Contains(patient.Id))];
        List<IReadOnlyList<CatalogEntry>> evidence = [.. counted.Select(patient => (IReadOnlyList<CatalogEntry>)[patient, .. _deciding.GetValueOrDefault(patient.Id) ?? []])];
        return new CohortOutcome(_steps, counted, evidence, _builtAt);
    }

    private void Demographics()
    {
        ResourceTypeIndex index = _session.GetIndex(PatientType);
        _builtAt = index.BuiltAt;
        ResourceSearcher.AddDiagnostics(_session, [PatientType], index.Diagnostics, _issues, _omitted);
        _patients.AddRange(index.Resources.Select(resource => resource.Entry));
        _remaining.UnionWith(_patients.Select(patient => patient.Id));
        _steps.Add(new CohortStep("patients", "every Patient in the folder", _remaining.Count));

        DateTimeOffset asOf = _request.AsOf;
        if (_request.LivingOnly)
        {
            Keep(patient => CohortRules.Living(patient, asOf));
            _steps.Add(new CohortStep("living", "alive at asOf: no deceasedBoolean true and no deceasedDateTime at or before asOf", _remaining.Count));
        }

        if (_request.AgeMin is not null || _request.AgeMax is not null)
        {
            int unknown = 0;
            Keep(patient =>
            {
                int? age = CohortRules.Age(patient, asOf);
                unknown += age is null ? 1 : 0;
                return age is { } years && !(years < _request.AgeMin) && !(years > _request.AgeMax);
            });
            _steps.Add(new CohortStep("age", $"whole years at asOf from birthDate: {Band(_request.AgeMin, _request.AgeMax)}", _remaining.Count));
            if (unknown > 0)
            {
                _issues.Add(new IssueItem("warning", "age-unknown", $"{unknown} patients have no full birthDate (YYYY-MM-DD), so their age at asOf is unknown and they were left out by the age criterion.", Location: null));
            }
        }

        if (_request.Gender is { } gender)
        {
            Keep(patient => ClinicalValues.Literal(patient, "gender") == gender);
            _steps.Add(new CohortStep("gender", $"Patient.gender = {gender}", _remaining.Count));
        }
    }

    private void Condition(int index, ConditionCriterion criterion, CriterionCodes codes)
    {
        string where = $"conditions[{index}]";
        string status = criterion.ActiveOnly
            ? "active at asOf (clinicalStatus not inactive/resolved/remission, not abated by asOf)"
            : "any clinical status";
        string verification = criterion.ConfirmedOnly ? "verificationStatus confirmed or absent" : "any verificationStatus but entered-in-error";
        string rule = $"a Condition with one of {Count(codes.Codes)}, {status}, {verification}, onset at or before asOf or not recorded";
        DateTimeOffset asOf = _request.AsOf;
        Dictionary<string, CatalogEntry?> met = Matching(where, "Condition", [$"code={Tokens(codes.Codes)}"], codes.Codes.Count, condition => CohortRules.Condition(condition, criterion, asOf));
        Apply(where, rule, met, codes);
    }

    private void Medication(int index, MedicationCriterion criterion, CriterionCodes codes)
    {
        string where = $"medications[{index}]";
        string status = criterion.ActiveOnly ? "status active" : "any status but entered-in-error, cancelled or draft";
        string rule = $"a MedicationRequest for one of {Count(codes.Codes)} (medicationCodeableConcept, or the Medication.code of medicationReference), {status}, not doNotPerform, authoredOn at or before asOf or not recorded";
        string tokens = Tokens(codes.Codes);
        DateTimeOffset asOf = _request.AsOf;
        Dictionary<string, CatalogEntry?> met = Matching(where, "MedicationRequest", [$"code={tokens}", $"medication:Medication.code={tokens}"], codes.Codes.Count, order => CohortRules.MedicationRequest(order, criterion, asOf));
        Apply(where, rule, met, codes);
    }

    private void Immunization(int index, ImmunizationCriterion criterion)
    {
        string where = $"immunizations[{index}]";
        string when = criterion.Window is { } window ? $"occurrenceDateTime in {window.Describe()}" : "ever (occurrenceDateTime at or before asOf, or not recorded)";
        string rule = $"a completed Immunization with one of {Count(criterion.Codes)}, {when}";
        DateTimeOffset asOf = _request.AsOf;
        Dictionary<string, CatalogEntry?> met = Matching(where, "Immunization", [$"vaccine-code={Tokens(criterion.Codes)}"], criterion.Codes.Count, immunization => CohortRules.Immunization(immunization, criterion, asOf));
        Apply(where, rule, met, new CriterionCodes(criterion.Codes, null));
    }

    private void Observation(int index, ObservationCriterion criterion)
    {
        string where = $"observations[{index}]";
        Dictionary<string, List<(CatalogEntry Entry, IElement Element)>> candidates = Candidates(where, "Observation", [$"combo-code={Tokens(criterion.Codes)}"]);
        Dictionary<string, CatalogEntry?> met = new(StringComparer.Ordinal);
        Dictionary<string, int> mismatchedUnits = new(StringComparer.Ordinal);
        int withoutReading = 0;
        foreach (string patient in _remaining)
        {
            List<(CatalogEntry Entry, CohortReading Reading)> readings = [.. (candidates.GetValueOrDefault(patient) ?? [])
                .Select(candidate => (candidate.Entry, Reading: CohortRules.Reading(candidate.Element, criterion)))
                .Where(candidate => candidate.Reading is not null)
                .Select(candidate => (candidate.Entry, candidate.Reading!))];
            if (readings.Count == 0)
            {
                if (criterion.IncludeMissing)
                {
                    met[patient] = null;
                    withoutReading++;
                }

                continue;
            }

            if (Decide(criterion, readings, mismatchedUnits) is { } deciding)
            {
                met[patient] = deciding;
            }
        }

        if (mismatchedUnits.Count > 0)
        {
            string units = string.Join(", ", mismatchedUnits.OrderByDescending(pair => pair.Value).Select(pair => $"'{pair.Key}' for {pair.Value}"));
            _issues.Add(new IssueItem(
                "warning",
                "unit-mismatch",
                $"{where}: the reading compared was not in '{criterion.Unit}' ({units} patients), so it was not compared and those patients do not meet the criterion. Nothing was converted; to count them, add a criterion run with the value in that unit.",
                Location: null));
        }

        string pick = criterion.Pick switch
        {
            ReadingPick.Earliest => "the earliest",
            ReadingPick.Any => "any",
            _ => "the latest",
        };
        string comparison = criterion.Comparator is { } comparator ? $" is {Symbol(comparator)} {criterion.Value!.Value.ToString(CultureInfo.InvariantCulture)}{(criterion.Unit is { } unit ? " " + unit : string.Empty)}" : " exists";
        string missing = criterion.IncludeMissing ? "; a patient with no reading in the window counts" : string.Empty;
        string rule = $"{pick} final/amended/corrected reading of one of {Count(criterion.Codes)} (Observation.code or a component) with instant in {criterion.Window.Describe()}{comparison}{missing}";
        Apply(where, rule, met, new CriterionCodes(criterion.Codes, null), criterion.IncludeMissing ? withoutReading : null);
    }

    /// <summary>The reading that puts the patient in the cohort, or null when none does.</summary>
    private static CatalogEntry? Decide(ObservationCriterion criterion, List<(CatalogEntry Entry, CohortReading Reading)> readings, Dictionary<string, int> mismatchedUnits)
    {
        // Latest first, or earliest first; at the same instant the smaller id first, in both directions.
        List<(CatalogEntry Entry, CohortReading Reading)> ordered = criterion.Pick == ReadingPick.Earliest
            ? [.. readings.OrderBy(reading => reading.Reading.Instant).ThenBy(reading => reading.Entry.Id, StringComparer.Ordinal)]
            : [.. readings.OrderByDescending(reading => reading.Reading.Instant).ThenBy(reading => reading.Entry.Id, StringComparer.Ordinal)];
        if (criterion.Comparator is not { } comparator)
        {
            return ordered[0].Entry;
        }

        IEnumerable<(CatalogEntry Entry, CohortReading Reading)> compared = criterion.Pick == ReadingPick.Any ? ordered : ordered.Take(1);
        string? mismatched = null;
        foreach ((CatalogEntry entry, CohortReading reading) in compared)
        {
            if (criterion.Unit is { } unit && !string.Equals(reading.Unit, unit, StringComparison.Ordinal))
            {
                mismatched ??= reading.Unit ?? "(no unit)";
                continue;
            }

            if (CohortRules.Compare(reading.Value!.Value, comparator, criterion.Value!.Value))
            {
                return entry;
            }
        }

        if (mismatched is not null)
        {
            mismatchedUnits[mismatched] = mismatchedUnits.GetValueOrDefault(mismatched) + 1;
        }

        return null;
    }

    /// <summary>Per patient still in the cohort, the first of the matches (in catalog order) that <paramref name="counts"/> accepts.</summary>
    private Dictionary<string, CatalogEntry?> Matching(string where, string resourceType, IReadOnlyList<string> queries, int codeCount, Func<IElement, bool> counts)
    {
        Dictionary<string, CatalogEntry?> met = new(StringComparer.Ordinal);
        if (codeCount == 0)
        {
            return met;
        }

        foreach ((string patient, List<(CatalogEntry Entry, IElement Element)> candidates) in Candidates(where, resourceType, queries))
        {
            if (candidates.FirstOrDefault(candidate => counts(candidate.Element)) is { Entry: { } entry })
            {
                met[patient] = entry;
            }
        }

        return met;
    }

    /// <summary>
    /// The matches of <paramref name="queries"/> (each match once, in query then catalog order), read and grouped by the
    /// patients still in the cohort whose Patient compartment they are in.
    /// </summary>
    private Dictionary<string, List<(CatalogEntry Entry, IElement Element)>> Candidates(string where, string resourceType, IReadOnlyList<string> queries)
    {
        Dictionary<string, List<(CatalogEntry, IElement)>> byPatient = new(StringComparer.Ordinal);
        if (_remaining.Count == 0)
        {
            return byPatient;
        }

        InMemoryCompartmentMembership membership = _session.Compartments;
        if (membership.UnevaluableParameters(PatientType, resourceType) is { Count: > 0 } unevaluable)
        {
            throw SearchFailures.NotSupported($"{where}: which patient a {resourceType} belongs to cannot be told, since its Patient compartment parameters {string.Join(", ", unevaluable)} are not search parameters of this FHIR version.");
        }

        HashSet<(string, string)> seen = [];
        foreach (string query in queries)
        {
            foreach (IndexedResource match in Search(where, resourceType, query))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add((match.Key.ResourceType, match.Key.Id)))
                {
                    continue;
                }

                List<string> owners = [.. PerPatient.Owners(membership, match).Where(_remaining.Contains)];
                if (owners.Count == 0 || Read(where, match.Entry) is not { } element)
                {
                    continue;
                }

                foreach (string owner in owners)
                {
                    (byPatient.TryGetValue(owner, out List<(CatalogEntry, IElement)>? list) ? list : byPatient[owner] = []).Add((match.Entry, element));
                }
            }
        }

        return byPatient;
    }

    private List<IndexedResource> Search(string where, string resourceType, string query)
    {
        SearchMatch match;
        try
        {
            match = ResourceSearcher.MatchAll(_session, new SearchRequest(resourceType, query));
        }
        catch (ToolRequestException ex)
        {
            throw new ToolRequestException(ex.Issue with { Diagnostics = $"{where}: {ex.Issue.Diagnostics}" });
        }

        List<IssueItem> found = [.. match.Issues.Select(issue => issue with { Diagnostics = $"{where}: {issue.Diagnostics}" })];
        found.AddRange(ReferenceFollowChecks.UnresolvedReferences(match.Source, new SearchOptions(match.Parsed.Options) { Include = [], RevInclude = [] }, []));
        List<OmittedItem> beyond = [];
        ResourceSearcher.AddDiagnostics(_session, match.Source, found, beyond);
        _issues.AddRange(found.Where(issue => !_issues.Contains(issue)));
        _omitted.AddRange(beyond.Where(item => !_omitted.Contains(item)));
        foreach (ResourceTypeIndex index in match.Source.IndexesRead.Where(index => index.BuiltAt > _builtAt))
        {
            _builtAt = index.BuiltAt;
        }

        return match.Matches;
    }

    private IElement? Read(string where, CatalogEntry entry)
    {
        if (++_read > _maxRead)
        {
            throw ToolRequestException.Error(
                "too-many-matches",
                $"{where}: the criteria matched more than {_maxRead} resources to read in one call; put the most selective criteria first (a condition before a lab), or narrow a window.");
        }

        return _elements.Get(entry);
    }

    /// <summary>
    /// Keeps the patients <paramref name="keep"/> accepts; a Patient that cannot be read is left out (and reported). Patients
    /// are not counted against the read limit: there is one per patient, and the limit bounds what criteria fan out to.
    /// </summary>
    private void Keep(Func<IElement, bool> keep)
    {
        foreach (CatalogEntry patient in _patients.Where(patient => _remaining.Contains(patient.Id)))
        {
            if (_elements.Get(patient) is not { } element || !keep(element))
            {
                _remaining.Remove(patient.Id);
            }
        }
    }

    private void Apply(string where, string rule, Dictionary<string, CatalogEntry?> met, CriterionCodes codes, int? withoutReading = null)
    {
        _remaining.IntersectWith(met.Keys);
        foreach ((string patient, CatalogEntry? entry) in met)
        {
            if (entry is not null)
            {
                (_deciding.TryGetValue(patient, out List<CatalogEntry>? list) ? list : _deciding[patient] = []).Add(entry);
            }
        }

        _steps.Add(new CohortStep(where, rule, _remaining.Count)
        {
            Codes = codes.Found is { } found ? [.. found.Take(CohortStep.CodesListed).Select(code => code.Token)] : null,
            CodeCount = codes.Found is null ? null : codes.Codes.Count,
            WithoutReading = withoutReading,
        });
    }

    private static string Tokens(IReadOnlyList<CohortCode> codes) => string.Join(',', codes.Select(code => code.Token));

    private static string Count(IReadOnlyList<CohortCode> codes) => codes.Count == 1 ? "1 code" : $"{codes.Count} codes";

    private static string Band(int? min, int? max) => (min, max) switch
    {
        ({ } low, { } high) => $"{low} to {high}",
        ({ } low, null) => $"{low} or older",
        _ => $"{max} or younger",
    };

    private static string Symbol(ValueComparator comparator) => comparator switch
    {
        ValueComparator.Gt => ">",
        ValueComparator.Ge => ">=",
        ValueComparator.Lt => "<",
        ValueComparator.Le => "<=",
        _ => "=",
    };
}
