// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// One FHIRPath value over a search's whole result: the expression is evaluated once over a <c>searchset</c> Bundle holding
/// every match in result order (<c>entry.resource</c>), so <c>entry.resource.valueQuantity.value.max()</c> or
/// <c>entry.count()</c> answers in one call, with the resources the value came from as evidence.
/// </summary>
internal static partial class ExpressionSearch
{
    /// <summary>The most matches one expression is evaluated over; each is read and held in memory.</summary>
    public const int MaxMatches = 5000;

    public const string FromResultElements = "result-elements";

    public const string FromMatches = "matches";

    /// <summary>
    /// The value is the number of matches an expression <c>X.count()</c> counted, each a whole match: they are listed in
    /// <c>facts.counted</c> and cited in evidence, as many as fit.
    /// </summary>
    public const string FromCounted = "counted";

    /// <summary>
    /// Most matches cited for a value computed over them (<see cref="FromMatches"/>): the query that found them is the citation,
    /// so listing every one would spend the budget on a count's provenance.
    /// </summary>
    public const int MatchEvidenceLimit = 25;

    /// <summary>Parses the expression, failing the request when it is not valid FHIRPath.</summary>
    public static Expression Compile(string expression) => FolderFhirPath.Compile("'fhirpath'", expression);

    /// <summary>The value of <paramref name="expression"/> over a Bundle of <paramref name="matches"/>, held to the budget.</summary>
    /// <exception cref="ToolRequestException">More than <see cref="MaxMatches"/> matches, or the expression fails to evaluate.</exception>
    public static ToolEnvelope<SearchFacts?> Evaluate(
        FolderSession session,
        FolderSearchSource source,
        SearchRequest request,
        ParsedSearch parsed,
        Expression expression,
        List<IndexedResource> matches,
        List<IssueItem> issues,
        int budget,
        CancellationToken cancellationToken)
    {
        if (matches.Count > MaxMatches)
        {
            throw ToolRequestException.Error(
                "too-many-matches",
                $"'fhirpath' is evaluated over every match, and this search matches {matches.Count} {request.ResourceType} resources (at most {MaxMatches}); narrow it with compartment=Patient/<id>, a code or a date range, keep one match per patient with perPatient, or use summary=count for the number alone.");
        }

        List<OmittedItem> omitted = [];
        issues.AddRange(ReferenceFollowChecks.UnresolvedReferences(source, new SearchOptions(parsed.Options) { Include = [], RevInclude = [] }, []));
        ResourceSearcher.AddDiagnostics(session, source, issues, omitted);

        var fhirPath = new FolderFhirPath(session);
        List<CatalogEntry> entries = [];
        var bundleEntries = new JsonArray();
        foreach (IndexedResource match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fhirPath.Read(match.Entry)?.Meta<JsonNode>() is JsonObject resource)
            {
                entries.Add(match.Entry);
                bundleEntries.Add(new JsonObject { ["resource"] = resource.DeepClone() });
            }
        }

