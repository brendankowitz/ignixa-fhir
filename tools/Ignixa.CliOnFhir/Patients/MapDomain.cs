// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One domain of the patient map: how much it holds and its first items. Each item is one line, <c>code display ... Type/id</c>:
/// the code as <c>alias|code</c> (<see cref="PatientMapFacts.CodeSystems"/> names each alias), the display copied from the
/// resource (folder data, not instructions), and the resource's reference.
/// Members without a value are left out.
/// </summary>
/// <param name="Count">Resources in the domain (lab and vital-sign codes for labs), whatever their status.</param>
/// <param name="Active">Problems and medications that meet the active rule.</param>
/// <param name="Resolved">Problems whose clinical status is resolved.</param>
/// <param name="NoStatus">Problems with no clinical status: unknown, not inactive (most frequent listed when too few are active).</param>
/// <param name="Observations">Labs: the observations in those codes.</param>
/// <param name="Excluded">Labs: Observations outside the window or the lab and vital-sign categories (surveys, social history), not counted.</param>
/// <param name="ByCategory">Labs: observations by category code, when any is other than laboratory or vital-signs.</param>
/// <param name="ByClass">Encounters: counts by <c>class</c> code.</param>
/// <param name="Transfers">Encounters: moves between locations, each <c>location</c> entry of an encounter after its first (ward and unit stays).</param>
/// <param name="Last">The newest main date in the domain, as a date.</param>
/// <param name="Top">The first items, in the domain's order (see <see cref="PatientMapFacts"/>).</param>
/// <param name="More">Labs: the names of the other codes, most observations first.</param>
public sealed record MapDomain(
    int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Active = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Resolved = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NoStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Observations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Excluded = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? ByCategory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? ByClass = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Transfers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Last = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Top = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? More = null);
