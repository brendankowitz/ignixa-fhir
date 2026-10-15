// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A concept's display and designation values folded for text search (<see cref="TextFolding"/>).</summary>
public sealed record FoldedConcept(Concept Concept, string? Display, IReadOnlyList<string> Designations);
