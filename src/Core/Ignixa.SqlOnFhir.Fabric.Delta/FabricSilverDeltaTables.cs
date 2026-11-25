// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>Creates a local Silver Delta table from its contract, or proves an existing one still matches it.</summary>
public static class FabricSilverDeltaTables
{
    private const string LogDirectoryName = "_delta_log";

    /// <summary>
    /// Makes <paramref name="tablePath"/> a Delta table that matches <paramref name="contract"/>: creates it when it
    /// has no Delta log, otherwise validates the latest state of the existing table. Safe to call concurrently for the
    /// same table, in one process or several: exactly one caller creates and the others validate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Creation</b> writes only the version 0 commit: protocol reader 1 / writer 4 without table features, a new
    /// table id, Parquet format, the contract's schema string exactly, no partition columns, and the contract
    /// configuration with one deliberate change: <c>delta.enableChangeDataFeed</c> is written as lowercase
    /// <c>true</c>, the only spelling every Delta engine honors. The commit is published atomically and never replaces
    /// an existing version 0; losing that race to another creator validates the winner's table instead.
    /// </para>
    /// <para>
    /// <b>Validation</b> reads the latest table state, checkpoints included, through the Delta engine and fails closed
    /// (see <see cref="FabricSilverTableAspect"/>): protocol exactly 1/4, the same schema
    /// (compared structurally, because the engine re-serializes the logged schema text), no partition columns, the
    /// change feed flag exactly lowercase <c>true</c> (a table created with
    /// <c>True</c> is rejected, not corrected), every other contract property present with the same value, and no
    /// other <c>delta.*</c> property unless it only tunes checkpoints, retention, statistics or file size.
    /// </para>
    /// <para>
    /// <b>Directory states.</b> A missing directory, an empty one, or one whose only entry is an empty
    /// <c>_delta_log</c> (hidden <c>.*.tmp</c> temp files are ignored) has no table and is created. Any other entry in
    /// <c>_delta_log</c> means a log exists and is validated; a log the engine cannot load fails with the engine's
    /// error. A directory with other entries but no log is rejected as
    /// <see cref="FabricSilverTableAspect.UnrecognizedContent"/>, so a table is never created on top of unknown files.
    /// </para>
    /// <para>Each call starts and disposes its own Delta engine.</para>
    /// </remarks>
    /// <param name="tablePath">The local table directory; relative paths resolve against the current directory.</param>
    /// <param name="contract">The Silver table contract the table must hold.</param>
    /// <param name="cancellationToken">Cancels the call. Once version 0 is published, the call reports it as created.</param>
    /// <returns>Whether this call created the table or validated an existing one.</returns>
    /// <exception cref="ArgumentException"><paramref name="tablePath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is <see langword="null"/>.</exception>
    /// <exception cref="FabricSilverTableMismatchException">The directory holds a table, or files, the contract cannot adopt.</exception>
    /// <exception cref="DeltaLake.Errors.DeltaLakeException">The engine cannot load the existing log.</exception>
    /// <exception cref="IOException">The directory or log cannot be read or written; nothing is retried.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not accessible.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<FabricSilverTableOutcome> EnsureTableAsync(
        string tablePath,
        FabricSilverContract contract,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();

        var tableDirectory = Path.GetFullPath(tablePath);
        var logDirectory = Path.Combine(tableDirectory, LogDirectoryName);

        if (HasLog(logDirectory))
        {
            return await ValidateAsync(tableDirectory, contract, cancellationToken);
        }

        if (HasEntriesOtherThanLog(tableDirectory))
        {
            // A concurrent creator may have published and written data since the first look; only files without a log
            // are unknown content.
            return HasLog(logDirectory)
                ? await ValidateAsync(tableDirectory, contract, cancellationToken)
                : throw new FabricSilverTableMismatchException(
                    FabricSilverTableAspect.UnrecognizedContent,
                    propertyName: null,
                    "The table directory holds files but no Delta log; a table is never created on top of unknown files.");
        }

        Directory.CreateDirectory(logDirectory);
        var commit = VersionZeroCommit.Create(contract, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var versionZeroPath = Path.Combine(logDirectory, VersionZeroCommit.FileName);

        return await ExclusiveFilePublisher.TryPublishAsync(versionZeroPath, commit, cancellationToken)
            ? FabricSilverTableOutcome.Created
            : await ValidateAsync(tableDirectory, contract, cancellationToken);
    }

    private static bool HasLog(string logDirectory) =>
        Directory.Exists(logDirectory) &&
        Directory.EnumerateFileSystemEntries(logDirectory)
            .Any(entry => !ExclusiveFilePublisher.IsStagingFileName(Path.GetFileName(entry)));

    private static bool HasEntriesOtherThanLog(string tableDirectory) =>
        Directory.Exists(tableDirectory) &&
        Directory.EnumerateFileSystemEntries(tableDirectory)
            .Any(entry => !string.Equals(Path.GetFileName(entry), LogDirectoryName, StringComparison.Ordinal));

    private static async Task<FabricSilverTableOutcome> ValidateAsync(
        string tableDirectory,
        FabricSilverContract contract,
        CancellationToken cancellationToken)
    {
        using var engine = new DeltaEngine(EngineOptions.Default);
        using var table = await engine.LoadTableAsync(new TableOptions { TableLocation = tableDirectory }, cancellationToken);
        ExistingTableValidator.Validate(table.ProtocolVersions(), table.Metadata(), contract);
        return FabricSilverTableOutcome.Validated;
    }
}
