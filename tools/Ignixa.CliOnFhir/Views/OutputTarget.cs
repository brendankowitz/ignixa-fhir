// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>Where a run writes every row, checked before anything is evaluated or written.</summary>
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
    /// The file a request writes to, or null when it only returns rows. The path must be outside the data folder, by the
    /// same check the index cache uses (links resolved, case ignored), and must not exist unless <c>overwrite</c> is given.
    /// </summary>
    /// <exception cref="ToolRequestException">
    /// <c>format</c> without <c>out</c>, <c>count</c> or <c>offset</c> with it, a format that is not offered or cannot be told from
    /// the extension, a path in or around the data folder, a directory, or an existing file without <c>overwrite</c>.
    /// </exception>
    public static OutputTarget? Resolve(FolderSession session, ViewRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Out is null)
        {
            return request.Format is null && !request.Overwrite
                ? null
                : throw ToolRequestException.Error("invalid-parameter", "'format' and 'overwrite' apply to 'out', which was not given.");
        }

        if (request.Count is not null || request.Offset != 0)
        {
            throw ToolRequestException.Error("invalid-parameter", "'count' and 'offset' page the rows a response returns; 'out' writes every row to the file and returns none.");
        }

        string format = FormatOf(request);
        string path = Canonical(request.Out);
        Check(session.RootPath, path);

        if (Directory.Exists(path))
        {
            throw ToolRequestException.Error("out-refused", $"The output path '{path}' is a directory; name a file.");
        }

        if (File.Exists(path) && !request.Overwrite)
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{path}' already exists; choose another path, or set overwrite to replace it.");
        }

        return new OutputTarget(path, format, request.Overwrite, session.RootPath);
    }

    /// <summary>
    /// Creates the directories the file needs and an empty file beside it to write into, checking again, now that they exist, that
    /// neither can lead into the data folder: a link may have replaced a directory since <see cref="Resolve"/> looked. The rows are
    /// written to that file, and <see cref="OutputFile.Commit"/> renames it to <see cref="Path"/>, so a file already there is
    /// replaced only by a complete result.
    /// </summary>
    /// <exception cref="ToolRequestException">The path now leads into the data folder, or the file cannot be created.</exception>
    public OutputFile Begin()
    {
        string directory = System.IO.Path.GetDirectoryName(Path)!;
        List<string> created = MissingDirectories(directory);
        string temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        var file = new OutputFile(temp, Path, Overwrite, created);
        try
        {
            Directory.CreateDirectory(directory);

            // Resolve() looked at this very string; if the directory now resolves to anything else, a link has replaced a part of it.
            if (!string.Equals(Canonical(directory), directory, StringComparison.Ordinal))
            {
                throw ToolRequestException.Error("out-refused", $"The directory '{directory}' of the output file is now reached through a link; run it again.");
            }

            // CreateNew never opens an existing file or follows a link planted at that name.
            using (new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
            }

            Check(FolderPath, Canonical(temp));
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            file.Discard();
            throw ToolRequestException.Error("out-write-failed", $"Cannot create the output file in '{directory}': {ex.Message}");
        }
        catch
        {
            file.Discard();
            throw;
        }
    }

    private static string Canonical(string path)
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

    private static void Check(string folderPath, string resolvedPath)
    {
        if (new FolderOverlap(folderPath).Reason(resolvedPath, "output file", Remedy) is { } reason)
        {
            throw ToolRequestException.Error("out-refused", $"Cannot write there: {reason}.");
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

    private static string FormatOf(ViewRequest request)
    {
        string? chosen = request.Format ?? System.IO.Path.GetExtension(request.Out)?.TrimStart('.');
        return chosen?.ToUpperInvariant() switch
        {
            "CSV" => Csv,
            "NDJSON" => Ndjson,
            "PARQUET" => Parquet,
            _ => throw ToolRequestException.Error("invalid-parameter", request.Format is null
                ? $"The format cannot be told from the extension of '{QueryEcho.Of(request.Out!)}'; end it in .csv, .ndjson or .parquet, or give format."
                : $"'format' must be csv, ndjson or parquet, not '{QueryEcho.Of(request.Format)}'."),
        };
    }
}
