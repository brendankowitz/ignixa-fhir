// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// The output files of this process not yet renamed into place or discarded. When the CLI's watchdog has to end a run whose
/// evaluation would not stop, the run cannot clean up after itself, so the watchdog names what it may leave behind from here.
/// Thread-safe.
/// </summary>
internal static class OpenOutputFiles
{
    private static readonly ConcurrentDictionary<OutputFile, byte> Files = new(ReferenceEqualityComparer.Instance);

    /// <summary>The temporary files of the output files still open, in no particular order.</summary>
    public static IReadOnlyList<string> TempPaths => [.. Files.Keys.Select(file => file.TempPath)];

    public static void Add(OutputFile file) => Files.TryAdd(file, 0);

    public static void Remove(OutputFile file) => Files.TryRemove(file, out _);
}
