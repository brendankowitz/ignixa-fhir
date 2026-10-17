// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Views;

/// <summary>Where the ViewDefinition came from and what it reads.</summary>
/// <param name="Source"><c>inline</c>, <c>file</c> or <c>folder</c>.</param>
/// <param name="Resource">The resource type the view runs over.</param>
/// <param name="Reference">The ViewDefinition's <c>ViewDefinition/id</c> (its id as the tools name it) when it came from the folder; absent otherwise.</param>
public sealed record ViewSourceFacts(
    string Source,
    string Resource,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reference);
