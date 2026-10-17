// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Number of indexed resources of one type.</summary>
/// <param name="Kinds">The type's record kinds (<see cref="Catalog.FolderKinds"/>) with their counts and filters, when it has more than one; null otherwise.</param>
public sealed record ResourceTypeCount(
    string ResourceType,
    int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<KindFacts>? Kinds = null);

