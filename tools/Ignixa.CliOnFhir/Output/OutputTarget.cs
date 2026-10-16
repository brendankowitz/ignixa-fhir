// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Ignixa.CliOnFhir.Output;

/// <summary>A file a run writes outside the data folder (a view's rows, a type file of <c>deid</c> or <c>map</c>), checked before anything is read or written.</summary>
/// <param name="Path">
/// The file's one canonical path (links resolved, <c>..</c> applied after them, as the operating system does). The checks, the
/// directories created and the write all use this string, so what was checked is what is written.
/// </param>
/// <param name="Format"><see cref="Csv"/>, <see cref="Ndjson"/> or <see cref="Parquet"/>.</param>
/// <param name="Overwrite">Whether an existing file at <paramref name="Path"/> may be replaced.</param>
/// <param name="FolderPath">The data folder, which nothing may be written in.</param>
internal sealed record OutputTarget(string Path, string Format, bool Overwrite, string FolderPath)
{
    public const string Csv = "csv";

    public const string Ndjson = "ndjson";

    public const string Parquet = "parquet";

    private const string Remedy = "files written into it can be read as data on the next catalog. Give an out path outside the folder";

    /// <summary>
    /// The file at <paramref name="requestedPath"/>, which must be outside the data folder, by the same check the index cache uses
    /// (links resolved, case ignored), must not be a directory, and must not exist unless <paramref name="overwrite"/> is given.
    /// </summary>
    /// <param name="folderPath">The data folder.</param>
    /// <param name="requestedPath">The path as the caller gave it.</param>
    /// <param name="format">What the file will hold, for the writer the caller picks.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <exception cref="ToolRequestException"><c>out-refused</c>: a path in or around the data folder, a directory, or an existing file without <paramref name="overwrite"/>.</exception>
    public static OutputTarget ForFile(string folderPath, string requestedPath, string format, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);
        ArgumentException.ThrowIfNullOrEmpty(requestedPath);

        string path = Canonical(requestedPath);
        Check(folderPath, path);

        if (Directory.Exists(path))
        {
            throw ToolRequestException.Error("out-refused", $"The output path '{path}' is a directory; name a file.");
        }

