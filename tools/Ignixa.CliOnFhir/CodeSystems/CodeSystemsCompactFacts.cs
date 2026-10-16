// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Facts of <c>folder_code_systems</c> by default: one short line per system; <c>detail=full</c> gives <see cref="CodeSystemsFacts"/>.</summary>
/// <param name="SystemCount">Distinct code systems.</param>
/// <param name="Offset">Systems skipped.</param>
/// <param name="Returned">Systems in this page.</param>
/// <param name="Systems">This page, most codings first, then by URI.</param>
/// <param name="Terminology"><c>loaded</c> or <c>not loaded</c>: this tool never loads terminology itself unless asked (<c>withTerminology</c>).</param>
public sealed record CodeSystemsCompactFacts(int SystemCount, int Offset, int Returned, IReadOnlyList<CodeSystemSummary> Systems, string Terminology);
