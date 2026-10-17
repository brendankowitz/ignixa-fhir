// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>Which map ran.</summary>
/// <param name="Source"><c>inline</c>, <c>file</c> or <c>folder</c>.</param>
/// <param name="Format"><c>fml</c> or <c>structuremap</c>.</param>
/// <param name="Url">Its url as the tools write a url (<see cref="Catalog.FhirIds.Written"/>).</param>
/// <param name="Group">The group it was entered by: its name when that is an identifier, else its digest.</param>
/// <param name="SourceType">The resource type it takes; absent when the map does not say.</param>
/// <param name="TargetType">The resource type it builds.</param>
/// <param name="File">The map file's absolute path, for <c>file</c>.</param>
/// <param name="Reference">For a StructureMap of the folder, <c>StructureMap/id</c>.</param>
public sealed record MapSourceFacts(
    string Source,
    string Format,
    string Url,
    string Group,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceType,
    string TargetType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reference);
