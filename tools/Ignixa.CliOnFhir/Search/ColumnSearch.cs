// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// A search page as a table: each matching resource becomes one row of FHIRPath values (<c>columns</c>), keyed by the resource
/// so it stays citable, within the response budget.
/// </summary>
internal static class ColumnSearch
{
    /// <summary>The first column of every row: the row's resource, as evidence names it.</summary>
    public const string KeyColumn = "ref";

    /// <summary>Rows per page when the caller gives no count; rows are small, so far more fit than resources.</summary>
    public const int DefaultRows = 50;

    public const int MaxRows = 200;

    public const int MaxColumns = 20;

    /// <summary>
    /// The path of a column holding each row's record kind (<see cref="FolderKinds"/>'s short name) instead of a FHIRPath value:
    /// the profile, or the class or category, that tells the type's kinds apart.
    /// </summary>
    public const string KindPath = "%kind";

    /// <summary>Parses every column, failing the request on the first that is malformed or not valid FHIRPath.</summary>
    /// <exception cref="ToolRequestException">No columns, too many, a blank, repeated or reserved name, or an invalid expression.</exception>
    public static IReadOnlyList<CompiledColumn> Compile(IReadOnlyList<SearchColumn> columns)
    {
        if (columns.Count == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", "'columns' is empty; give at least one name and FHIRPath, for example {\"date\":\"authoredOn\"}.");
        }

        if (columns.Count > MaxColumns)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'columns' has {columns.Count} columns; at most {MaxColumns} are allowed.");
        }

        HashSet<string> names = new(StringComparer.Ordinal) { KeyColumn };
        List<CompiledColumn> compiled = [];
        foreach (SearchColumn column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw ToolRequestException.Error("invalid-parameter", "A column in 'columns' has a blank name.");
            }

            if (!names.Add(column.Name))
            {
                throw ToolRequestException.Error(
                    "invalid-parameter",
                    column.Name == KeyColumn
                        ? $"'{KeyColumn}' is the name of the key column every row starts with (the row's resource); name the column something else."
                        : $"'columns' names '{column.Name}' twice.");
            }

