// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>Builds the envelope of a run, with the rows fitted to the response budget or the file they went to described.</summary>
internal static class ViewResponse
{
    /// <summary>How many of the first rows shown have their source resource cited as evidence.</summary>
    public const int EvidenceRows = 20;

    /// <summary>Why a view has no per-row sources: the engine orders its rows across resources.</summary>
    public const string UnionAllOrder = "union-all-order";

    /// <summary>Where the call a cut table offers for every row would write; any path outside the folder will do.</summary>
    private const string WholeAnswerPath = "view-rows.csv";

    /// <summary>The response that returns rows: as many of the page as fit the budget, the rest in <c>omitted</c> and <c>next</c>.</summary>
    public static ToolEnvelope<ViewFacts?> ForRows(ViewOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        List<IssueItem> issues = [.. outcome.Issues];
        if (outcome.Paging.Offset > outcome.Pass.Total)
        {
            issues.Add(new IssueItem("warning", "offset-beyond-rows", $"offset {outcome.Paging.Offset} is past the last of the view's {outcome.Pass.Total} rows, so none are returned.", Location: null));
        }

        int take = Largest(outcome.Pass.Kept.Count, outcome.Budget, count =>
        {
            Parts measured = Assemble(outcome, count);
            return EnvelopeAssembler.MaxSize(outcome.Session, measured.Facts, measured.Evidence, measured.Omitted, [], issues, measured.Next);
        });
        Parts parts = Assemble(outcome, take);
        return EnvelopeAssembler.Create<ViewFacts?>(outcome.Session, parts.Facts, outcome.Scope.BuiltAt, parts.Evidence, parts.Omitted, [], issues, cancellationToken) with { Next = parts.Next };
    }

    /// <summary>The response of a run that wrote every row to a file: the file's facts and the first rows' sources as evidence.</summary>
    public static ToolEnvelope<ViewFacts?> ForFile(ViewOutcome outcome, ViewFileFacts file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(file);

        List<IssueItem> issues = [.. outcome.Issues];
        if (!file.Created)
        {
            string existing = File.Exists(file.Path) ? "; the file already at that path was left as it was" : string.Empty;
            issues.Add(new IssueItem("information", "out-not-created", $"The view produced no rows, so nothing was written to '{file.Path}'{existing}.", Location: null));
        }

        List<OmittedItem> omitted = [.. outcome.Omitted];
        ViewFacts facts = Facts(outcome, omitted, rows: null, file);
        return EnvelopeAssembler.Create<ViewFacts?>(outcome.Session, facts, outcome.Scope.BuiltAt, Evidence(outcome, outcome.Pass.Kept), omitted, [], issues, cancellationToken);
    }

    private readonly record struct Parts(ViewFacts Facts, List<EvidenceItem> Evidence, List<OmittedItem> Omitted, List<NextCall> Next);

    // The most rows, up to the available ones, whose envelope fits the budget; adding a row never makes an envelope smaller.
    private static int Largest(int available, int budget, Func<int, int> sizeWith)
    {
        int fits = 0;
        int tooMany = available + 1;
        while (tooMany - fits > 1)
        {
            int middle = fits + ((tooMany - fits) / 2);
            if (sizeWith(middle) <= budget)
            {
                fits = middle;
            }
            else
            {
                tooMany = middle;
            }
        }

        return fits;
    }

    private static Parts Assemble(ViewOutcome outcome, int take)
    {
        List<OmittedItem> omitted = [.. outcome.Omitted];
        List<ViewRow> shown = [.. outcome.Pass.Kept.Take(take)];
        ViewFacts facts = Facts(outcome, omitted, [.. shown.Select(row => (IReadOnlyList<object?>)row.Cells)], file: null);

        long remaining = Math.Max(0, outcome.Pass.Total - outcome.Paging.Offset) - take;
        List<NextCall> next = [];
        if (remaining > 0)
        {
            int nextOffset = outcome.Paging.Offset + take;
            omitted.Add(new OmittedItem($"{remaining} more rows (offset={nextOffset})", take < outcome.Pass.Kept.Count ? "size-budget" : "limit"));
            next = Next(outcome, nextOffset, take < outcome.Pass.Kept.Count, omitted);
        }

        return new Parts(facts, Evidence(outcome, shown), omitted, next);
    }

