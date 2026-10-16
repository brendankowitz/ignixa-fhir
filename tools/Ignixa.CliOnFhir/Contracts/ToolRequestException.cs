// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// A request the tool cannot answer (malformed query, unsupported feature, unknown resource type). Carries the
/// problem as an issue so the boundary can report it in the envelope instead of letting a stack trace escape.
/// </summary>
public sealed class ToolRequestException(IssueItem issue) : Exception(issue?.Diagnostics)
{
    public IssueItem Issue { get; } = issue ?? throw new ArgumentNullException(nameof(issue));

    /// <summary>For a remote request, whether asking again may succeed; <see cref="TransientFailure.None"/> otherwise.</summary>
    public TransientFailure Transient { get; init; }

    public static ToolRequestException Error(string code, string diagnostics, TransientFailure transient = TransientFailure.None) =>
        new(new IssueItem("error", code, diagnostics, Location: null)) { Transient = transient };
}
