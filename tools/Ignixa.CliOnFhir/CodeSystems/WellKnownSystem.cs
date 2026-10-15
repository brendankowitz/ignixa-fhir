// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>An external code system with a short name, its steward, and the other identifiers it is known by.</summary>
/// <param name="PreferredUri">The URI FHIR and HL7 Terminology prefer for it.</param>
/// <param name="Name">Short name, for example <c>SNOMED CT</c>.</param>
/// <param name="Steward">The organization that maintains it.</param>
/// <param name="Aliases">Other URIs used for it, which are not preferred.</param>
/// <param name="Oids">Its OIDs (without <c>urn:oid:</c>).</param>
public sealed record WellKnownSystem(string PreferredUri, string Name, string Steward, IReadOnlyList<string> Aliases, IReadOnlyList<string> Oids);
