// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// The output files of this process not yet renamed into place or discarded, the directories they created, and the existing targets
/// renamed aside while a set of files is committed. When the CLI's watchdog has to end a run whose evaluation would not stop, the run
/// cannot clean up after itself, so the watchdog names what it may leave behind from here. Thread-safe.
/// </summary>
internal static class OpenOutputFiles
{
    private static readonly ConcurrentDictionary<OutputFile, byte> Files = new(ReferenceEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<string, string> Backups = new(StringComparer.Ordinal);

    /// <summary>The temporary files of the output files still open, in no particular order.</summary>
    public static IReadOnlyList<string> TempPaths => [.. Files.Keys.Select(file => file.TempPath)];

    /// <summary>
    /// The directories the output files still open created, each once, in no particular order. A file renamed into place leaves its
    /// directories holding it, so they are not listed once it is.
    /// </summary>
    public static IReadOnlyList<string> CreatedDirectories => [.. Files.Keys.SelectMany(file => file.CreatedDirectories).Distinct(StringComparer.Ordinal)];

    /// <summary>Each existing target renamed aside (the backup's path, and the target's) and not yet put back or removed.</summary>
    public static IReadOnlyList<(string Backup, string Target)> BackedUp => [.. Backups.Select(pair => (pair.Key, pair.Value))];

    public static void Add(OutputFile file) => Files.TryAdd(file, 0);

    public static void Remove(OutputFile file) => Files.TryRemove(file, out _);

    public static void AddBackup(string backup, string target) => Backups[backup] = target;

    public static void RemoveBackup(string backup) => Backups.TryRemove(backup, out _);
}
