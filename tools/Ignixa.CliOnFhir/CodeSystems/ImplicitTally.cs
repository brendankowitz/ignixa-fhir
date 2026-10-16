// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One FHIR <c>code</c>-typed element with a FHIR-defined binding (for example <c>Patient.gender</c>), whose values carry no system.</summary>
/// <param name="Element">The element path.</param>
/// <param name="System">The code system the element's binding implies, or null when it implies none (R4 <c>Task.intent</c>).</param>
/// <param name="Codes">Each distinct value with its occurrences, most frequent first, then by value.</param>
/// <param name="Occurrences">Values in all.</param>
public sealed record ImplicitTally(string Element, string? System, IReadOnlyList<(string Code, int Count)> Codes, int Occurrences);
