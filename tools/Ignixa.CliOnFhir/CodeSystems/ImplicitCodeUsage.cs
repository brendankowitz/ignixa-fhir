// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One FHIR <c>code</c> element with a FHIR-defined binding: its values carry no system in the data.</summary>
/// <param name="Element">The element path.</param>
/// <param name="System">
/// The code system the element's binding implies, which Ignixa's indexer attaches to its values; null when the binding implies
/// none (its ValueSet draws on several code systems, for example R4 <c>Task.intent</c>, or binds no single one).
/// </param>
/// <param name="DistinctCodes">Distinct values.</param>
/// <param name="Occurrences">Values in all.</param>
/// <param name="TopCodes">The most frequent values, bare codes.</param>
public sealed record ImplicitCodeUsage(string Element, string? System, int DistinctCodes, int Occurrences, IReadOnlyList<CodeCount> TopCodes);
