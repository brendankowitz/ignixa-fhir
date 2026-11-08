// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Outcome of a package activation.
/// </summary>
/// <remarks>
/// A failed activation carries only error issues and activated nothing. A successful activation is durable;
/// its warning issues describe follow-up work that is deferred (and logged and metered once by the pipeline)
/// or definitions that are not searchable yet.
/// </remarks>
public sealed record ActivationResult
{
    private ActivationResult(
        IReadOnlyList<ValidationIssue> issues,
        IReadOnlyList<string> pendingReindex,
        string? reindexJobId)
    {
        Issues = issues;
        PendingReindex = pendingReindex;
        ReindexJobId = reindexJobId;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Resource types with Pending search parameters from this activation.
    /// </summary>
    public IReadOnlyList<string> PendingReindex { get; }

    /// <summary>
    /// The reindex job started for, or already running ahead of, the Pending search parameters.
    /// </summary>
    public string? ReindexJobId { get; }

    public bool Success => Issues.All(issue => issue.Severity != ActivationIssueSeverity.Error);

    public static ActivationResult Activated(
        IReadOnlyList<string> pendingReindex,
        string? reindexJobId,
        IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Any(issue => issue.Severity == ActivationIssueSeverity.Error))
        {
            throw new ArgumentException("A durable activation cannot carry error issues.", nameof(issues));
        }

        return new(issues, pendingReindex, reindexJobId);
    }

    public static ActivationResult Failed(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0 || issues.Any(issue => issue.Severity != ActivationIssueSeverity.Error))
        {
            throw new ArgumentException("A failed activation carries only error issues, at least one.", nameof(issues));
        }

        return new(issues, [], null);
    }
}
