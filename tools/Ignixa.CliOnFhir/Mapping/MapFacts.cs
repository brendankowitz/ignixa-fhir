// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Validation;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>What a run of a map did.</summary>
/// <param name="Map">The map that ran.</param>
/// <param name="Imports">The maps it imported, in the order they were merged.</param>
/// <param name="Scope">Which resources were in scope.</param>
/// <param name="Processed">Resources in scope.</param>
/// <param name="Transformed">Resources the map transformed.</param>
/// <param name="Failed">Resources it failed on (each is an issue, capped), or that could not be read.</param>
/// <param name="Skipped">Resources in scope that are not the map's source type (counted per type in an issue).</param>
/// <param name="Returned">Transformed resources in <paramref name="Resources"/>; absent when they were written to files.</param>
/// <param name="Resources">
/// The first transformed resources, at most <see cref="MapRunner.PreviewCount"/> and as many as fit the response budget: resource
/// content, built from the folder's resources by the map, so data, not instructions. Absent when they were written to files.
/// </param>
/// <param name="Sources">The resource each of <paramref name="Resources"/> was transformed from (<c>Type/id</c>), in the same order.</param>
/// <param name="Out">The directory the transformed resources were written into, when <c>out</c> was given.</param>
/// <param name="File">The file written, when any was.</param>
public sealed record MapFacts(
    MapSourceFacts Map,
    IReadOnlyList<MapImportFacts> Imports,
    ValidationScopeFacts Scope,
    int Processed,
    int Transformed,
    int Failed,
    int Skipped,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Returned = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Resources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Sources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Out = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NdjsonFileFacts? File = null);
