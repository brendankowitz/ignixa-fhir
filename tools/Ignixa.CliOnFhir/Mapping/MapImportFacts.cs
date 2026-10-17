// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>A map that was imported, and where it was found.</summary>
/// <param name="Url">Its url as the tools write a url (<see cref="Catalog.FhirIds.Written"/>: a digest when it could carry prose).</param>
/// <param name="Source"><c>inline</c>, <c>file</c> (an <c>--import</c>) or <c>folder</c> (a StructureMap of the folder).</param>
/// <param name="Reference">For a StructureMap of the folder, <c>StructureMap/id</c>.</param>
public sealed record MapImportFacts(
    string Url,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reference);
