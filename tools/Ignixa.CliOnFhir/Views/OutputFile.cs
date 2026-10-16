// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// The file a run writes into, beside its target: the rows go to <see cref="TempPath"/> and <see cref="Commit"/> renames it
/// over the target, which is atomic, so the target is never half written, a file already there is replaced only by a complete
/// result, and a hard link to another file (a data file) is replaced, not written through. A run that does not commit
/// <see cref="Discard"/>s, which removes the temporary file and the directories this run created, and nothing else.
/// </summary>
/// <param name="TempPath">The file being written, which this run created.</param>
/// <param name="TargetPath">The file it becomes.</param>
/// <param name="Overwrite">Whether an existing target may be replaced.</param>
/// <param name="CreatedDirectories">The directories this run created, the deepest last.</param>
internal sealed record OutputFile(string TempPath, string TargetPath, bool Overwrite, IReadOnlyList<string> CreatedDirectories)
{
    /// <summary>Renames the written file over the target.</summary>
    /// <exception cref="ToolRequestException">A file appeared at the target since it was checked and <c>overwrite</c> was not given, or the rename failed.</exception>
    public void Commit()
    {
        try
        {
            File.Move(TempPath, TargetPath, Overwrite);
        }
        catch (IOException ex) when (!Overwrite && File.Exists(TargetPath))
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{TargetPath}' appeared while the view was running ({ex.GetType().Name}); choose another path, or set overwrite to replace it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{TargetPath}' failed: {ex.Message}");
        }
    }

    /// <summary>Removes the temporary file and the directories this run created (deepest first, only while empty).</summary>
    public void Discard()
    {
        if (File.Exists(TempPath))
        {
            File.Delete(TempPath);
        }

        foreach (string directory in CreatedDirectories)
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
