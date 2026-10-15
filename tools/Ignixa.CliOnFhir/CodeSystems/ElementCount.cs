// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>How many codings (or codes) one element holds.</summary>
/// <param name="Element">The element path, for example <c>Observation.code</c> or <c>Observation.component.code</c> (no array indices; <c>.coding</c> left out).</param>
/// <param name="Count">Occurrences.</param>
public sealed record ElementCount(string Element, int Count);
