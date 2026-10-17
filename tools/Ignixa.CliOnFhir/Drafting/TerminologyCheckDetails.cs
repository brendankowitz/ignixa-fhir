// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>Whether a coded value of a draft is a code of its system, and whether its display is that code's.</summary>
/// <param name="System">The system, as given.</param>
/// <param name="Code">The code, as given.</param>
/// <param name="Result"><c>valid</c>, <c>invalid</c> or <c>unknown</c>, as <c>term validate</c> answers it.</param>
/// <param name="Reason">Why: <c>code-in-system</c>, <c>code-not-in-version</c>, <c>system-not-loaded</c>, <c>display-mismatch</c>, <c>rejected-by-tx-server</c>, ...</param>
/// <param name="DisplayMatches">Whether the display given matches the code's display or a designation; null when none was given or none is known.</param>
/// <param name="ExpectedDisplay">The code's display in the code system (terminology content), when known.</param>
/// <param name="Source">What answered: the local store or the terminology server; null when nothing could.</param>
public sealed record TerminologyCheckDetails(string System, string Code, string Result, string Reason, bool? DisplayMatches, string? ExpectedDisplay, AnswerSource? Source);
