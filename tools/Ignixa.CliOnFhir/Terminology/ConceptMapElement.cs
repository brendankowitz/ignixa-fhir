// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One <c>group.element</c>: a source code and its targets.</summary>
/// <param name="Code">The source code.</param>
/// <param name="NoMap">R5 <c>noMap</c>: the map states there is no target.</param>
/// <param name="Targets">The targets in source order.</param>
public sealed record ConceptMapElement(string Code, bool NoMap, IReadOnlyList<ConceptMapTarget> Targets);