    private static ViewFacts Facts(ViewOutcome outcome, List<OmittedItem> omitted, IReadOnlyList<IReadOnlyList<object?>>? rows, ViewFileFacts? file = null)
    {
        ViewRequest request = outcome.Request;
        if (QueryEcho.IsCut(request.Where))
        {
            omitted.Add(QueryEcho.Cut(request.Where!, "facts.where"));
        }

        Dictionary<string, string>? variables = null;
        if (outcome.Variables.Count > 0)
        {
            variables = outcome.Variables.ToDictionary(pair => pair.Key, pair => QueryEcho.Of(pair.Value), StringComparer.Ordinal);
            omitted.AddRange(outcome.Variables.Where(pair => QueryEcho.IsCut(pair.Value)).Select(pair => QueryEcho.Cut(pair.Value, $"facts.variables.{pair.Key}")));
        }

        return new ViewFacts(
            new ViewSourceFacts(outcome.Loaded.Source, outcome.View.Resource, outcome.Loaded.Reference),
            [.. outcome.View.Columns.Select(column => new ViewColumn(column.Name, column.Type, column.Collection))],
            outcome.Pass.Total,
            outcome.Scope.Entries.Count,
            outcome.View.OrdersAcrossResources ? new ViewProvenance(null, UnionAllOrder) : new ViewProvenance(EvidenceRows, null),
            Unreadable: outcome.Unreadable > 0 ? outcome.Unreadable : null,
            Where: request.Where is null ? null : QueryEcho.Of(request.Where),
            Variables: variables,
            Compartment: request.Compartment is null ? null : QueryEcho.Of(request.Compartment),
            Offset: rows is null ? null : outcome.Paging.Offset,
            Returned: rows?.Count,
            Rows: rows,
            Distinct: outcome.Distinct,
            File: file);
    }

    // The ViewDefinition's own place first, then the source of each of the first rows shown, once.
    private static List<EvidenceItem> Evidence(ViewOutcome outcome, IEnumerable<ViewRow> shown)
    {
        List<EvidenceItem> evidence = outcome.Loaded.Evidence is { } view ? [view] : [];
        evidence.AddRange(shown
            .Take(EvidenceRows)
            .Select(row => row.Source)
            .OfType<CatalogEntry>()
            .Distinct()
            .Select(entry => new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset)));
        return evidence;
    }

    // Whole-answer calls first, as every tool does: every row in a file, then the page that follows.
    private static List<NextCall> Next(ViewOutcome outcome, int nextOffset, bool cutBySize, List<OmittedItem> omitted)
    {
        ViewRequest request = outcome.Request;
        if (QueryEcho.IsCut(request.View) || QueryEcho.IsCut(request.Where) || (request.Variables?.Any(QueryEcho.IsCut) ?? false))
        {
            omitted.Add(QueryEcho.NextLeftOut("the rest of the rows"));
            return [];
        }

        Dictionary<string, object> shared = new(StringComparer.Ordinal) { ["view"] = request.View };
        AddIfGiven(shared, "where", request.Where);
        AddIfGiven(shared, "compartment", request.Compartment);
        if (request.Variables is { Count: > 0 } variables)
        {
            shared["variables"] = variables.ToArray();
        }

        Dictionary<string, object> page = new(shared, StringComparer.Ordinal) { ["offset"] = nextOffset };
        // count 0 asks for the totals only; the page that follows is a page of rows.
        if (request.Count is > 0 and int count)
        {
            page["count"] = count;
        }

        if (request.MaxBytes is int maxBytes)
        {
            page["maxBytes"] = maxBytes;
        }

        Dictionary<string, object> everything = new(shared, StringComparer.Ordinal) { ["out"] = WholeAnswerPath };
        return
        [
            new NextCall("sql_view", everything, "every row in one file: set out to a path outside the folder (csv, ndjson or parquet by its extension), then read the file"),
            new NextCall("sql_view", page, cutBySize ? "the rows left; fewer columns or a smaller count fit more of them in one response" : "the next rows"),
        ];
    }

    private static void AddIfGiven(Dictionary<string, object> args, string name, string? value)
    {
        if (value is not null)
        {
            args[name] = value;
        }
    }
}
