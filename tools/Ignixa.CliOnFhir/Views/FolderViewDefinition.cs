// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>A ViewDefinition resource found in a file of the folder.</summary>
/// <param name="Json">The resource.</param>
/// <param name="Id">Its <c>id</c>, when it has one.</param>
/// <param name="Url">Its <c>url</c>, when it has one.</param>
/// <param name="Version">Its <c>version</c>, when it has one.</param>
/// <param name="Location">The resource's place in the folder.</param>
internal sealed record FolderViewDefinition(JsonObject Json, string? Id, string? Url, string? Version, EvidenceItem Location)
{
    /// <summary>The name the tools give it: <c>ViewDefinition/id</c> with the id made safe, or null when it has no id.</summary>
    public string? Reference => Id is null ? null : FhirIds.Reference(ViewDefinitions.ResourceType, Id);

    /// <summary>Where it was read from, as <c>file</c> or <c>file:line</c> or <c>file@offset</c>.</summary>
    public string Where => Location.Line is int line ? $"{Location.File}:{line}" : $"{Location.File}@{Location.Offset}";
}