            compiled.Add(IsKind(column.Path)
                ? new CompiledColumn(column.Name, column.Path, Expression: null)
                : new CompiledColumn(column.Name, column.Path, FolderFhirPath.Compile($"Column '{column.Name}'", column.Path)));
        }

        return compiled;
    }

    /// <summary>
    /// The rows of the page <c>[offset, offset + count)</c> of <paramref name="matches"/>, as many as fit the budget. Every row
    /// of the page is evaluated first, so an expression that fails on any of them fails the call.
    /// </summary>
    public static ToolEnvelope<SearchFacts?> Page(
        FolderSession session,
        FolderSearchSource source,
        SearchRequest request,
        ParsedSearch parsed,
        IReadOnlyList<CompiledColumn> columns,
        List<IndexedResource> matches,
        int count,
        List<IssueItem> issues,
        int budget,
        int? largerBudget,
        CancellationToken cancellationToken)
    {
        List<IndexedResource> page = [.. matches.Skip(request.Offset).Take(count)];
        List<OmittedItem> omitted = [];
        issues.AddRange(ReferenceFollowChecks.UnresolvedReferences(source, parsed.Options, page));
        ResourceSearcher.AddDiagnostics(session, source, issues, omitted);
        issues.AddRange(NameIssues.For(page.Select(match => match.Entry)));

        var fhirPath = new FolderFhirPath(session);
        List<Row?> rows = [];
        foreach (IndexedResource match in page)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(Evaluate(fhirPath, session.Kinds, columns, match.Entry));
        }

        if (page.Count == 0)
        {
            IElement empty = fhirPath.Empty(request.ResourceType);
            foreach (CompiledColumn column in columns.Where(column => column.Expression is not null))
            {
                _ = fhirPath.Evaluate(empty, column.Expression!, $"Column '{column.Name}'", column.Path, $"an empty {request.ResourceType}");
            }
        }

        issues.AddRange(fhirPath.Problems);
        omitted.AddRange(fhirPath.Omitted);

        // Assembled once: the assembler hands over the session's one-time cache notices, so each candidate size is a copy of it.
        ToolEnvelope<SearchFacts?> shell = ResourceSearcher.Envelope(session, source, facts: null, [], omitted, issues, cancellationToken);
        return new Table(shell, request, columns, matches.Count, rows, budget, largerBudget).Fit();
    }

    /// <summary>The row of <paramref name="entry"/>, or null when it cannot be read now (the reason is in the reader's problems).</summary>
    private static Row? Evaluate(FolderFhirPath fhirPath, FolderKinds kinds, IReadOnlyList<CompiledColumn> columns, CatalogEntry entry)
    {
        if (fhirPath.Read(entry) is not { } resource)
        {
            return null;
        }

        string reference = ResourceReader.Describe(entry);
        var cells = new JsonArray(JsonValue.Create(reference));
        List<int> empty = [];
        for (int index = 0; index < columns.Count; index++)
        {
            CompiledColumn column = columns[index];
            if (column.Expression is null)
            {
                cells.Add(JsonValue.Create(kinds.KindOf(entry).ShortName));
                continue;
            }

            IReadOnlyList<IElement> values = fhirPath.Evaluate(resource, column.Expression, $"Column '{column.Name}'", column.Path, reference);
            if (values.Count == 0)
            {
                empty.Add(index);
            }

            cells.Add(FhirPathJson.Cell(values));
        }

        return new Row(entry, cells, empty, fhirPath.TakeResolved(), fhirPath.TakeUnresolved());
    }

    private static bool IsKind(string path) => string.Equals(path.Trim(), KindPath, StringComparison.Ordinal);

    /// <summary>A column after parsing; a <see cref="KindPath"/> column has no expression.</summary>
    public sealed record CompiledColumn(string Name, string Path, Expression? Expression);

    /// <summary>One evaluated row: its resource, its cells (key first), which columns are empty and what <c>resolve()</c> reached or missed.</summary>
    private sealed record Row(CatalogEntry Entry, JsonArray Cells, IReadOnlyList<int> EmptyColumns, IReadOnlyList<CatalogEntry> Resolved, IReadOnlyList<string> Unresolved);

    /// <summary>The evaluated page and how much of it one response holds.</summary>
    private sealed class Table(ToolEnvelope<SearchFacts?> shell, SearchRequest request, IReadOnlyList<CompiledColumn> columns, int total, List<Row?> rows, int budget, int? largerBudget)
    {
        /// <summary>
        /// The largest prefix of the page whose whole response fits the budget. A row too large to fit even alone is named in
        /// <c>omitted</c> and passed over, so paging moves past it instead of stalling on it.
        /// </summary>
        public ToolEnvelope<SearchFacts?> Fit()
        {
            List<OmittedItem> skipped = [];
            List<Row?> kept = [.. rows];

            // The next calls repeat the request whole, so ones that would not fit even beside no row are left out.
            _noNext = !SearchNarrowing.Repeatable(request) || !Fits(Build(kept, 0, skipped));
            while (true)
            {
                if (Fits(Build(kept, kept.Count, skipped)))
                {
                    return Build(kept, kept.Count, skipped);
                }

                int low = 0;
                int high = kept.Count - 1;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (Fits(Build(kept, middle, skipped)))
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle - 1;
                    }
                }

                int first = kept.FindIndex(row => row is not null);
                if (low > first || first < 0)
                {
                    return Build(kept, low, skipped);
                }

                // Nothing fits before the first readable row, so that row is too large on its own; pass over it and fit again.
                Row large = kept[first]!;
                skipped.Add(new OmittedItem(
                    $"the row of {ResourceReader.Describe(large.Entry)} is {EnvelopeJson.SizeOf(large.Cells)} bytes and exceeds the size budget; select smaller values (a coding's code rather than the CodeableConcept) or read it with read_resources",
                    "size-budget"));
                kept[first] = null;
            }
        }

        private bool _noNext;

        private bool Fits(ToolEnvelope<SearchFacts?> envelope) => EnvelopeJson.SizeOf(envelope) <= budget;

        /// <summary>The response holding the first <paramref name="taken"/> page positions of <paramref name="kept"/>.</summary>
        private ToolEnvelope<SearchFacts?> Build(List<Row?> kept, int taken, List<OmittedItem> skipped)
        {
            List<Row> shown = [.. kept.Take(taken).OfType<Row>()];
            int resume = request.Offset + taken;
            bool cut = taken < kept.Count;

            List<EvidenceItem> evidence = [];
            HashSet<string> cited = new(StringComparer.Ordinal);
            foreach (CatalogEntry entry in shown.SelectMany(row => (IEnumerable<CatalogEntry>)[row.Entry, .. row.Resolved]))
            {
                string reference = ResourceReader.Describe(entry);
                if (cited.Add(reference))
                {
                    evidence.Add(new EvidenceItem(reference, entry.FilePath, entry.Line, entry.Offset));
                }
            }

            Dictionary<string, int> emptyCells = new(StringComparer.Ordinal);
            foreach (int index in shown.SelectMany(row => row.EmptyColumns))
            {
                emptyCells[columns[index].Name] = emptyCells.GetValueOrDefault(columns[index].Name) + 1;
            }

            List<IssueItem> allIssues = [.. shell.Issues, .. EmptyColumnWarnings(shown.Count, emptyCells), .. UnresolvedNote(shown)];
            List<OmittedItem> allOmitted = [.. shell.Omitted, .. skipped];
            if (QueryEcho.IsCut(request.Query))
            {
                allOmitted.Add(QueryEcho.Cut(request.Query!, "facts.query"));
            }

            if (resume < total)
            {
                string resumeWith = _noNext ? $"offset={resume}; no next call, as the request is too long to repeat in one" : $"offset={resume}";
                allOmitted.Add(new OmittedItem($"{total - resume} more matches ({resumeWith})", cut ? "size-budget" : "limit"));
            }

            var facts = new SearchFacts(request.ResourceType, QueryEcho.Of(request.Query ?? string.Empty), request.Compartment is null ? null : QueryEcho.Of(request.Compartment), total, request.Offset, shown.Count, Resources: null)
            {
                Columns = [KeyColumn, .. columns.Select(column => column.Name)],
                Rows = [.. shown.Select(row => row.Cells)],
                EmptyCells = emptyCells.Count == 0 ? null : emptyCells,
            };

            List<NextCall> next = resume < total && !_noNext ? SearchNarrowing.After(request, total, resume, cut, largerBudget) : [];
            return shell with { Facts = facts, Evidence = evidence, Omitted = allOmitted, Issues = allIssues, Next = next };
        }

        /// <summary>A warning for each column with no value in any returned row: most often a misspelt path.</summary>
        private IEnumerable<IssueItem> EmptyColumnWarnings(int shown, Dictionary<string, int> emptyCells) =>
            shown == 0
                ? []
                : emptyCells.Where(column => column.Value == shown).Select(column => new IssueItem(
                    "warning",
                    "column-empty",
                    $"Column '{column.Key}' ({columns.First(compiled => compiled.Name == column.Key).Path}) has no value in any of the {shown} rows; check the path (names are case-sensitive, a choice element is value.ofType(Quantity) or valueQuantity, a reference is followed with .resolve()).",
                    Location: null));

        /// <summary>A note naming the references <c>resolve()</c> could not find in the folder, whose cells are therefore empty.</summary>
        private static IEnumerable<IssueItem> UnresolvedNote(List<Row> shown)
        {
            List<string> unresolved = [.. shown.SelectMany(row => row.Unresolved).Distinct(StringComparer.Ordinal)];
            if (unresolved.Count == 0)
            {
                return [];
            }

            return [new IssueItem(
                "information",
                "resolve-target-absent",
                $"resolve() found no resource in the folder for {unresolved.Count} references in the returned rows ({string.Join(", ", unresolved.Take(5).Select(FhirIds.Written))}): another server's, or a Bundle or conditional reference the index left unresolved (see unresolved-bundle-reference). What they would have given is empty.",
                Location: null)];
        }
    }
}
