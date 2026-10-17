// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Facts of <c>folder_code_systems</c> by default: one short line per system; <c>detail=full</c> gives <see cref="CodeSystemsFacts"/>.</summary>
/// <param name="SystemCount">Distinct code systems.</param>
/// <param name="Offset">Systems skipped.</param>
/// <param name="Returned">Systems in this page.</param>
/// <param name="Systems">This page, most codings first, then by URI.</param>
/// <param name="Terminology"><c>loaded</c> or <c>not loaded</c>: this tool never loads terminology itself unless asked (<c>withTerminology</c>).</param>
/// <param name="Kinds">
/// The record kinds of each resource type that has more than one (<see cref="Catalog.FolderKinds"/>), counted by short name; null
/// when every type is of one kind.
/// </param>
public sealed record CodeSystemsCompactFacts(
    int SystemCount,
    int Offset,
    int Returned,
    IReadOnlyList<CodeSystemSummary> Systems,
    string Terminology,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? Kinds = null);
