// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>Where a run writes every row, checked before anything is evaluated or written.</summary>
/// <param name="Path">The absolute path of the file.</param>
/// <param name="Format"><see cref="Csv"/>, <see cref="Ndjson"/> or <see cref="Parquet"/>.</param>
internal sealed record OutputTarget(string Path, string Format)
{
    public const string Csv = "csv";

    public const string Ndjson = "ndjson";

    public const string Parquet = "parquet";

    private const string Remedy = "the file would be read as data on the next catalog. Give an out path outside the folder";

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
        string path = System.IO.Path.GetFullPath(request.Out);
        if (new FolderOverlap(session.RootPath).Reason(request.Out, "output file", Remedy) is { } reason)
        {
            throw ToolRequestException.Error("out-refused", $"Cannot write there: {reason}.");
        }

        if (Directory.Exists(path))
        {
            throw ToolRequestException.Error("out-refused", $"The output path '{path}' is a directory; name a file.");
        }

        if (File.Exists(path) && !request.Overwrite)
        {
            throw ToolRequestException.Error("out-refused", $"The output file '{path}' already exists; choose another path, or set overwrite to replace it.");
        }

        return new OutputTarget(path, format);
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
