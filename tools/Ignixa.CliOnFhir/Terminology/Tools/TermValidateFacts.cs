// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology</c> validate.</summary>
/// <param name="Result"><c>valid</c>, <c>invalid</c> or <c>unknown</c> (the loaded terminology cannot tell; <paramref name="Reason"/> says why).</param>
/// <param name="Reason">
/// <c>code-in-system</c>, <c>code-in-value-set</c>, <c>code-not-in-version</c>, <c>not-in-value-set</c>,
/// <c>code-not-in-loaded-content</c>, <c>expansion-incomplete</c>, <c>system-not-loaded</c>, <c>version-not-loaded</c>,
/// <c>value-set-not-loaded</c>, <c>system-ambiguous</c> (the code is in the ValueSet under more than one system and none was given),
/// <c>display-mismatch</c> (result invalid: the code is right but the display matches neither its display nor a designation,
/// as R4 $validate-code answers it), <c>rejected-by-tx-server</c> (result invalid), <c>not-found-by-tx-server</c> or <c>not-checked-by-tx-server</c> (result unknown: the server does not know the code, or does not have the code system or ValueSet).
/// </param>
/// <param name="System">The system checked against (given, or the one the ValueSet holds the code under).</param>
/// <param name="Version">The code system version that answered.</param>
/// <param name="Code">The code checked.</param>
/// <param name="ValueSet">The ValueSet checked against, when given.</param>
/// <param name="ValueSetVersion">Its version.</param>
/// <param name="Source">What answered.</param>
/// <param name="Display">The code's display in the code system (or the ValueSet), when known: terminology content.</param>
/// <param name="ProvidedDisplay">The display given to check.</param>
/// <param name="DisplayMatches">Whether the given display matches the display or a designation (case- and accent-insensitive); null when none was given or none is known.</param>
/// <param name="Inactive">Whether the concept is inactive; null when not known.</param>
public sealed record TermValidateFacts(
    string Result,
    string Reason,
    string? System,
    string? Version,
    string Code,
    string? ValueSet,
    string? ValueSetVersion,
    AnswerSource? Source,
    string? Display,
    string? ProvidedDisplay,
    bool? DisplayMatches,
    bool? Inactive);
