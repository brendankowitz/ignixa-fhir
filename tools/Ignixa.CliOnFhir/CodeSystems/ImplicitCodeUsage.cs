// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One FHIR <c>code</c> element with a FHIR-defined binding: its values carry no system in the data and none is attributed.</summary>
public sealed record ImplicitCodeUsage(string Element, int DistinctCodes, int Occurrences, IReadOnlyList<CodeCount> TopCodes);
