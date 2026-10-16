// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// The file a run writes into, beside its target. <see cref="Stream"/> is the file, created exclusively and open: the writers write
/// to it and never reopen it by path. <see cref="Commit"/> renames it over the target, which is atomic, so the target is never
/// half written, a file already there is replaced only by a complete result, and a hard link to another file (a data file) is
/// replaced, not written through. A run that does not commit <see cref="Discard"/>s, which removes the temporary file and the
/// directories this run created, and nothing else, and only while their directory still resolves to the path that was checked:
/// nothing is ever deleted through a link that has replaced it.
/// </summary>
/// <remarks>
/// Residual: between the last check before the rename and the rename itself (microseconds) a process with write access to the
/// output's parent directory could still swap it for a link. That needs such a process on the same machine; the data folder
/// is checked again after the rename and a file that landed in it is reported by name, never deleted.
/// </remarks>
internal sealed class OutputFile
{
    private readonly OutputTarget _target;
    private readonly IReadOnlyList<string> _createdDirectories;

    /// <param name="target">The checked target.</param>
    /// <param name="stream">The temporary file, open for writing.</param>
    /// <param name="tempPath">Its path, as checked.</param>
    /// <param name="createdDirectories">The directories this run created, deepest first.</param>
    public OutputFile(OutputTarget target, FileStream stream, string tempPath, IReadOnlyList<string> createdDirectories)
    {
        _target = target;
        Stream = stream;
        TempPath = tempPath;
        _createdDirectories = createdDirectories;
    }

    /// <summary>The temporary file, open; the writers own it once they are given it.</summary>
    public FileStream Stream { get; }

    /// <summary>The temporary file's path, for reporting.</summary>
    public string TempPath { get; }

    /// <summary>
    /// Closes the file and renames it over the target, after checking once more that the target's directory is still the one
    /// that was checked; the data folder is checked again afterwards.
    /// </summary>
    /// <exception cref="ToolRequestException">
    /// The directory changed (nothing was moved), a file appeared at the target and <c>overwrite</c> was not given, the rename
    /// failed, or the file is now inside the data folder (it is named and left).
    /// </exception>
    public void Commit()
    {
        Stream.Dispose();
        string directory = Path.GetDirectoryName(_target.Path)!;
        if (!OutputTarget.DirectoryUnchanged(directory))
        {
            throw ToolRequestException.Error("out-refused", $"The directory '{directory}' of the output file was replaced by a link while the view was running, so nothing was moved.");
        }

        try
        {
            File.Move(TempPath, _target.Path, _target.Overwrite);
        }
        catch (IOException ex) when (!_target.Overwrite && File.Exists(_target.Path))
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{_target.Path}' appeared while the view was running ({ex.GetType().Name}); choose another path, or set overwrite to replace it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{_target.Path}' failed: {ex.Message}");
        }

        _target.CheckFinal();
    }

    /// <summary>
    /// Closes the file and removes it and the directories this run created, unless their directory no longer resolves to the path
    /// that was checked, in which case nothing is deleted.
    /// </summary>
    /// <returns>An <c>out-temp-left</c> information issue naming the temporary file when it was left; otherwise null.</returns>
    public IssueItem? Discard()
    {
        Stream.Dispose();
        string directory = Path.GetDirectoryName(_target.Path)!;
        if (!OutputTarget.DirectoryUnchanged(directory))
        {
            return new IssueItem(
                "information",
                "out-temp-left",
                $"The temporary file '{TempPath}' was left where it was written: its directory '{directory}' no longer resolves to the path that was checked, so nothing was deleted through it.",
                Location: null);
        }

        File.Delete(TempPath);
        OutputTarget.RemoveCreated(_createdDirectories);
        return null;
    }
}
