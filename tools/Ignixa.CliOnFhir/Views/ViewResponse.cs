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
            return EnvelopeAssembler.MaxSize(outcome.Session, measured.Facts, measured.Evidence, measured.Omitted, [], issues, []);
        });
        Parts parts = Assemble(outcome, take);
        return EnvelopeAssembler.Create<ViewFacts?>(outcome.Session, parts.Facts, outcome.Scope.BuiltAt, parts.Evidence, parts.Omitted, [], issues, cancellationToken);
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

    private readonly record struct Parts(ViewFacts Facts, List<EvidenceItem> Evidence, List<OmittedItem> Omitted);

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
        if (remaining > 0)
        {
            int nextOffset = outcome.Paging.Offset + take;
            omitted.Add(new OmittedItem($"{remaining} more rows (offset={nextOffset})", take < outcome.Pass.Kept.Count ? "size-budget" : "limit"));
            omitted.Add(new OmittedItem(
                $"next: no call is listed, because the sql_view tool does not exist yet and the view verb is CLI only; run the same view with offset {nextOffset} for the following rows, or with out set to a path outside the folder for every row",
                "limit"));
        }

        return new Parts(facts, Evidence(outcome, shown), omitted);
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
            outcome.AsOf,
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
}
