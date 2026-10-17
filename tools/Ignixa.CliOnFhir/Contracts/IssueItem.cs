// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>An OperationOutcome-style problem with the request or with data in scope.</summary>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>information</c>.</param>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Diagnostics">Human-readable detail.</param>
/// <param name="Location">File (and line) the issue refers to, when there is one.</param>
public sealed record IssueItem(string Severity, string Code, string Diagnostics, string? Location);
