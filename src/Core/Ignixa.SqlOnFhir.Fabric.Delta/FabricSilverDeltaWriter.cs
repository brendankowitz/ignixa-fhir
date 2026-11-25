// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Apache.Arrow;
using DeltaLake.Interfaces;
using DeltaLake.Table;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Writes batches of FHIR resources into local Silver Delta tables with the healthcare data solutions upsert: per id,
/// a row replaces the stored one only when its <c>meta_lastUpdated</c> is strictly newer, and
/// <c>msftCreatedDatetime</c> keeps its first value. Each table's rows are committed as one MERGE, so the table's
/// change data feed records every insert and update.
/// </summary>
public static class FabricSilverDeltaWriter
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMilliseconds(100);

    /// <summary>Writes the batch with the default <see cref="FabricSilverWriterOptions"/>.</summary>
    /// <inheritdoc cref="WriteBatchAsync(IReadOnlyList{FabricSilverTableBatch}, FabricSilverIngestion, FabricSilverWriterOptions, CancellationToken)"/>
    public static Task<FabricSilverBatchResult> WriteBatchAsync(
        IReadOnlyList<FabricSilverTableBatch> batches,
        FabricSilverIngestion ingestion,
        CancellationToken cancellationToken) =>
        WriteBatchAsync(batches, ingestion, new FabricSilverWriterOptions(), cancellationToken);

    /// <summary>
    /// Projects every resource, then creates or validates every table, then merges each table's rows in batch order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Before anything is written</b> the whole call is checked: every batch within
    /// <see cref="FabricSilverWriterOptions.MaxSourceResourcesPerTable"/>, every resource projected (one that cannot
    /// be rejects the call), and every existing table matching its contract and within
    /// <see cref="FabricSilverWriterOptions.MaxTargetFiles"/> and <see cref="FabricSilverWriterOptions.MaxTargetRows"/>.
    /// Tables without a Delta log are then created (an empty version 0 that a replay reuses). A table whose creation
    /// is lost to another creator is checked only then, so its failure can follow the creation of earlier tables.
    /// </para>
    /// <para>
    /// <b>Reduction.</b> Each table's batch is reduced to one row per Silver id: the greatest
    /// <c>meta_lastUpdated</c> wins and, on a tie, the later resource in the batch (an Ignixa POLICY; healthcare data
    /// solutions leaves ties nondeterministic). Resources without
    /// <c>meta.lastUpdated</c> are dropped. The result reports both counts.
    /// </para>
    /// <para>
    /// <b>Atomicity is per table.</b> Each table's rows commit in one MERGE, all or nothing. Tables are merged one at a
    /// time in batch order and a failure stops the call, so earlier tables may be committed while later ones are not
    /// (<see cref="FabricSilverWriteException.CompletedTables"/> lists the tables completed: committed, or unchanged
    /// with no new version). Replaying the same batch is safe and completes it: rows already written are left as they
    /// are, because only a strictly newer row replaces a stored one. Each merge attempt validates the table it loads
    /// against the contract again, so a table changed after validation fails the merge before the engine starts.
    /// </para>
    /// <para>
    /// <b>Concurrency.</b> Writers in other processes may target the same tables. A merge that loses the commit race
    /// is retried against the new table state with jittered backoff, up to
    /// <see cref="FabricSilverWriterOptions.MaxMergeAttempts"/> attempts; no other failure is retried. Within a process
    /// merges run one at a time, because one merge's peak memory is the whole budget.
    /// </para>
    /// <para>
    /// <b>Cancellation</b> is observed before and between merges, never during one: a merge the engine has started
    /// finishes and reports its true outcome. Cancellation observed while the call projects resources, validates or
    /// creates tables (that is, before the merge loop reaches its first table) propagates as a plain
    /// <see cref="OperationCanceledException"/>. From the first table's merge on, including while it waits for another
    /// merge in the process to finish or backs off between retries, it is a
    /// <see cref="FabricSilverWriteCanceledException"/> (also an <see cref="OperationCanceledException"/>) listing the
    /// tables already completed (committed, or unchanged with no new version), which is empty when the first table had
    /// not completed. A table whose input reduces to no rows is reported unchanged without observing cancellation.
    /// Either way a replay completes the rest.
    /// </para>
    /// </remarks>
    /// <param name="batches">One batch per table; each table path at most once.</param>
    /// <param name="ingestion">The source file the resources came from, recorded on every row.</param>
    /// <param name="options">The fail-closed ceilings and retry limit.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One result per table, in batch order.</returns>
    /// <exception cref="ArgumentNullException">An argument or a batch is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Two batches name the same table path (compared ignoring case).</exception>
    /// <exception cref="FabricSilverLimitExceededException">A batch or a table is over a ceiling, or a table's size cannot be proven; no merge ran, but tables created earlier in this call may hold an empty version 0.</exception>
    /// <exception cref="FabricSilverResourceRejectedException">A resource cannot be projected; nothing was written.</exception>
    /// <exception cref="FabricSilverWriteException">The engine or the file system failed, or a table does not match its contract (<see cref="Exception.InnerException"/> is then a <see cref="FabricSilverTableMismatchException"/>, and a replay fails again until the table is fixed); <see cref="FabricSilverWriteException.Phase"/> and <see cref="FabricSilverWriteException.MayHaveCommitted"/> state what was written. After an engine or I/O failure a replay is safe.</exception>
    /// <exception cref="FabricSilverWriteCanceledException"><paramref name="cancellationToken"/> was canceled after merging started; it lists the completed tables.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before merging started.</exception>
    public static async Task<FabricSilverBatchResult> WriteBatchAsync(
        IReadOnlyList<FabricSilverTableBatch> batches,
        FabricSilverIngestion ingestion,
        FabricSilverWriterOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(ingestion);
        ArgumentNullException.ThrowIfNull(options);
        if (batches.Any(batch => batch is null))
        {
            throw new ArgumentNullException(nameof(batches), "The batches hold a null.");
        }

        if (batches.GroupBy(batch => batch.TablePath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Two batches name the same table.", nameof(batches));
        }

        foreach (var batch in batches.Where(batch => batch.Resources.Count > options.MaxSourceResourcesPerTable))
        {
            throw new FabricSilverLimitExceededException(
                batch.TablePath, FabricSilverLimit.SourceResources, options.MaxSourceResourcesPerTable, batch.Resources.Count);
        }

        var reductions = batches.Select(batch => Prepare(batch, ingestion)).ToList();
        cancellationToken.ThrowIfCancellationRequested();

        using var engine = new DeltaEngine(new EngineOptions
        {
            DataFusionExecutionTargetPartitions = MergeEngineSettings.TargetPartitions,
            DataFusionExecutionBatchSize = MergeEngineSettings.BatchSize,
        });

        var outcomes = await PrepareTablesAsync(engine, batches, options, cancellationToken);

        var completed = new List<FabricSilverTableResult>(batches.Count);
        for (var index = 0; index < batches.Count; index++)
        {
            try
            {
                completed.Add(await MergeAsync(engine, batches[index], reductions[index], outcomes[index], options, completed, cancellationToken));
            }
            catch (OperationCanceledException exception)
            {
                throw new FabricSilverWriteCanceledException(System.Array.AsReadOnly(completed.ToArray()), exception, cancellationToken);
            }
        }

        return new FabricSilverBatchResult(completed.AsReadOnly());
    }

    // Projects, reduces and converts a table's batch once, so any data error surfaces before a table is touched.
    private static LatestRowReduction Prepare(FabricSilverTableBatch batch, FabricSilverIngestion ingestion)
    {
        var rows = new List<FabricSilverRow>(batch.Resources.Count);
        for (var position = 0; position < batch.Resources.Count; position++)
        {
            var resource = batch.Resources[position];
            try
            {
                rows.Add(batch.Projection.Project(resource, ingestion));
            }
            catch (FabricSilverProjectionException exception)
            {
                throw new FabricSilverResourceRejectedException(
                    batch.TablePath, position, batch.Contract.ResourceType, RawId(resource), exception);
            }
        }

        var reduction = LatestRowReducer.Reduce(rows, batch.LastUpdatedOrdinal);
        using var proof = SilverArrowConverter.Convert(batch.Columns, batch.ArrowSchema, reduction.Rows);
        return reduction;
    }

    // The typed getter throws for a non-string id, which is exactly the resource worth reporting.
    private static string? RawId(ResourceJsonNode resource) =>
        ((IMutableJsonNode)resource).MutableNode["id"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;

    private static async Task<FabricSilverTableOutcome[]> PrepareTablesAsync(
        DeltaEngine engine,
        IReadOnlyList<FabricSilverTableBatch> batches,
        FabricSilverWriterOptions options,
        CancellationToken cancellationToken)
    {
        var hasLog = batches.Select(batch => Probe(batch, FabricSilverDeltaTables.HasLog, LogDirectory(batch))).ToArray();
        if (options.AfterLogProbe is { } afterLogProbe)
        {
            await afterLogProbe();
        }

        var outcomes = new FabricSilverTableOutcome[batches.Count];

        for (var index = 0; index < batches.Count; index++)
        {
            if (hasLog[index])
            {
                await InspectAsync(engine, batches[index], options, cancellationToken);
                outcomes[index] = FabricSilverTableOutcome.Validated;
            }
            else if (Probe(batches[index], FabricSilverDeltaTables.HasEntriesOtherThanLog, batches[index].TablePath))
            {
                // Another writer may have created the table since the log was first checked.
                if (Probe(batches[index], FabricSilverDeltaTables.HasLog, LogDirectory(batches[index])))
                {
                    hasLog[index] = true;
                    await InspectAsync(engine, batches[index], options, cancellationToken);
                    outcomes[index] = FabricSilverTableOutcome.Validated;
                    continue;
                }

                throw new FabricSilverWriteException(
                    batches[index].TablePath,
                    FabricSilverWritePhase.Validate,
                    attempts: 0,
                    completedTables: [],
                    new FabricSilverTableMismatchException(
                        FabricSilverTableAspect.UnrecognizedContent,
                        propertyName: null,
                        "The table directory holds files but no Delta log; a table is never created on top of unknown files."));
            }
        }

        for (var index = 0; index < batches.Count; index++)
        {
            if (hasLog[index])
            {
                continue;
            }

            var batch = batches[index];
            if (options.BeforeCreateTable is { } beforeCreateTable)
            {
                await beforeCreateTable(batch.TablePath);
            }

            try
            {
                outcomes[index] = await FabricSilverDeltaTables.EnsureTableAsync(batch.TablePath, batch.Contract, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A lost creation race validates the winner's table; its mismatch or unreadable log is a validation failure.
                var phase = exception is FabricSilverTableMismatchException or InvalidDataException
                    ? FabricSilverWritePhase.Validate
                    : FabricSilverWritePhase.Create;
                throw new FabricSilverWriteException(batch.TablePath, phase, attempts: 0, completedTables: [], exception);
            }

            // Another creator won the race and may already have written to the table.
            if (outcomes[index] == FabricSilverTableOutcome.Validated)
            {
                await InspectAsync(engine, batch, options, cancellationToken);
            }
        }

        return outcomes;
    }

    // A table directory that cannot be listed fails like a log that cannot be loaded.
    private static bool Probe(FabricSilverTableBatch batch, Func<string, bool> probe, string path)
    {
        try
        {
            return probe(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new FabricSilverWriteException(batch.TablePath, FabricSilverWritePhase.Validate, attempts: 0, completedTables: [], exception);
        }
    }

    private static async Task InspectAsync(
        DeltaEngine engine,
        FabricSilverTableBatch batch,
        FabricSilverWriterOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            using var table = await LoadAsync(engine, batch, cancellationToken);
            ExistingTableValidator.Validate(table.ProtocolVersions(), table.Metadata(), batch.Contract);
            await TargetTableSize.RequireWithinAsync(table, batch.TablePath, options, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or FabricSilverLimitExceededException))
        {
            throw new FabricSilverWriteException(batch.TablePath, FabricSilverWritePhase.Validate, attempts: 0, completedTables: [], exception);
        }
    }

    private static async Task<FabricSilverTableResult> MergeAsync(
        DeltaEngine engine,
        FabricSilverTableBatch batch,
        LatestRowReduction reduction,
        FabricSilverTableOutcome outcome,
        FabricSilverWriterOptions options,
        List<FabricSilverTableResult> completed,
        CancellationToken cancellationToken)
    {
        if (reduction.Rows.Count == 0)
        {
            return new FabricSilverTableResult(batch.TablePath, outcome, committedVersion: null, reduction, 0, 0, attempts: 0);
        }

        await options.MergeGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong? before = null;
                ulong? after;
                var engineMergeStarted = false;
                try
                {
                    using var table = await LoadAsync(engine, batch, cancellationToken);
                    ExistingTableValidator.Validate(table.ProtocolVersions(), table.Metadata(), batch.Contract);
                    before = table.Version();

                    // Each attempt exports its own record batch, so a retry never reuses one the engine has consumed.
                    using var source = SilverArrowConverter.Convert(batch.Columns, batch.ArrowSchema, reduction.Rows);
                    options.BeforeEngineMerge?.Invoke(batch.TablePath);
                    engineMergeStarted = true;

                    // Never canceled once started: the binding reports a canceled token as a cancellation even when the
                    // native merge committed, which would hide the commit from the caller.
                    await table.MergeAsync(batch.MergeStatement, [source], batch.ArrowSchema, CancellationToken.None);
                    options.AfterEngineMerge?.Invoke(batch.TablePath);
                    after = table.Version();
                }
                catch (Exception exception) when (engineMergeStarted || exception is not OperationCanceledException)
                {
                    var conflict = MergeConflict.IsRetryable(exception);
                    if (conflict && attempt < options.MaxMergeAttempts)
                    {
                        await Task.Delay(Backoff(attempt), options.BackoffClock, cancellationToken);
                        continue;
                    }

                    // A lost commit race committed nothing; any other failure after the engine started may have
                    // committed (the engine's post-commit steps run after the commit), so the table is read again.
                    var (mayHaveCommitted, versionCheckFailure) = engineMergeStarted && !conflict
                        ? await VersionAdvancedAsync(engine, batch, before)
                        : (false, null);
                    throw new FabricSilverWriteException(
                        batch.TablePath,
                        FabricSilverWritePhase.Merge,
                        attempt,
                        System.Array.AsReadOnly(completed.ToArray()),
                        exception,
                        mayHaveCommitted,
                        versionCheckFailure);
                }

                // A merge that changes no row commits nothing, so the loaded version stays put.
                if (after == before)
                {
                    return new FabricSilverTableResult(batch.TablePath, outcome, committedVersion: null, reduction, 0, 0, attempt);
                }

                var version = (long)after!.Value;
                MergeCommitMetrics metrics;
                try
                {
                    // The merge is committed, so its result is read whatever the caller's token says.
                    metrics = await MergeCommitMetrics.ReadAsync(batch.TablePath, version, reduction.Rows.Count, CancellationToken.None);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    throw new FabricSilverWriteException(
                        batch.TablePath,
                        FabricSilverWritePhase.ReadCommit,
                        attempt,
                        System.Array.AsReadOnly(completed.ToArray()),
                        exception,
                        committedVersion: version);
                }

                return new FabricSilverTableResult(batch.TablePath, outcome, version, reduction, metrics.InsertedRows, metrics.UpdatedRows, attempt);
            }
        }
        finally
        {
            options.MergeGate.Release();
        }
    }

    private static Task<ITable> LoadAsync(DeltaEngine engine, FabricSilverTableBatch batch, CancellationToken cancellationToken) =>
        engine.LoadTableAsync(new TableOptions { TableLocation = batch.TablePath }, cancellationToken);

    // Unknown counts as advanced: claiming a table may hold the rows is safe, claiming it is unchanged is not. A failed
    // read is returned, not dropped, so the caller can report why the version is unknown.
    private static async Task<(bool Advanced, Exception? ReadFailure)> VersionAdvancedAsync(
        DeltaEngine engine,
        FabricSilverTableBatch batch,
        ulong? before)
    {
        try
        {
            using var table = await LoadAsync(engine, batch, CancellationToken.None);
            return (before is null || table.Version() is not { } current || current > before, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (true, exception);
        }
    }

    // Exponential with equal jitter: half the window is fixed, half random, so racing writers spread out.
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Retry jitter only spreads racing writers; it guards nothing.")]
    private static TimeSpan Backoff(int failedAttempt)
    {
        var window = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, FirstBackoff.Ticks << Math.Min(failedAttempt - 1, 10)));
        return (window / 2) + ((window / 2) * Random.Shared.NextDouble());
    }

    private static string LogDirectory(FabricSilverTableBatch batch) =>
        Path.Combine(batch.TablePath, FabricSilverDeltaTables.LogDirectoryName);
}
