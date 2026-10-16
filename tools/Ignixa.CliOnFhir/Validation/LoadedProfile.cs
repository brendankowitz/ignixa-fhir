// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>A StructureDefinition loaded for validation, from the folder or a package.</summary>
/// <param name="Url">Its canonical url as written.</param>
/// <param name="Version">Its business version, when it has one.</param>
/// <param name="Type">The type it constrains (<c>StructureDefinition.type</c>).</param>
/// <param name="Source"><c>folder</c> or the package <c>id@version</c>.</param>
/// <param name="Usable">False when the validator could not build checks from it (no snapshot could be generated, malformed).</param>
/// <param name="Evidence">Where it was read.</param>
/// <param name="Elements">
/// By element path (<c>Observation.code</c>, <c>Observation.value[x]</c>), the required bindings and reference target profiles
/// its snapshot and differential define, outside slices, with those a differential-only profile inherits through its loaded
/// <c>baseDefinition</c> chain; the tool checks these itself (<see cref="ValidatorGapDetectors"/>).
/// </param>
internal sealed record LoadedProfile(string Url, string? Version, string Type, string Source, bool Usable, EvidenceItem Evidence, IReadOnlyDictionary<string, ProfileElementRule> Elements);
