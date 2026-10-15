// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Session;

/// <summary>
/// Completes a tool response with what every tool reports the same way: the session's cache problems and
/// the freshness of the data it answered from.
/// </summary>
public static class EnvelopeAssembler
{
    /// <summary>
    /// Builds the envelope for <paramref name="facts"/>. Counts changed files afresh (reused briefly, see
    /// <see cref="FolderSession.CountStaleFiles"/>) and appends the session's cache issues and, when the folder
    /// could not be re-listed, a <c>freshness-unavailable</c> warning after the caller's <paramref name="issues"/>.
    /// Call it after the response's indexes were requested so their cache problems are included.
    /// </summary>
    /// <param name="builtAt">When the catalog or the newest index the response was answered from finished building.</param>
    public static ToolEnvelope<TFacts> Create<TFacts>(
        FolderSession session,
        TFacts facts,
        DateTimeOffset builtAt,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<OmittedItem> omitted,
        IReadOnlyList<AbsentItem> absent,
        IEnumerable<IssueItem> issues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(issues);

        StaleFilesCount stale = session.CountStaleFiles(cancellationToken);
        List<IssueItem> allIssues = [.. issues, .. session.CacheIssues];
        if (stale.Unavailable is { } reason)
        {
            allIssues.Add(new IssueItem("warning", "freshness-unavailable", $"Could not check whether files changed since they were read, so freshness.staleFiles is unknown: {reason}", Location: null));
        }

        return new ToolEnvelope<TFacts>(facts, evidence, omitted, absent, allIssues, new FreshnessInfo(builtAt, stale.Count));
    }

    /// <summary>The issue form of a catalog or index diagnostic.</summary>
    public static IssueItem ToIssue(CatalogDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new IssueItem(
            diagnostic.Code == "duplicate-id" ? "warning" : "error",
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Line is int line ? $"{diagnostic.FilePath}:{line}" : diagnostic.FilePath);
    }
}
