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
internal sealed record LoadedProfile(string Url, string? Version, string Type, string Source, bool Usable, EvidenceItem Evidence);
