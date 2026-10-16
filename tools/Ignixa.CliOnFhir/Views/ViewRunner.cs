// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.Serialization;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Runs a SQL-on-FHIR ViewDefinition over the folder (the <c>sql_view</c> tool of the design, CLI <c>view</c>). The input is
/// the folder's resources of the view's type, read from the catalog by their recorded locations; the output is rows in the
/// response (capped, with the total and what was left out) or every row in a file outside the folder.
/// </summary>
public static class ViewRunner
{
    /// <summary>
    /// Runs the view <paramref name="request"/> names. A request that cannot be answered (an invalid ViewDefinition, an output
    /// path in the folder, an unknown compartment) is an envelope with null facts and an <c>error</c> issue.
    /// </summary>
    public static async Task<ToolEnvelope<ViewFacts?>> RunAsync(FolderSession session, ViewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<ViewFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return await ExecuteAsync(session, request, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<ViewFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static async Task<ToolEnvelope<ViewFacts?>> ExecuteAsync(FolderSession session, ViewRequest request, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        List<OmittedItem> omitted = [];
        ViewPaging paging = ViewPaging.Resolve(request, issues);
        OutputTarget? target = OutputTarget.Resolve(session, request);
        int budget = ResponseBudget.Resolve(request.MaxBytes, session.MaxResponseBytes, issues);

        LoadedView loaded = ViewLocator.Resolve(session, request.View);
        CompiledView view = ViewCompiler.Compile(session, loaded, request.Where);
        IReadOnlyDictionary<string, string> variables = ViewVariables.Parse(request.Variables);
        issues.AddRange(ViewVariables.Undeclared(variables, view.Expression));
        issues.AddRange(ViewIssues.ResolveUse(loaded.Json));
        int? distinctColumn = DistinctColumn(view, request.Distinct);
        ViewScope scope = ViewScope.Resolve(session, view.Resource, request.Compartment, issues, omitted);

        var elements = new ResourceElements(session);
        var evaluation = new ViewEvaluation(view, variables, elements, JsonSourceNodeFactory.Parse(new JsonObject { ["resourceType"] = view.Resource }).ToElement(session.Schema));
        evaluation.CheckVariables();
        if (scope.Entries.Count == 0)
        {
            evaluation.EvaluateOnEmpty(loaded.FromFolder);
        }

        (ViewPass pass, ViewFileFacts? file) = await PassAsync(evaluation, scope, loaded, view, paging, distinctColumn, target, cancellationToken);
        issues.AddRange(EmptyColumnIssues(view, pass));
        issues.AddRange(NameIssues.For(scope.Entries));
        issues.AddRange(elements.Problems);
        var outcome = new ViewOutcome(
            session,
            request,
            loaded,
            view,
            scope,
            pass,
            paging,
            budget,
            variables,
            evaluation.Unreadable,
            distinctColumn is int index ? new DistinctFacts(view.Columns[index].Name, pass.DistinctValues, pass.DistinctNulls) : null,
            issues,
            omitted);
        return file is null ? ViewResponse.ForRows(outcome, cancellationToken) : ViewResponse.ForFile(outcome, file, cancellationToken);
    }

    // One pass over the rows. With an output file every row is written, and a run that fails leaves no half-written file.
    private static async Task<(ViewPass Pass, ViewFileFacts? File)> PassAsync(
        ViewEvaluation evaluation, ViewScope scope, LoadedView loaded, CompiledView view, ViewPaging paging, int? distinctColumn, OutputTarget? target, CancellationToken cancellationToken)
    {
        IEnumerable<ViewRow> rows = evaluation.Rows(scope.Entries, loaded.FromFolder);
        if (target is null)
        {
            return (await ViewPass.RunAsync(rows, view.Columns.Count, paging, distinctColumn, sink: null, cancellationToken), null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
        ViewFileSink sink = ViewFileSink.Create(target, view.Columns);
        bool completed = false;
        try
        {
            // The rows kept are the first ones, which evidence cites.
            ViewPass pass = await ViewPass.RunAsync(rows, view.Columns.Count, new ViewPaging(ViewResponse.EvidenceRows, Offset: 0), distinctColumn, sink, cancellationToken);
            long bytes = await sink.CompleteAsync(cancellationToken);
            completed = true;
            return (pass, new ViewFileFacts(target.Path, target.Format, sink.Rows, bytes, Created: sink.Rows > 0));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{target.Path}' failed: {ex.Message}. The partial file was removed.");
        }
        finally
        {
            await sink.DisposeAsync();
            if (!completed)
            {
                File.Delete(target.Path);
            }
        }
    }

    // A cell holds a primitive: the engine gives a complex element (a name, a CodeableConcept) as null, as it does a misspelt path,
    // and either would otherwise read as data that is not there.
    private static IEnumerable<IssueItem> EmptyColumnIssues(CompiledView view, ViewPass pass)
    {
        IReadOnlyList<int> empty = pass.EmptyColumns;
        if (empty.Count == 0)
        {
            yield break;
        }

        const int Named = 10;
        string names = string.Join(", ", empty.Take(Named).Select(index => ViewIssues.Word(view.Columns[index].Name)));
        string more = empty.Count > Named ? $" and {empty.Count - Named} more" : string.Empty;
        yield return new IssueItem(
            "warning",
            "column-empty",
            $"{empty.Count} column(s) are null in every one of the view's {pass.Total} rows: {names}{more}. A path may be misspelt, or yield a complex element (a HumanName or CodeableConcept), which a cell cannot hold: select a primitive such as name.first().family or code.coding.first().code.",
            Location: null);
    }

    private static int? DistinctColumn(CompiledView view, string? column)
    {
        if (column is null)
        {
            return null;
        }

        int index = view.Columns.ToList().FindIndex(candidate => candidate.Name == column);
        return index >= 0
            ? index
            : throw ToolRequestException.Error("invalid-parameter", $"'distinct' names '{QueryEcho.Of(column)}', which is not a column of the view; its columns are {string.Join(", ", view.Columns.Select(candidate => ViewIssues.Word(candidate.Name)))}.");
    }
}
