// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>What a code system URI is known as.</summary>
/// <param name="Kind">
/// <c>external</c> (in the well-known table), <c>hl7-v2</c> or <c>hl7-v3</c> (HL7 Terminology v2 tables and v3 code systems),
/// <c>hl7-terminology</c> (another HL7 Terminology code system), <c>fhir</c> (a <c>http://hl7.org/fhir/</c> canonical) or <c>unknown</c>.
/// </param>
/// <param name="Name">A short name; null when unknown.</param>
/// <param name="Steward">The steward; null when unknown.</param>
/// <param name="PreferredUri">The preferred URI when the one written is an alias or an OID form; null when it is preferred or unknown.</param>
public sealed record SystemIdentity(string Kind, string? Name, string? Steward, string? PreferredUri);
