// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.CliOnFhir.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// The file a run writes into, beside its target, created exclusively and held open from creation to <see cref="Commit"/> or
/// <see cref="Discard"/>. The writers write to <see cref="Stream"/> and never reopen the file by path. Where the operating system can
/// say (<see cref="OpenFilePath"/>), the open handle's real path is checked, not a path string: what the writers write goes to that
/// file whatever happens to the path meanwhile, and the file is renamed over the target only while the handle shows it still at
/// <see cref="TempPath"/>. The rename is atomic, so the target is never half written, a file already there is replaced only by a
/// complete result, and a hard link to another file (a data file) is replaced, not written through. A run that does not commit
/// discards, which removes the temporary file and the directories this run created, and nothing else; a temporary file that is no
/// longer where it was created is left and reported where it really is.
/// </summary>
/// <remarks>
/// Residual: between the last check before the rename and the rename itself (microseconds) a process with write access to the
/// output's parent directory could still swap it for a link. On Linux the handle is still open after the rename and shows where
/// the file landed, so a file that landed in the data folder is reported by name, never deleted; elsewhere the final path is
/// resolved again instead.
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Commit or Discard ends every file, and each says what became of it; a Dispose would close it and leave a temporary file without a word.")]
internal sealed class OutputFile
{
    private readonly OutputTarget _target;
    private readonly SafeFileHandle _handle;
    private readonly FileStream _file;
    private readonly IReadOnlyList<string> _createdDirectories;
    private bool _closed;
    private bool _moved;

    /// <param name="target">The checked target.</param>
    /// <param name="handle">The temporary file, created exclusively and checked to be at <paramref name="tempPath"/>; owned from here.</param>
    /// <param name="tempPath">Its path, as checked.</param>
    /// <param name="createdDirectories">The directories this run created, deepest first.</param>
    public OutputFile(OutputTarget target, SafeFileHandle handle, string tempPath, IReadOnlyList<string> createdDirectories)
    {
        _target = target;
        _handle = handle;
        _file = new FileStream(handle, FileAccess.ReadWrite);
        Stream = new UnclosedStream(_file);
        TempPath = tempPath;
        _createdDirectories = createdDirectories;
    }

    /// <summary>The temporary file, for the writers; disposing it does not close the file.</summary>
    public Stream Stream { get; }

    /// <summary>The temporary file's path, as checked.</summary>
    public string TempPath { get; }

    /// <summary>
    /// Flushes the file, checks that its handle still shows it at <see cref="TempPath"/> and that the target's directory is still the
    /// one that was checked, and renames it over the target; where the file landed is checked against the data folder afterwards.
    /// </summary>
    /// <exception cref="ToolRequestException">
    /// <c>out-refused</c>: the file or its directory moved (nothing was renamed), a file appeared at the target and <c>overwrite</c> was
    /// not given, or the file landed somewhere other than the target (it is named and left). <c>out-write-failed</c>: the file could
    /// not be flushed, closed or renamed.
    /// </exception>
    public void Commit()
    {
        string directory = Path.GetDirectoryName(_target.Path)!;
        try
        {
            _file.Flush();
            if (OpenFilePath.IsAvailable && !AtTempPath())
            {
                throw ToolRequestException.Error("out-refused", $"The temporary output file was moved, or its directory '{directory}' replaced, while the view was running, so nothing was renamed.");
            }

            // Implied by the handle's check where there is one; the only check of the path where there is not.
            if (!OutputTarget.DirectoryUnchanged(directory))
            {
                throw ToolRequestException.Error("out-refused", $"The directory '{directory}' of the output file was replaced by a link while the view was running, so nothing was renamed.");
            }

            string? landed = Rename();
            _target.CheckFinal(landed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{_target.Path}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Closes the file and removes it and the directories this run created. It is left, and reported, when it is no longer where it
    /// was created or its directory no longer resolves to the path that was checked: nothing is deleted through a moved or replaced
    /// directory. Nothing here throws for a file system failure; what could not be removed is reported instead.
    /// </summary>
    /// <returns>
    /// An information issue: <c>out-temp-left</c> with where the temporary file was as of the check, or <c>out-directory-left</c>; null when
    /// everything was removed, or when the file was already renamed over the target (<see cref="Commit"/> reports any problem with it).
    /// </returns>
    public IssueItem? Discard()
    {
        if (_moved)
        {
            return null;
        }

        string? location;
        try
        {
            location = _closed || !OpenFilePath.IsAvailable ? TempPath : Location();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CloseForDiscard();
            return Left(TempPath, $"where it is could not be read ({ex.GetType().Name}), so it was not deleted");
        }

        CloseForDiscard();
        if (location is null)
        {
            // Someone else deleted it; only the directories are left to remove.
            return OutputTarget.RemoveCreated(_createdDirectories);
        }

        string directory = Path.GetDirectoryName(_target.Path)!;
        if (!OpenFilePath.Same(location, TempPath))
        {
            return Left(location, "it is no longer where it was created (its directory was moved or replaced by a link), so nothing was deleted");
        }

        if (!OutputTarget.DirectoryUnchanged(directory))
        {
            return Left(TempPath, $"its directory '{directory}' no longer resolves to the path that was checked, so nothing was deleted through it");
        }

        try
        {
            File.Delete(TempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Left(TempPath, $"it could not be deleted ({ex.GetType().Name}: {ex.Message})");
        }

        return OutputTarget.RemoveCreated(_createdDirectories);
    }

    private static IssueItem Left(string location, string why) =>
        new("information", "out-temp-left", $"The temporary file was left at '{location}' (where it was as of the check): {why}. Delete it when the directory is safe to use.", Location: null);

    // Where the open file is now; null once it has no name.
    private string? Location() => OpenFilePath.Of(_handle);

    private bool AtTempPath() => Location() is { } location && OpenFilePath.Same(location, TempPath);

    // Renames the file over the target and returns where the handle shows it landed, or null when that cannot be read. Windows cannot
    // rename a file this process holds open without sharing delete, so it is closed first there.
    private string? Rename()
    {
        bool renameOpen = OpenFilePath.IsAvailable && !OperatingSystem.IsWindows();
        if (!renameOpen)
        {
            Close();
        }

        try
        {
            File.Move(TempPath, _target.Path, _target.Overwrite);
        }
        catch (IOException ex) when (!_target.Overwrite && File.Exists(_target.Path))
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{_target.Path}' appeared while the view was running ({ex.GetType().Name}); choose another path, or set overwrite to replace it.");
        }

        _moved = true;
        if (!renameOpen)
        {
            return null;
        }

        // A lookup that fails must not leave the file open; the failure itself is reported by Commit.
        try
        {
            return Location();
        }
        finally
        {
            Close();
        }
    }

    private void Close()
    {
        if (!_closed)
        {
            _closed = true;
            _file.Dispose();
        }
    }

    // The file is being deleted or left as a partial result, so bytes that could not be flushed into it are not lost data; the
    // handle is closed whether or not the flush succeeds.
    private void CloseForDiscard()
    {
        try
        {
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to report: what was not flushed belonged to a result that is being thrown away.
        }
    }
}
