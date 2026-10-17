// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>One run of a FHIR Mapping Language map over resources of the folder (CLI <c>map</c>; the design's <c>fml_transform</c>).</summary>
/// <param name="Map">
/// The map: inline StructureMap JSON (text starting with <c>{</c>), inline FML (text with a line break, or starting with <c>map </c> or
/// a comment), the path of an FML or StructureMap JSON file, else the id (<c>sm1</c>, <c>StructureMap/sm1</c>) or url
/// (<c>url</c>, <c>url|version</c>) of a StructureMap in the folder. Nothing is fetched from the network.
/// </param>
/// <param name="Imports">Maps the map imports, each given as <paramref name="Map"/> is; the folder's StructureMaps are searched too.</param>
/// <param name="Out">Write every transformed resource to <c>&lt;Type&gt;.ndjson</c> in this directory, outside the folder, instead of returning a preview.</param>
/// <param name="ResourceType">Transform resources of this type (default: the map's source type), all or those <paramref name="Query"/> matches.</param>
/// <param name="Query">A FHIR search query over <paramref name="ResourceType"/> choosing the resources.</param>
/// <param name="Patient">A Patient id: that patient's compartment, or with <paramref name="ResourceType"/> its members of that type.</param>
/// <param name="References">Exactly these resources (<c>Type/id</c>); not combined with the other scope arguments.</param>
/// <param name="Overwrite">Replace the type file <paramref name="Out"/> would get when it exists.</param>
/// <param name="MaxBytes">The response budget, as for the other tools that return resources.</param>
/// <param name="TimeoutSeconds">How long a run may take (default <see cref="MapRunner.DefaultTimeoutSeconds"/>, at most <see cref="MapRunner.MaxTimeoutSeconds"/>).</param>
/// <param name="MaxResources">The most resources a run may read (default <see cref="MapRunner.DefaultMaxResources"/>).</param>
public sealed record MapRequest(
    string Map,
    IReadOnlyList<string>? Imports = null,
    string? Out = null,
    string? ResourceType = null,
    string? Query = null,
    string? Patient = null,
    IReadOnlyList<string>? References = null,
    bool Overwrite = false,
    int? MaxBytes = null,
    int? TimeoutSeconds = null,
    int? MaxResources = null);
