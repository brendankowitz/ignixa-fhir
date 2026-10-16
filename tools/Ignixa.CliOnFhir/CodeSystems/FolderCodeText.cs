// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// <c>folder_code_systems</c> with <c>text</c>: the codes the folder's resources carry whose display matches some words, so a
/// clinician's category (an ICD-10 chapter, "any diabetes", "old MI") can be turned into the codes the data holds. It reads the
/// token entries of the search indexes (as <see cref="FolderTerminology"/> does), whose text is the display written in the
/// data; the displays and designations of terminology already loaded in the session are matched too, never loaded here. No
/// terminology server is used.
/// </summary>
internal static class FolderCodeText
{
    public const int DefaultCount = 25;

    private const int MaxNextCodes = 50;

    private static readonly string[] ActiveStatuses = ["active", "recurrence", "relapse"];

    private static readonly char[] WordSeparators = ['|', ',', ' ', '\t', '\r', '\n'];

    private static readonly (string Type, string Argument)[] CohortTypes = [("Condition", "conditions"), ("MedicationRequest", "medications"), ("Immunization", "immunizations")];

    /// <param name="patient">A Patient id: only the resources of its compartment are read; null reads the whole folder.</param>
    /// <exception cref="ToolRequestException">No usable word, an unknown resource type or patient, or a bad page.</exception>
    public static ToolEnvelope<CodeTextFacts?> Search(FolderSession session, string text, string? resourceType, string? system, string? patient, int? count, int offset, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count ?? DefaultCount, offset, issues);
        IReadOnlyList<string> words = Words(text);
        if (resourceType is not null && !session.Schema.ResourceTypeNames.Contains(resourceType))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'resourceType' {resourceType} is not a {session.Schema.Version} resource type.");
        }

        List<string> types = [.. session.Catalog.Entries
            .Select(entry => entry.ResourceType)
            .Where(type => resourceType is null || type == resourceType)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        TerminologyState? terminology = session.Terminology.Current is { IsUnusable: false } loaded ? loaded : null;
        IReadOnlySet<(string Type, string Id)>? scope = patient is null ? null : CompartmentOf(session, patient);

        Dictionary<(string System, string Code), CodeTally> tallies = ReadTallies(session, types, system, scope, cancellationToken);
        List<CodeMatch> matches = [.. tallies
            .Select(pair => new CodeMatch(pair.Key, pair.Value, StoreConcept(terminology, pair.Key)))
            .Where(match => StartsAWord(match.Display, words) || (match.Concept?.Designations.Any(designation => StartsAWord(designation.Value, words)) ?? false))
            .OrderByDescending(match => match.Tally.Resources)
            .ThenBy(match => match.Key.System, StringComparer.Ordinal)
            .ThenBy(match => match.Key.Code, StringComparer.Ordinal)];

        List<CodeMatch> page = [.. matches.Skip(offset).Take(pageSize)];
        Dictionary<(string System, string Code), int> patients = PatientsPerCode(session, types, [.. page.Select(match => match.Key)], scope, cancellationToken);
        List<CodeTextMatch> rows = [.. page.Select(match => new CodeTextMatch(
            CodeAliases.Of(match.Key.System) + "|" + match.Key.Code,
            match.Display is { } display ? display[..Math.Min(display.Length, TermText.MaxShortText)] : null,
            [.. match.Tally.Types.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}")],
            match.Tally.Resources,
            patients.GetValueOrDefault(match.Key),
            match.Tally.Types.ContainsKey("Condition") ? match.Tally.Active : null))];

        issues.AddRange(Paging.BeyondTotal(offset, matches.Count, "matching codes"));
        List<AbsentItem> absent = matches.Count == 0
            ? [new AbsentItem("codes whose display matches the words", $"{tallies.Count} distinct codes of {string.Join(", ", types)} in {(patient is null ? "the folder's" : $"Patient/{FhirIds.Safe(patient)}'s")} search indexes{(terminology is null ? string.Empty : " and the displays and designations of the loaded terminology")}", session.Catalog.BuiltAt)]
            : [];

        // Assembled once: the assembler hands over the session's one-time cache notices, so each candidate size is a copy of it.
        ToolEnvelope<CodeTextFacts?> shell = EnvelopeAssembler.Create<CodeTextFacts?>(session, null, session.Catalog.BuiltAt, [], [], absent, issues, cancellationToken);
        ToolEnvelope<CodeTextFacts?> Build(int kept)
        {
            List<OmittedItem> omitted = [];
            List<NextCall> next = [];
            int remaining = matches.Count - offset - kept;
            if (remaining > 0)
            {
                omitted.Add(new OmittedItem($"{remaining} more matching codes", kept < page.Count ? "size-budget" : "limit"));
                Dictionary<string, object> args = new() { ["text"] = text, ["offset"] = offset + kept, ["count"] = pageSize };
                if (resourceType is not null)
                {
                    args["resourceType"] = resourceType;
                }

                if (system is not null)
                {
                    args["system"] = system;
                }

                if (patient is not null)
                {
                    args["patient"] = patient;
                }

                next.Add(new NextCall("folder_code_systems", args));
            }

            // A cohort over one patient's codes counts that patient alone, so only a folder-wide search suggests one.
            if (patient is null && CohortCall([.. page.Take(kept)]) is { } cohort)
            {
                next.Add(cohort);
            }

            var facts = new CodeTextFacts(words, matches.Count, offset, kept, SystemsOf(page.Take(kept).Select(match => match.Key.System)), [.. rows.Take(kept)]);
            return shell with { Facts = facts, Omitted = omitted, Next = next };
        }

        int budget = Math.Min(EnvelopeJson.DefaultBudget, session.MaxResponseBytes);
        int low = 0;
        int high = page.Count;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (EnvelopeJson.SizeOf(Build(middle)) <= budget)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Build(low);
    }

    /// <summary>The words of <paramref name="text"/>, split at '|', ',' and whitespace and folded; at least one.</summary>
    private static List<string> Words(string text)
    {
        List<string> words = [.. text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Select(TextFolding.Fold).Distinct(StringComparer.Ordinal)];
        return words.Count > 0
            ? words
            : throw ToolRequestException.Error("invalid-parameter", "'text' has no word to match: give words or alternatives such as 'diabet|glyc|kidney'.");
    }

    private static Concept? StoreConcept(TerminologyState? terminology, (string System, string Code) key) =>
        terminology?.Store.ResolveCodeSystem(key.System, version: null).Entry?.Find(key.Code);

    /// <summary>Whether a word of <paramref name="text"/> starts with any of the folded <paramref name="words"/>.</summary>
    private static bool StartsAWord(string? text, IReadOnlyList<string> words)
    {
        if (text is null)
        {
            return false;
        }

        string folded = TextFolding.Fold(text);
        int start = -1;
        for (int index = 0; index <= folded.Length; index++)
        {
            bool inWord = index < folded.Length && char.IsLetterOrDigit(folded[index]);
            if (inWord && start < 0)
            {
                start = index;
            }
            else if (!inWord && start >= 0)
            {
                ReadOnlySpan<char> word = folded.AsSpan(start, index - start);
                foreach (string wanted in words)
                {
                    if (word.StartsWith(wanted, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                start = -1;
            }
        }

        return false;
    }

    /// <summary>The Patient and the members of its compartment, as (type, id).</summary>
    private static HashSet<(string Type, string Id)> CompartmentOf(FolderSession session, string patient)
    {
        PatientCompartment compartment = PatientCompartment.Load(session, patient);
        return [(compartment.Patient.ResourceType, compartment.Patient.Id), .. compartment.Members.Select(member => (member.ResourceType, member.Id))];
    }

    private static bool InScope(IReadOnlySet<(string Type, string Id)>? scope, IndexedResource resource) =>
        scope is null || scope.Contains((resource.Entry.ResourceType, resource.Entry.Id));

    private static Dictionary<(string System, string Code), CodeTally> ReadTallies(FolderSession session, List<string> types, string? system, IReadOnlySet<(string Type, string Id)>? scope, CancellationToken cancellationToken)
    {
        Dictionary<(string System, string Code), CodeTally> tallies = [];
        foreach (string type in types)
        {
            foreach (IndexedResource resource in session.GetIndex(type).Resources.Where(resource => InScope(scope, resource)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<(string System, string Code), string?> codes = [];
                bool active = false;
                foreach (SearchIndexEntry entry in resource.Index)
                {
                    if (entry.Value is not TokenSearchValue token || FolderTerminology.OverIdentifier(entry.SearchParameter))
                    {
                        continue;
                    }

                    if (entry.SearchParameter.Code == "clinical-status" && token.Code is { } status && ActiveStatuses.Contains(status, StringComparer.Ordinal))
                    {
                        active = true;
                    }

                    if (token.System is { Length: > 0 } tokenSystem && token.Code is { Length: > 0 } code && (system is null || tokenSystem == system || CodeAliases.Of(tokenSystem) == system))
                    {
                        (string, string) key = (tokenSystem, code);
                        codes[key] = codes.GetValueOrDefault(key) is { Length: > 0 } known ? known : token.Text;
                    }
                }

                foreach (((string System, string Code) key, string? display) in codes)
                {
                    if (!tallies.TryGetValue(key, out CodeTally? tally))
                    {
                        tally = new CodeTally();
                        tallies[key] = tally;
                    }

                    tally.Add(type, display, active);
                }
            }
        }

        return tallies;
    }

    /// <summary>Per code, the patients whose compartment holds a resource carrying it.</summary>
    private static Dictionary<(string System, string Code), int> PatientsPerCode(FolderSession session, List<string> types, List<(string System, string Code)> codes, IReadOnlySet<(string Type, string Id)>? scope, CancellationToken cancellationToken)
    {
        Dictionary<(string System, string Code), int> counts = [];
        if (codes.Count == 0)
        {
            return counts;
        }

        HashSet<(string System, string Code)> wanted = [.. codes];
        Dictionary<(string Type, string Id), List<(string System, string Code)>> carriers = [];
        foreach (string type in types)
        {
            foreach (IndexedResource resource in session.GetIndex(type).Resources.Where(resource => InScope(scope, resource)))
            {
                List<(string System, string Code)> carried = [];
                foreach (SearchIndexEntry entry in resource.Index)
                {
                    if (entry.Value is TokenSearchValue { System: { Length: > 0 } system, Code: { Length: > 0 } code }
                        && !FolderTerminology.OverIdentifier(entry.SearchParameter)
                        && wanted.Contains((system, code))
                        && !carried.Contains((system, code)))
                    {
                        carried.Add((system, code));
                    }
                }

                if (carried.Count > 0)
                {
                    carriers[(type, resource.Entry.Id)] = carried;
                }
            }
        }

        foreach (IReadOnlyList<CatalogEntry> members in session.PatientCompartments.Members.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach ((string System, string Code) key in members
                .SelectMany(member => carriers.GetValueOrDefault((member.ResourceType, member.Id)) ?? [])
                .Distinct())
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts;
    }

    private static Dictionary<string, string> SystemsOf(IEnumerable<string> systems) =>
        systems.Distinct(StringComparer.Ordinal).ToDictionary(CodeAliases.Of, system => system, StringComparer.Ordinal);

    // The page's matching codes of its most common cohort-able type, for the caller to prune; built from structure only.
    private static NextCall? CohortCall(List<CodeMatch> page)
    {
        foreach ((string type, string argument) in CohortTypes.OrderByDescending(cohortType => page.Count(match => match.Tally.Types.ContainsKey(cohortType.Type))))
        {
            List<string> codes = [.. page.Where(match => match.Tally.Types.ContainsKey(type)).Take(MaxNextCodes).Select(match => $"{match.Key.System}|{match.Key.Code}")];
            if (codes.Count > 0)
            {
                return new NextCall(
                    "patient_cohort",
                    new Dictionary<string, object> { [argument] = new List<object> { new Dictionary<string, object> { ["codes"] = codes } } },
                    $"Counts patients with any of these {type} codes; drop the ones that do not belong first.");
            }
        }

        return null;
    }

    private sealed record CodeMatch((string System, string Code) Key, CodeTally Tally, Concept? Concept)
    {
        public string? Display => Tally.Display ?? Concept?.Display;
    }
}
