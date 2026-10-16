// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Runs a SQL-on-FHIR ViewDefinition over the folder (the <c>sql_view</c> tool of the design, CLI <c>view</c>). The input is
/// the folder's resources of the view's type, read from the catalog by their recorded locations; the output is rows in the
/// response (capped, with the total and what was left out) or every row in a file outside the folder.
/// </summary>
public static class ViewRunner
{
    /// <summary>How long a run may take unless the call says; like the FML transform's default.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The longest timeout a call may ask for.</summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>The most resources a run reads unless the call says; like the cohort tool's limit.</summary>
    public const int DefaultMaxResources = 50_000;

    private const string BudgetAlternatives = "count 0 with distinct for a count over every row, out to write every row to a file, fewer columns in the view";

    /// <summary>
    /// Runs the view <paramref name="request"/> names. A request that cannot be answered (an invalid ViewDefinition, an output
    /// path in the folder, an unknown compartment, a run that takes too long) is an envelope with null facts and an
    /// <c>error</c> issue. A failure of the tool itself is the same, as <c>view-internal-error</c>, and is logged in full.
    /// </summary>
    /// <param name="logger">Receives the details of a <c>view-internal-error</c>.</param>
    public static Task<ToolEnvelope<ViewFacts?>> RunAsync(FolderSession session, ViewRequest request, CancellationToken cancellationToken, ILogger? logger = null) =>
        RunAsync(session, request, timeout: null, logger, cancellationToken);

    /// <param name="timeout">How long the run may take; null for the request's own, in seconds (a test passes a span no real run keeps).</param>
    internal static async Task<ToolEnvelope<ViewFacts?>> RunAsync(FolderSession session, ViewRequest request, TimeSpan? timeout, ILogger? logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<ViewFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return await WithinTimeAsync(session, request, timeout, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<ViewFacts>(session, ex.Issue, cancellationToken);
        }
#pragma warning disable CA1031 // The boundary of the verb: anything unexpected is logged in full and reported as an error, never as a success.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger?.LogError(ex, "The view run failed unexpectedly.");
            return EnvelopeAssembler.Failure<ViewFacts>(
                session,
                new IssueItem("error", "view-internal-error", $"The view run failed unexpectedly with {ex.GetType().Name} (digest {FhirIds.Digest(ex.Message)}); this is a defect of the tool, not of the request, and its details are in the log on stderr.", Location: null),
                cancellationToken);
        }
    }

    private static async Task<ToolEnvelope<ViewFacts?>> WithinTimeAsync(FolderSession session, ViewRequest request, TimeSpan? limit, CancellationToken cancellationToken)
    {
        int seconds = request.TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (seconds is < 1 or > MaxTimeoutSeconds)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'timeout' must be 1 to {MaxTimeoutSeconds} seconds, not {seconds}.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit ?? TimeSpan.FromSeconds(seconds));
        try
        {
            return await ExecuteAsync(session, request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw ToolRequestException.Error("view-timeout", $"The view did not finish within {limit?.TotalSeconds ?? seconds} seconds and was stopped. Limit it to one patient's resources with compartment Patient/<id>, or raise the timeout (up to {MaxTimeoutSeconds}).");
        }
    }

    private static async Task<ToolEnvelope<ViewFacts?>> ExecuteAsync(FolderSession session, ViewRequest request, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        List<OmittedItem> omitted = [];
        ViewPaging paging = ViewPaging.Resolve(request, issues);
        OutputTarget? target = OutputTarget.Resolve(session, request);
        int budget = ResponseBudget.Resolve(request.MaxBytes, session.MaxResponseBytes, issues, BudgetAlternatives);

        LoadedView loaded = ViewLocator.Resolve(session, request.View);
        CompiledView view = ViewCompiler.Compile(session, loaded, request.Where);
        IReadOnlyDictionary<string, string> variables = ViewVariables.Parse(request.Variables);
        issues.AddRange(ViewVariables.Undeclared(variables, view.Expression));
        issues.AddRange(ViewIssues.ResolveUse(loaded.Json));
        int? distinctColumn = DistinctColumn(view, request.Distinct);
        ViewScope scope = ViewScope.Resolve(session, view.Resource, request.Compartment, issues, omitted);
        CheckResourceLimit(request, view, scope);

        var evaluation = new ViewEvaluation(session, view, variables, JsonSourceNodeFactory.Parse(new JsonObject { ["resourceType"] = view.Resource }).ToElement(session.Schema));
        evaluation.CheckVariables();
        if (scope.Entries.Count == 0)
        {
            evaluation.EvaluateOnEmpty(loaded.FromFolder);
        }

        (ViewPass pass, ViewFileFacts? file) = await PassAsync(session, evaluation, scope, loaded, view, paging, distinctColumn, target, cancellationToken);
        issues.AddRange(EmptyColumnIssues(view, pass));
        issues.AddRange(NameIssues.For(scope.Entries));
        issues.AddRange(evaluation.Problems);
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

    private static void CheckResourceLimit(ViewRequest request, CompiledView view, ViewScope scope)
    {
        int limit = request.MaxResources ?? DefaultMaxResources;
        if (limit < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'maxResources' must be 1 or more, not {limit}.");
        }

        if (scope.Entries.Count > limit)
        {
            throw ToolRequestException.Error("too-many-resources", $"The view would read {scope.Entries.Count} {view.Resource} resources, more than the limit of {limit}, and every one is read into memory to evaluate it. Limit it to one patient's resources with compartment Patient/<id>, or raise maxResources.");
        }
    }

    // One pass over the rows. With an output file every row is written to a temporary file beside it, and only a complete,
    // non-empty result is renamed over the target; a run that fails or produces nothing removes what it created and touches nothing else.
    private static async Task<(ViewPass Pass, ViewFileFacts? File)> PassAsync(
        FolderSession session, ViewEvaluation evaluation, ViewScope scope, LoadedView loaded, CompiledView view, ViewPaging paging, int? distinctColumn, OutputTarget? target, CancellationToken cancellationToken)
    {
        IEnumerable<ViewRow> rows = evaluation.Rows(scope.Entries, loaded.FromFolder, cancellationToken);
        if (target is null)
        {
            return (await ViewPass.RunAsync(rows, view.Columns.Count, paging, distinctColumn, sink: null, cancellationToken), null);
        }

        OutputFile file = target.Begin();
        ViewFileSink sink = ViewFileSink.Create(file.TempPath, target.Format, view.Columns);
        ViewPass pass;
        long bytes;
        bool written = false;
        try
        {
            // The rows kept are the first ones, which evidence cites.
            pass = await ViewPass.RunAsync(rows, view.Columns.Count, new ViewPaging(ViewResponse.EvidenceRows, Offset: 0), distinctColumn, sink, cancellationToken);
            bytes = await sink.CompleteAsync(cancellationToken);
            written = sink.Rows > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{target.Path}' failed: {ex.Message}. The partial file was removed.");
        }
        finally
        {
            await sink.DisposeAsync();
            if (!written)
            {
                file.Discard();
            }
        }

        if (written)
        {
            try
            {
                file.Commit();
            }
            catch (ToolRequestException)
            {
                file.Discard();
                throw;
            }
        }

        return (pass, new ViewFileFacts(target.Path, target.Format, sink.Rows, bytes, Created: written));
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
