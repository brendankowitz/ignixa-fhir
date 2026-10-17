// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;
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
    /// <param name="asOf">The session's instant, which the view reads as <c>%asOf</c> and the facts state.</param>
    /// <param name="logger">Receives the details of a <c>view-internal-error</c>; required, so that no caller can drop them.</param>
    public static Task<ToolEnvelope<ViewFacts?>> RunAsync(FolderSession session, ViewRequest request, AsOfClock asOf, ILogger logger, CancellationToken cancellationToken) =>
        RunAsync(session, request, asOf, logger, timeoutSignal: CancellationToken.None, cancellationToken);

    /// <param name="timeoutSignal">
    /// Stops the run as its timeout does, besides the request's own seconds: a test passes one already cancelled, so the run is a
    /// timeout at its first check, whatever the machine's speed.
    /// </param>
    internal static async Task<ToolEnvelope<ViewFacts?>> RunAsync(FolderSession session, ViewRequest request, AsOfClock asOf, ILogger logger, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(asOf);
        ArgumentNullException.ThrowIfNull(logger);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<ViewFacts>(session, folderProblem, cancellationToken);
        }

        List<IssueItem> notes = [];
        try
        {
            return await WithinTimeAsync(session, request, asOf, notes, timeoutSignal, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return Failure(session, ex.Issue, notes, cancellationToken);
        }
#pragma warning disable CA1031 // The boundary of the verb: anything unexpected is logged in full and reported as an error, never as a success.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "The view run failed unexpectedly.");
            return Failure(
                session,
                new IssueItem("error", "view-internal-error", $"The view run failed unexpectedly with {ex.GetType().Name} (digest {FhirIds.Digest(ex.Message)}); this is a defect of the tool, not of the request, and its details are in the log on stderr.", Location: null),
                notes,
                cancellationToken);
        }
    }

    // Which step a run is in, for a timeout to say what used the time.
    private sealed class RunProgress
    {
        public bool ResolvingScope { get; set; }
    }

    // A failure still reports what it left behind (a temporary file it could not safely delete).
    private static ToolEnvelope<ViewFacts?> Failure(FolderSession session, IssueItem error, List<IssueItem> notes, CancellationToken cancellationToken) =>
        EnvelopeAssembler.FolderProblem(session) is not null || notes.Count == 0
            ? EnvelopeAssembler.Failure<ViewFacts>(session, error, cancellationToken)
            : EnvelopeAssembler.Create<ViewFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [error, .. notes], cancellationToken);

    private static async Task<ToolEnvelope<ViewFacts?>> WithinTimeAsync(FolderSession session, ViewRequest request, AsOfClock asOf, List<IssueItem> notes, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        int seconds = request.TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (seconds is < 1 or > MaxTimeoutSeconds)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'timeout' must be 1 to {MaxTimeoutSeconds} seconds, not {seconds}.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSignal);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var progress = new RunProgress();
        try
        {
            return await ExecuteAsync(session, request, asOf, notes, progress, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            string narrower = request.Compartment is null ? "Limit it to one patient's resources with compartment Patient/<id>, or raise" : "Raise";
            string cold = progress.ResolvingScope
                ? " The time ran out while the patient compartments were being worked out, which a first run over a folder does from the search indexes of every type that can be in one; a build cut by the timeout keeps nothing, so run it once with a higher timeout (for example --timeout 300) to build them and keep them in the index cache, and later runs read them from there (with the cache off, nothing is kept)."
                : string.Empty;
            throw ToolRequestException.Error("view-timeout", $"The view did not finish within {seconds} seconds and was stopped.{cold} {narrower} the timeout (up to {MaxTimeoutSeconds}).");
        }
    }

    private static async Task<ToolEnvelope<ViewFacts?>> ExecuteAsync(FolderSession session, ViewRequest request, AsOfClock clock, List<IssueItem> notes, RunProgress progress, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        List<OmittedItem> omitted = [];
        ViewPaging paging = ViewPaging.Resolve(request, issues);
        OutputTarget? target = ViewOutput.Resolve(session, request);
        int budget = ResponseBudget.Resolve(request.MaxBytes, session.MaxResponseBytes, issues, BudgetAlternatives);

        AsOfResolution asOf = clock.Resolve();
        issues.AddRange(asOf.Issues);
        LoadedView loaded = ViewLocator.Resolve(session, request.View);
        IReadOnlyDictionary<string, string> variables = ViewVariables.Parse(request.Variables);
        CompiledView view = ViewCompiler.Compile(session, ViewAsOf.With(loaded, asOf.Value, variables, issues), request.Where);
        issues.AddRange(ViewVariables.Undeclared(variables, view.Expression));
        issues.AddRange(ViewIssues.ResolveUse(loaded.Json));
        int? distinctColumn = DistinctColumn(view, request.Distinct);
        progress.ResolvingScope = request.Compartment is not null;
        ViewScope scope = await ViewScope.ResolveAsync(session, view.Resource, request.Compartment, issues, omitted, cancellationToken);
        progress.ResolvingScope = false;
        CheckResourceLimit(request, view, scope);

        var evaluation = new ViewEvaluation(session, view, variables, JsonSourceNodeFactory.Parse(new JsonObject { ["resourceType"] = view.Resource }).ToElement(session.Schema));
        evaluation.CheckVariables();
        if (scope.Entries.Count == 0)
        {
            evaluation.EvaluateOnEmpty(loaded.FromFolder);
        }

        (ViewPass pass, ViewFileFacts? file) = await PassAsync(evaluation, scope, loaded, view, paging, distinctColumn, target, notes, cancellationToken);
        issues.AddRange(notes);
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
            asOf.Value,
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
            string memory = view.OrdersAcrossResources ? " (and a view like this one, a top-level unionAll of plain branches, holds all of them in memory while it runs)" : string.Empty;
            string narrower = request.Compartment is null ? "Limit it to one patient's resources with compartment Patient/<id>, or raise" : "Raise";
            throw ToolRequestException.Error("too-many-resources", $"The view would read {scope.Entries.Count} {view.Resource} resources, more than the limit of {limit}{memory}. {narrower} maxResources.");
        }
    }

    // One pass over the rows. With an output file every row is written to a temporary file beside it, which is open and handed to the
    // writer, and only a complete, non-empty result is renamed over the target; a run that fails or produces nothing removes what it
    // created and touches nothing else. What it could not safely remove is reported in notes.
    private static async Task<(ViewPass Pass, ViewFileFacts? File)> PassAsync(
        ViewEvaluation evaluation, ViewScope scope, LoadedView loaded, CompiledView view, ViewPaging paging, int? distinctColumn, OutputTarget? target, List<IssueItem> notes, CancellationToken cancellationToken)
    {
        if (target is null)
        {
            return (await ViewPass.RunAsync(evaluation.Rows(scope.Entries, loaded.FromFolder, cancellationToken), view.Columns.Count, paging, distinctColumn, sink: null, cancellationToken), null);
        }

        OutputFile file = target.Begin(notes);
        ViewFileSink? sink = null;
        ViewPass pass;
        long bytes;
        long rows;
        try
        {
            sink = ViewFileSink.Create(file.Stream, target.Format, view.Columns);

            // The rows kept are the first ones, which evidence cites.
            pass = await ViewPass.RunAsync(evaluation.Rows(scope.Entries, loaded.FromFolder, cancellationToken), view.Columns.Count, new ViewPaging(ViewResponse.EvidenceRows, Offset: 0), distinctColumn, sink, cancellationToken);
            bytes = await sink.CompleteAsync(cancellationToken);
            rows = sink.Rows;
            ViewFileSink finished = sink;
            sink = null;
            await finished.DisposeAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await AbandonAsync(sink, file, notes);
            throw ToolRequestException.Error("out-write-failed", $"Writing '{target.Path}' failed: {ex.Message}. The partial file was removed unless a note says otherwise.");
        }
        catch
        {
            await AbandonAsync(sink, file, notes);
            throw;
        }

        if (rows == 0)
        {
            Note(file.Discard(), notes);
            return (pass, new ViewFileFacts(target.Path, target.Format, 0, 0, Created: false));
        }

        try
        {
            file.Commit();
        }
        catch (ToolRequestException)
        {
            Note(file.Discard(), notes);
            throw;
        }

        return (pass, new ViewFileFacts(target.Path, target.Format, rows, bytes, Created: true));
    }

    // Every way out of a failed write ends the writer first, whatever stopped it, and then removes what this run created. A writer
    // that fails to finish while being abandoned changes nothing: its file is being thrown away, and the failure that stopped the
    // run is the one reported.
    private static async Task AbandonAsync(ViewFileSink? sink, OutputFile file, List<IssueItem> notes)
    {
        try
        {
            if (sink is not null)
            {
                await sink.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // See above: the partial file is discarded next.
        }
        finally
        {
            Note(file.Discard(), notes);
        }
    }

    private static void Note(IssueItem? left, List<IssueItem> notes)
    {
        if (left is not null)
        {
            notes.Add(left);
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
