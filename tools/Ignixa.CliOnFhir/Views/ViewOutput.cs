// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>The file a view run writes every row to: the view's own arguments, then the shared checks of <see cref="OutputTarget"/>.</summary>
internal static class ViewOutput
{
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

        return OutputTarget.ForFile(session.RootPath, request.Out, FormatOf(request), request.Overwrite);
    }

    private static string FormatOf(ViewRequest request)
    {
        string? chosen = request.Format ?? Path.GetExtension(request.Out)?.TrimStart('.');
        return chosen?.ToUpperInvariant() switch
        {
            "CSV" => OutputTarget.Csv,
            "NDJSON" => OutputTarget.Ndjson,
            "PARQUET" => OutputTarget.Parquet,
            _ => throw ToolRequestException.Error("invalid-parameter", request.Format is null
                ? $"The format cannot be told from the extension of '{QueryEcho.Of(request.Out!)}'; end it in .csv, .ndjson or .parquet, or give format."
                : $"'format' must be csv, ndjson or parquet, not '{QueryEcho.Of(request.Format)}'."),
        };
    }
}