        var bundle = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = "searchset",
            ["total"] = entries.Count,
            ["entry"] = bundleEntries,
        };

        IElement input = JsonSourceNodeFactory.Parse(bundle).ToElement(session.Schema);
        string subject = $"the Bundle of {entries.Count} matches";
        JsonArray value;
        List<CatalogEntry> cited;
        string valueFrom;
        List<string>? counted = null;
        if (CountedFocus(expression) is { } countedFocus)
        {
            // X.count() is evaluated as X and counted here, so that the matches it counts can be listed and cited.
            IReadOnlyList<IElement> items = fhirPath.Evaluate(input, countedFocus, "'fhirpath'", request.FhirPath!, subject);
            value = new JsonArray(items.Count);
            IReadOnlyList<CatalogEntry> resolved = fhirPath.TakeResolved();
            if (EntryPositions(items, entries.Count, wholeResources: true) is { } positions)
            {
                cited = [.. positions.Distinct().Select(position => entries[position])];
                counted = [.. cited.Select(ResourceReader.Describe)];
                valueFrom = FromCounted;
            }
            else
            {
                (cited, valueFrom) = ([.. entries.Concat(resolved).DistinctBy(entry => (entry.ResourceType, entry.Id))], FromMatches);
            }
        }
        else
        {
            IReadOnlyList<IElement> result = fhirPath.Evaluate(input, expression, "'fhirpath'", request.FhirPath!, subject);
            (cited, valueFrom) = Cited(result, entries, fhirPath.TakeResolved());
            value = FhirPathJson.Values(result);
        }

        issues.AddRange(fhirPath.Problems);
        omitted.AddRange(fhirPath.Omitted);
        issues.AddRange(UnresolvedNote(fhirPath.TakeUnresolved()));
        issues.AddRange(NameIssues.For(cited));
        List<EvidenceItem> evidence = [.. cited.Select(entry => new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset))];

        // Assembled once: the assembler hands over the session's one-time cache notices, so each candidate size is a copy of it.
        ToolEnvelope<SearchFacts?> shell = ResourceSearcher.Envelope(session, source, facts: null, [], omitted, issues, cancellationToken);
        return new Fit(shell, request, matches.Count, value, valueFrom, evidence, counted).Within(budget);
    }

    /// <summary>The <c>X</c> of an expression that is <c>X.count()</c>; null for any other expression.</summary>
    private static Expression? CountedFocus(Expression expression) =>
        expression is FunctionCallExpression { Focus: { } focus, Arguments.Count: 0 } call && call.FunctionName.Equals("count", StringComparison.OrdinalIgnoreCase)
            ? focus
            : null;

    /// <summary>
    /// The Bundle entry each item was read from, when every item was read from one (and, with <paramref name="wholeResources"/>,
    /// is that entry's resource itself rather than an element inside it); null otherwise.
    /// </summary>
    private static List<int>? EntryPositions(IReadOnlyList<IElement> items, int entryCount, bool wholeResources)
    {
        List<int> positions = [];
        foreach (IElement item in items)
        {
            string location = item.Location ?? string.Empty;
            Match entry = EntryLocation().Match(location);
            if (!entry.Success
                || (wholeResources && entry.Length != location.Length)
                || !int.TryParse(entry.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int position)
                || position >= entryCount)
            {
                return null;
            }

            positions.Add(position);
        }

        return positions;
    }

    /// <summary>
    /// The resources the result is cited by: the matches its items were read from, when every item was read from one;
    /// otherwise (a computed value, an item <c>resolve()</c> reached, or no items) every match, then each resource
    /// <c>resolve()</c> reached.
    /// </summary>
    private static (List<CatalogEntry> Cited, string ValueFrom) Cited(IReadOnlyList<IElement> result, List<CatalogEntry> entries, IReadOnlyList<CatalogEntry> resolved)
    {
        if (result.Count > 0 && EntryPositions(result, entries.Count, wholeResources: false) is { } positions)
        {
            return ([.. positions.Distinct().Select(position => entries[position])], FromResultElements);
        }

        return ([.. entries.Concat(resolved).DistinctBy(entry => (entry.ResourceType, entry.Id))], FromMatches);
    }

    private static IEnumerable<IssueItem> UnresolvedNote(IReadOnlyList<string> unresolved)
    {
        List<string> distinct = [.. unresolved.Distinct(StringComparer.Ordinal)];
        if (distinct.Count == 0)
        {
            return [];
        }

        return [new IssueItem(
            "information",
            "resolve-target-absent",
            $"resolve() found no resource in the folder for {distinct.Count} references ({string.Join(", ", distinct.Take(5).Select(FhirIds.Written))}): another server's, or a Bundle or conditional reference the index left unresolved (see unresolved-bundle-reference). They added nothing to the value.",
            Location: null)];
    }

    [GeneratedRegex(@"^Bundle\.entry\[(?<index>\d+)\]\.resource(?=\.|\[|$)", RegexOptions.CultureInvariant)]
    private static partial Regex EntryLocation();

    /// <summary>The value and its evidence cut to the budget: the value first, then as much evidence as still fits.</summary>
    private sealed class Fit(ToolEnvelope<SearchFacts?> shell, SearchRequest request, int total, JsonArray value, string valueFrom, List<EvidenceItem> evidence, List<string>? counted)
    {
        /// <summary>The value first, then as many counted references as fit, then as much evidence as still fits.</summary>
        public ToolEnvelope<SearchFacts?> Within(int budget)
        {
            int items = Largest(value.Count, shown => Build(shown, countedShown: 0, evidenceShown: 0), budget);
            int listed = Largest(counted?.Count ?? 0, shown => Build(items, shown, evidenceShown: 0), budget);
            int cap = valueFrom == FromMatches ? Math.Min(evidence.Count, MatchEvidenceLimit) : evidence.Count;
            int cited = Largest(cap, shown => Build(items, listed, shown), budget);
            return Build(items, listed, cited);
        }

        /// <summary>The largest <c>n</c> in <c>[0, count]</c> whose response fits, assuming size grows with <c>n</c>.</summary>
        private static int Largest(int count, Func<int, ToolEnvelope<SearchFacts?>> build, int budget)
        {
            int low = 0;
            int high = count;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (EnvelopeJson.SizeOf(build(middle)) <= budget)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        private ToolEnvelope<SearchFacts?> Build(int valueShown, int countedShown, int evidenceShown)
        {
            List<OmittedItem> allOmitted = [.. shell.Omitted];
            if (valueShown < value.Count)
            {
                allOmitted.Add(new OmittedItem(
                    $"{value.Count - valueShown} more items of the value (offset={valueShown}); narrow the expression, for example with .select(...), .take(n) or .skip(n)",
                    "size-budget"));
            }

            if (counted is not null && countedShown < counted.Count)
            {
                allOmitted.Add(new OmittedItem(
                    $"{counted.Count - countedShown} more counted matches are not listed in facts.counted (it lists the first {countedShown}); the same fhirpath without .count(), with .select(id), returns them all",
                    "size-budget"));
            }

            if (evidenceShown < evidence.Count)
            {
                allOmitted.Add(new OmittedItem(
                    $"{evidence.Count - evidenceShown} more resources the value was computed from are not listed in evidence (it lists the first {evidenceShown}); a search with the same query lists them",
                    evidenceShown < Math.Min(evidence.Count, valueFrom == FromMatches ? MatchEvidenceLimit : evidence.Count) ? "size-budget" : "limit"));
            }

            var facts = new SearchFacts(request.ResourceType, request.Query ?? string.Empty, request.Compartment, total, Offset: 0, Returned: 0, Resources: null)
            {
                FhirPath = request.FhirPath,
                Value = new JsonArray([.. value.Take(valueShown).Select(item => item?.DeepClone())]),
                ValueFrom = valueFrom,
                Counted = counted is null ? null : [.. counted.Take(countedShown)],
            };

            return shell with { Facts = facts, Evidence = [.. evidence.Take(evidenceShown)], Omitted = allOmitted };
        }
    }
}
