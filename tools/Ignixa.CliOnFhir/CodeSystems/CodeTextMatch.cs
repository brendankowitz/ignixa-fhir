// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>A code the folder uses whose display matched the words of a text search.</summary>
/// <param name="Code">The code with its system aliased (<c>sct|44054006</c>); the full system is in <see cref="CodeTextFacts.Systems"/>.</param>
/// <param name="Display">The display the data gives it, else the loaded terminology's.</param>
/// <param name="Types">Resource types carrying it, each with the resources of that type.</param>
/// <param name="Resources">Resources carrying it (of the types searched).</param>
/// <param name="Patients">Patients whose compartment holds at least one of them.</param>
/// <param name="Active">Conditions with it whose clinical status is active, recurrence or relapse; null when it is on no Condition.</param>
public sealed record CodeTextMatch(
    string Code,
    string? Display,
    IReadOnlyList<string> Types,
    int Resources,
    int Patients,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Active);
