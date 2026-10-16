// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// A directory a run writes several files into (one NDJSON file per resource type), outside the data folder. Each file in it is an
/// <see cref="OutputTarget"/> of its own, checked by the same rules as a single output file, and written through an
/// <see cref="OutputFile"/>, so every safeguard of a single file applies to each.
/// </summary>
/// <param name="Path">The directory's one canonical path (links resolved, <c>..</c> applied after them).</param>
/// <param name="Overwrite">Whether existing files in it that the run writes may be replaced.</param>
/// <param name="FolderPath">The data folder, which nothing may be written in.</param>
internal sealed record OutputDirectory(string Path, bool Overwrite, string FolderPath)
{
    /// <summary>
    /// The directory at <paramref name="requestedPath"/>: refused when it is the data folder, inside it or around it (the index
    /// cache's rule, links resolved, case ignored), the folder by identity on Linux (a bind mount), or an existing file. It need
    /// not exist yet; the first file written creates it.
    /// </summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>.</exception>
    public static OutputDirectory Resolve(string folderPath, string requestedPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);
        ArgumentException.ThrowIfNullOrEmpty(requestedPath);

        string path = OutputTarget.Canonical(requestedPath);
        OutputTarget.CheckDirectory(folderPath, path);
        if (System.IO.File.Exists(path))
        {
            throw ToolRequestException.Error("out-refused", $"The output path '{path}' is a file; name a directory, which the run writes one NDJSON file per resource type into.");
        }

        return new OutputDirectory(path, overwrite, folderPath);
    }

    /// <summary>The checked target of the file <paramref name="fileName"/> (a plain name, no directory) in this directory.</summary>
    /// <exception cref="ToolRequestException"><c>out-refused</c>: as for <see cref="OutputTarget.ForFile"/>.</exception>
    public OutputTarget File(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (System.IO.Path.GetFileName(fileName) != fileName || fileName is "." or "..")
        {
            throw new ArgumentException($"'{fileName}' is not a plain file name.", nameof(fileName));
        }

        return OutputTarget.ForFile(FolderPath, System.IO.Path.Combine(Path, fileName), OutputTarget.Ndjson, Overwrite);
    }
}