        if (File.Exists(path) && !overwrite)
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{path}' already exists; choose another path, or set overwrite to replace it.");
        }

        return new OutputTarget(path, format, overwrite, folderPath);
    }

    /// <summary>
    /// Creates the directories the file needs and a new file beside the target to write into, and returns it open. The directory is
    /// resolved again once it exists, since a link may have replaced a part of it since <see cref="ForFile"/> looked, and the file
    /// is created exclusively (<c>CreateNew</c>: never an existing file, never one a link names). Then, before anything is written,
    /// the operating system is asked where the open file really is (<see cref="OpenFilePath"/>): a directory swapped for a link
    /// between the check and the creation puts the file wherever the link leads, which no check of a path string can see. A file
    /// that is not at the checked path is closed, deleted (this run created it a moment ago, empty) and refused. Where the operating
    /// system cannot say, the new file's path is resolved and checked instead. The writers are given the open file and never reopen
    /// it by path; <see cref="OutputFile.Commit"/> renames it to <see cref="Path"/>, so a file already there is replaced only by a
    /// complete result.
    /// </summary>
    /// <param name="notes">Receives an <c>out-temp-left</c> or <c>out-directory-left</c> note for what a refusal could not remove.</param>
    /// <exception cref="ToolRequestException">
    /// <c>out-refused</c>: the path now leads somewhere else (the data folder included). <c>out-write-failed</c>: the file cannot be created.
    /// </exception>
    public OutputFile Begin(List<IssueItem> notes) => Begin(notes, beforeCreate: null);

    /// <param name="notes">As for <see cref="Begin(List{IssueItem})"/>.</param>
    /// <param name="beforeCreate">Runs between the directory's last check and the file's creation; a test swaps the directory there.</param>
    internal OutputFile Begin(List<IssueItem> notes, Action? beforeCreate)
    {
        ArgumentNullException.ThrowIfNull(notes);

        string directory = System.IO.Path.GetDirectoryName(Path)!;
        List<string> created = MissingDirectories(directory);
        string temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        SafeFileHandle handle;
        try
        {
            Directory.CreateDirectory(directory);

            // ForFile() looked at this very string; if the directory now resolves to anything else, a link has replaced a part of it.
            if (!DirectoryUnchanged(directory))
            {
                throw ToolRequestException.Error("out-refused", $"The directory '{directory}' of the output file is now reached through a link; run it again.");
            }

            beforeCreate?.Invoke();
            handle = File.OpenHandle(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(RemoveCreated(created), notes);
            throw ToolRequestException.Error("out-write-failed", $"Cannot create the output file in '{directory}': {ex.Message}");
        }
        catch (ToolRequestException)
        {
            Note(RemoveCreated(created), notes);
            throw;
        }

        return OpenFilePath.IsAvailable ? Verified(handle, temp, created, notes) : CheckedByPath(handle, temp, created, notes);
    }

    /// <summary>Whether <paramref name="directory"/>, a path already resolved, still resolves to itself: no link has replaced a part of it.</summary>
    internal static bool DirectoryUnchanged(string directory)
    {
        try
        {
            return string.Equals(CanonicalPath.Resolve(directory), directory, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// After the rename: where the file landed (<paramref name="landed"/>, from its handle; null to resolve <see cref="Path"/> instead)
    /// must be outside the data folder and, when the handle says, the target itself. If not, the error names it and the file is left;
    /// nothing is deleted inside the data folder.
    /// </summary>
    internal void CheckFinal(string? landed)
    {
        string resolved = landed ?? Canonical(Path);
        if (new FolderOverlap(FolderPath).Reason(resolved, "output file", Remedy) is { } reason)
        {
            throw ToolRequestException.Error("out-refused", $"The output file ended up inside the data folder at '{resolved}' and was not removed; delete it yourself: {reason}.");
        }

        if (landed is not null && !OpenFilePath.Same(landed, Path))
        {
            throw ToolRequestException.Error("out-refused", $"The output file was renamed to '{Path}' but landed at '{landed}', because a link replaced its directory during the rename; it was left there.");
        }
    }

    /// <summary>
    /// Removes the directories in <paramref name="created"/> (deepest first) that are still plain, empty directories this run made; one
    /// something else has put an entry in since is kept. Nothing is removed once the deepest no longer resolves to itself.
    /// </summary>
    /// <returns>An <c>out-directory-left</c> information issue when a directory this run made could not be removed; otherwise null.</returns>
    internal static IssueItem? RemoveCreated(IReadOnlyList<string> created)
    {
        if (created.Count == 0)
        {
            return null;
        }

        if (!DirectoryUnchanged(created[0]))
        {
            return DirectoryLeft(created[^1], $"'{created[0]}' no longer resolves to the path that was checked, so nothing was deleted through it");
        }

        foreach (string directory in created)
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone, which is what removing it was for.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return DirectoryLeft(directory, $"it could not be deleted ({ex.GetType().Name}: {ex.Message})");
            }
        }

        return null;
    }

    internal static string Canonical(string path)
    {
        try
        {
            return CanonicalPath.Resolve(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ToolRequestException.Error("out-refused", $"The output path '{QueryEcho.Of(path)}' cannot be resolved: {ex.Message}");
        }
    }

    /// <summary>
    /// Refuses a <paramref name="resolvedPath"/> inside the data folder: by its path, links resolved (the index cache's rule), and on
    /// Linux also by identity, so that a bind mount or a second mount of the folder, which no link leads to, is the folder too.
    /// </summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>.</exception>
    internal static void Check(string folderPath, string resolvedPath) =>
        Check(folderPath, resolvedPath, "output file", System.IO.Path.GetDirectoryName(resolvedPath)!);

    /// <summary>
    /// <see cref="Check(string, string)"/> for a directory a run writes several files into: refused when it is the data folder, in it,
    /// or around it (the index cache's rule), or, on Linux, the folder by identity, itself or through an ancestor.
    /// </summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>.</exception>
    internal static void CheckDirectory(string folderPath, string resolvedDirectory) =>
        Check(folderPath, resolvedDirectory, "output directory", resolvedDirectory);

    private static void Check(string folderPath, string resolvedPath, string noun, string directory)
    {
        if (new FolderOverlap(folderPath).Reason(resolvedPath, noun, Remedy) is { } reason)
        {
            throw ToolRequestException.Error("out-refused", $"Cannot write there: {reason}.");
        }

        if (DirectoryIdentity.IsAvailable && MountedFolder(folderPath, directory) is { } alias)
        {
            throw ToolRequestException.Error("out-refused", $"Cannot write there: the directory '{alias}' is the data folder itself under another path (a bind mount or a second mount of it, or a link that replaced the directory after its path was resolved); the folder is never written and {Remedy}.");
        }
    }

    // The output's directory, or the first of its ancestors, that is the data folder by device and inode; null when none is.
    private static string? MountedFolder(string folderPath, string directory)
    {
        try
        {
            return DirectoryIdentity.Of(folderPath) is { } folder ? DirectoryIdentity.SameAsOrUnder(directory, folder) : null;
        }
        catch (IOException ex)
        {
            throw ToolRequestException.Error("out-refused", $"Cannot write there: the output directory cannot be compared with the data folder ({ex.Message}).");
        }
    }

    private static IssueItem DirectoryLeft(string directory, string why) =>
        new("information", "out-directory-left", $"The directory '{directory}', which this run created for the output file, was left: {why}.", Location: null);

    private static void Note(IssueItem? left, List<IssueItem> notes)
    {
        if (left is not null)
        {
            notes.Add(left);
        }
    }

    // The new file must be at the checked path, as the operating system sees the open file. One that is not was created a moment
    // ago, exclusively, and nothing has been written to it, so deleting exactly that file undoes it, wherever the swap put it.
    private OutputFile Verified(SafeFileHandle handle, string temp, List<string> created, List<IssueItem> notes)
    {
        string? location;
        try
        {
            location = OpenFilePath.Of(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            handle.Dispose();
            notes.Add(new IssueItem("information", "out-temp-left", $"The temporary file '{temp}' may have been left: where it was created could not be read ({ex.GetType().Name}), so it was not deleted.", Location: null));
            throw ToolRequestException.Error("out-write-failed", $"Cannot tell where the output file was created: {ex.Message}");
        }

        if (location is not null && OpenFilePath.Same(location, temp))
        {
            return new OutputFile(this, handle, temp, created);
        }

        handle.Dispose();
        DeleteStray(location, notes);
        Note(RemoveCreated(created), notes);
        // Windows can also resolve a path spelt in another form (a short 8.3 name, a substituted or mapped drive) to a different string.
        string spelling = OperatingSystem.IsWindows() ? ", or the path is spelt in a form Windows resolves differently (a short 8.3 name, a substituted or mapped drive; give its long form)" : string.Empty;
        throw ToolRequestException.Error("out-refused", $"The output file was not created at the path that was checked ('{temp}'): a link replaced a part of its directory while it was being created{spelling}. It was deleted with nothing written to it; run it again.");
    }

    // Where the operating system cannot say where an open file is: the path is resolved once more and checked, as before.
    private OutputFile CheckedByPath(SafeFileHandle handle, string temp, List<string> created, List<IssueItem> notes)
    {
        var file = new OutputFile(this, handle, temp, created);
        try
        {
            Check(FolderPath, Canonical(temp));
            return file;
        }
        catch (ToolRequestException)
        {
            Note(file.Discard(), notes);
            throw;
        }
    }

    private static void DeleteStray(string? location, List<IssueItem> notes)
    {
        if (location is null)
        {
            return; // something else deleted it already
        }

        try
        {
            File.Delete(location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add(new IssueItem("information", "out-temp-left", $"The temporary file was left at '{location}' (where it was as of the check): it was created there through a link and could not be deleted ({ex.GetType().Name}: {ex.Message}). Delete it yourself.", Location: null));
        }
    }

    // The directories from the first missing one down, so that only these are removed if nothing is written.
    private static List<string> MissingDirectories(string directory)
    {
        List<string> missing = [];
        for (string? current = directory; current is not null && !Directory.Exists(current); current = System.IO.Path.GetDirectoryName(current))
        {
            missing.Add(current);
        }

        return missing;
    }
}
