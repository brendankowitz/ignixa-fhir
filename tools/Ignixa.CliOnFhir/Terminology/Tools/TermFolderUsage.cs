// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>The terminology section of <c>folder_inspect</c> (and of <c>terminology_list</c> on request).</summary>
/// <param name="Status"><c>loaded</c>, or <c>not loaded</c> when no terminology call has loaded the store yet (nothing else is then computed).</param>
/// <param name="Rule">How the codes were collected and resolved.</param>
/// <param name="SystemsUsed">Distinct systems with codes in the folder.</param>
/// <param name="Systems">Per system, most codes first (at most the limit; the rest are counted in 'omitted').</param>
public sealed record TermFolderUsage(string Status, string? Rule, int? SystemsUsed, IReadOnlyList<SystemUsage>? Systems)
{
    public const string LoadedStatus = "loaded";
    public const string NotLoadedStatus = "not loaded";

    public static TermFolderUsage NotLoaded { get; } = new(NotLoadedStatus, null, null, null);
}
