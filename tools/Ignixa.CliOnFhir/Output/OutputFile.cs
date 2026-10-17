// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.CliOnFhir.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Ignixa.CliOnFhir.Output;

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

    // For a commit that can be rolled back: the existing target renamed aside, and the identities of it and of the file written.
    private string? _backup;
    private DirectoryIdentity? _backupIdentity;
    private DirectoryIdentity? _writtenIdentity;

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
        OpenOutputFiles.Add(this);
    }

    /// <summary>The temporary file, for the writers; disposing it does not close the file.</summary>
    public Stream Stream { get; }

    /// <summary>The temporary file's path, as checked.</summary>
    public string TempPath { get; }

    /// <summary>The directories this run created for the file, deepest first.</summary>
    public IReadOnlyList<string> CreatedDirectories => _createdDirectories;

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
        try
        {
            CheckInPlace();
            string? landed = Rename(replace: _target.Overwrite);
            _target.CheckFinal(landed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{_target.Path}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// <see cref="Commit"/> for a file of a set that is committed all or none: an existing target is first renamed aside, beside it,
    /// so that <see cref="Rollback"/> can put it back if a later file of the set fails, and the file is then renamed into the free
    /// place without replacing anything. <see cref="DropBackup"/> removes the old target once the whole set is in place.
    /// </summary>
    /// <exception cref="ToolRequestException">As for <see cref="Commit"/>.</exception>
    public void CommitWithBackup()
    {
        try
        {
            CheckInPlace();
            _writtenIdentity = IdentityOf(TempPath);
            if (File.Exists(_target.Path))
            {
                if (!_target.Overwrite)
                {
                    throw ToolRequestException.Error("out-refused", $"The output file '{_target.Path}' appeared while the run was writing it; choose another path, or set overwrite to replace it.");
                }

                string backup = Path.Combine(Path.GetDirectoryName(_target.Path)!, $".{Path.GetFileName(_target.Path)}.{Guid.NewGuid():N}.bak");
                _backupIdentity = IdentityOf(_target.Path);
                OpenOutputFiles.AddBackup(backup, _target.Path);
                try
                {
                    NoReplaceRename.Move(_target.Path, backup);
                }
                catch
                {
                    OpenOutputFiles.RemoveBackup(backup);
                    throw;
                }

                _backup = backup;
            }

            string? landed = Rename(replace: false);
            _target.CheckFinal(landed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-write-failed", $"Writing '{_target.Path}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Undoes <see cref="CommitWithBackup"/> when a later file of the set failed: the file this run renamed into place is deleted, and
    /// the target it replaced is renamed back. Both go only through the directory that was checked, and the file is deleted only when it
    /// is still the one this run wrote (by device and inode, where they can be read). Nothing here throws for a file system failure.
    /// </summary>
    /// <returns>An <c>out-rollback-left</c> warning when something could not be put back; otherwise null.</returns>
    public IssueItem? Rollback()
    {
        string directory = Path.GetDirectoryName(_target.Path)!;
        if ((_moved || _backup is not null) && !OutputTarget.DirectoryUnchanged(directory))
        {
            return RollbackLeft($"its directory '{directory}' no longer resolves to the path that was checked, so nothing was changed through it");
        }

        try
        {
            if (_moved)
            {
                if (!SameFile(_target.Path, _writtenIdentity))
                {
                    return RollbackLeft("the file at that path is no longer the one this run wrote, so it was not deleted");
                }

                File.Delete(_target.Path);
                _moved = false;
            }

            if (_backup is { } backup)
            {
                NoReplaceRename.Move(backup, _target.Path);
                _backup = null;
                OpenOutputFiles.RemoveBackup(backup);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RollbackLeft($"it could not be put back ({ex.GetType().Name}: {ex.Message})");
        }

        return null;
    }

    /// <summary>Removes the old target <see cref="CommitWithBackup"/> renamed aside, once the whole set is in place.</summary>
    /// <returns>An <c>out-backup-left</c> information issue when it could not be removed; otherwise null.</returns>
    public IssueItem? DropBackup()
    {
        if (_backup is not { } backup)
        {
            return null;
        }

        string directory = Path.GetDirectoryName(_target.Path)!;
        try
        {
            if (OutputTarget.DirectoryUnchanged(directory) && SameFile(backup, _backupIdentity))
            {
                File.Delete(backup);
                _backup = null;
                OpenOutputFiles.RemoveBackup(backup);
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IssueItem("information", "out-backup-left", $"The previous '{_target.Path}' was kept at '{backup}' (where it was as of the check): it could not be deleted ({ex.GetType().Name}: {ex.Message}).", Location: null);
        }

        return new IssueItem("information", "out-backup-left", $"The previous '{_target.Path}' was kept at '{backup}' (where it was as of the check): its directory or the file there changed, so it was not deleted.", Location: null);
    }

    /// <summary>
    /// The checks of <see cref="Commit"/> without the rename: flushes the file and checks that its handle still shows it at
    /// <see cref="TempPath"/> and that the target's directory is still the one that was checked. A run that writes several files
    /// prepares every one before it commits any, so that a file found moved stops the run before any is renamed.
    /// </summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>: the file or its directory moved. <c>out-write-failed</c>: the file could not be flushed.</exception>
    public void Prepare()
    {
        try
        {
            CheckInPlace();
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
        try
        {
            return DiscardOpen();
        }
        finally
        {
            OpenOutputFiles.Remove(this);
        }
    }

    private IssueItem? DiscardOpen()
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

    private void CheckInPlace()
    {
        string directory = Path.GetDirectoryName(_target.Path)!;
        _file.Flush();
        if (OpenFilePath.IsAvailable && !AtTempPath())
        {
            throw ToolRequestException.Error("out-refused", $"The temporary output file was moved, or its directory '{directory}' replaced, while the run was writing it, so nothing was renamed.");
        }

        // Implied by the handle's check where there is one; the only check of the path where there is not.
        if (!OutputTarget.DirectoryUnchanged(directory))
        {
            throw ToolRequestException.Error("out-refused", $"The directory '{directory}' of the output file was replaced by a link while the run was writing it, so nothing was renamed.");
        }
    }

    private IssueItem RollbackLeft(string why) =>
        new("warning", "out-rollback-left", $"'{_target.Path}' could not be rolled back after a later file of the run failed: {why}.{(_backup is null ? string.Empty : $" The file it replaced is kept at '{_backup}'.")}", Location: null);

    private static DirectoryIdentity? IdentityOf(string path) => DirectoryIdentity.IsAvailable ? DirectoryIdentity.Of(path) : null;

    // Whether the file at path is the one recorded; with no identity to compare (another system), only that something is there.
    private static bool SameFile(string path, DirectoryIdentity? recorded) =>
        recorded is { } identity ? DirectoryIdentity.Of(path) == identity : File.Exists(path);

    private static IssueItem Left(string location, string why) =>
        new("information", "out-temp-left", $"The temporary file was left at '{location}' (where it was as of the check): {why}. Delete it when the directory is safe to use.", Location: null);

    // Where the open file is now; null once it has no name.
    private string? Location() => OpenFilePath.Of(_handle);

    private bool AtTempPath() => Location() is { } location && OpenFilePath.Same(location, TempPath);

    // Renames the file over the target and returns where the handle shows it landed, or null when that cannot be read. Windows cannot
    // rename a file this process holds open without sharing delete, so it is closed first there.
    private string? Rename(bool replace)
    {
        bool renameOpen = OpenFilePath.IsAvailable && !OperatingSystem.IsWindows();
        if (!renameOpen)
        {
            Close();
        }

        try
        {
            if (replace)
            {
                File.Move(TempPath, _target.Path, overwrite: true);
            }
            else
            {
                NoReplaceRename.Move(TempPath, _target.Path);
            }
        }
        catch (IOException ex) when (!replace && File.Exists(_target.Path))
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{_target.Path}' appeared while the run was writing it ({ex.GetType().Name}); choose another path, or set overwrite to replace it.");
        }

        _moved = true;
        OpenOutputFiles.Remove(this);
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
