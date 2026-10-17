// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>A map's text as it was found, before it is parsed.</summary>
/// <param name="Text">FML, or StructureMap JSON.</param>
/// <param name="Format"><see cref="Fml"/> or <see cref="StructureMap"/>.</param>
/// <param name="Source"><see cref="InlineSource"/>, <see cref="FileSource"/> or <see cref="FolderSource"/>.</param>
/// <param name="FromFolder">Whether it is data of the folder (a StructureMap of the catalog, or a file inside the folder), and so untrusted.</param>
/// <param name="File">The file's absolute path, or, for a StructureMap of the folder, its path relative to the folder.</param>
/// <param name="Reference">For a StructureMap of the folder, <c>StructureMap/id</c> with the id made safe.</param>
/// <param name="Evidence">For a StructureMap of the folder, where it is.</param>
internal sealed record LoadedMap(string Text, string Format, string Source, bool FromFolder, string? File, string? Reference, EvidenceItem? Evidence)
{
    public const string Fml = "fml";

    public const string StructureMap = "structuremap";

    public const string InlineSource = "inline";

    public const string FileSource = "file";

    public const string FolderSource = "folder";

    /// <summary>Where a problem in it is: its file, or <c>inline</c>; a line and column are added when known.</summary>
    public string Where(int? line = null, int? column = null)
    {
        string where = File ?? InlineSource;
        return line is int known ? column is int col ? $"{where}:{known}:{col}" : $"{where}:{known}" : where;
    }
}
