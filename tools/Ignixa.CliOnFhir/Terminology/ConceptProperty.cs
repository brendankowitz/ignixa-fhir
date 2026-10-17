// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A concept property as the CodeSystem gives it.</summary>
/// <param name="Code">The property code, for example <c>status</c> or <c>notSelectable</c>.</param>
/// <param name="Value">The value as text: a code, <c>system|code</c> for a Coding, or the literal of a string, number, boolean or date.</param>
/// <param name="Type">The JSON value type: code, Coding, string, integer, boolean, dateTime or decimal.</param>
public sealed record ConceptProperty(string Code, string Value, string Type);
