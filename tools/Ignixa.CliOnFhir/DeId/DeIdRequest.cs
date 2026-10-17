// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.DeId;

/// <summary>One de-identification of resources in the folder into a directory outside it (CLI <c>deid</c>; the design's <c>deid_resources</c>).</summary>
/// <param name="Config">
/// The <c>Ignixa.DeId</c> configuration: inline JSON (text starting with <c>{</c>) or the path of a file holding it. Required:
/// <c>Ignixa.DeId</c> has no default profile that is safe for a whole folder.
/// </param>
/// <param name="Out">The directory to write <c>&lt;Type&gt;.ndjson</c> files into; it must be outside the folder.</param>
/// <param name="ResourceType">De-identify resources of this type: all of them, or those <paramref name="Query"/> matches.</param>
/// <param name="Query">A FHIR search query over <paramref name="ResourceType"/> choosing the resources.</param>
/// <param name="Patient">A Patient id: that patient's compartment, or with <paramref name="ResourceType"/> its members of that type.</param>
/// <param name="References">Exactly these resources (<c>Type/id</c>); not combined with the other scope arguments.</param>
/// <param name="Overwrite">Replace the type files the run writes when they exist; otherwise an existing one is refused before anything is read.</param>
/// <param name="TimeoutSeconds">How long a run may take (default <see cref="DeIdRunner.DefaultTimeoutSeconds"/>, at most <see cref="DeIdRunner.MaxTimeoutSeconds"/>).</param>
/// <param name="MaxResources">The most resources a run may read (default <see cref="DeIdRunner.DefaultMaxResources"/>).</param>
public sealed record DeIdRequest(
    string? Config,
    string? Out,
    string? ResourceType = null,
    string? Query = null,
    string? Patient = null,
    IReadOnlyList<string>? References = null,
    bool Overwrite = false,
    int? TimeoutSeconds = null,
    int? MaxResources = null);
