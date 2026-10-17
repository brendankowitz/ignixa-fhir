// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Everything the folder's codings say about one code system, as written (the URI is folder data).</summary>
/// <param name="System">The system URI as written.</param>
/// <param name="Codes">Each distinct code with its occurrences, most frequent first, then by code.</param>
/// <param name="Codings">Codings with this system.</param>
/// <param name="Resources">Resources holding at least one of them.</param>
/// <param name="Elements">Codings per element, most first.</param>
public sealed record SystemTally(string System, IReadOnlyList<(string Code, int Count)> Codes, int Codings, int Resources, IReadOnlyList<ElementCount> Elements);
