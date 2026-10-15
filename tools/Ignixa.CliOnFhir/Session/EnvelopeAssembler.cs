// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;

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

    /// <summary>
    /// An <c>error</c> issue when the folder cannot be read at all (missing, unmounted, unreadable), otherwise null. Tools
    /// check this first so an unreachable folder is reported as a request problem rather than an unhandled exception.
    /// </summary>
    public static IssueItem? FolderProblem(FolderSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            _ = session.Catalog;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IssueItem("error", "folder-unavailable", $"The folder '{session.RootPath}' cannot be read: {ex.Message}", Location: null);
        }
    }

    /// <summary>
    /// The envelope of a call that could not be answered: null facts and the problem as an issue. An unreadable folder is
    /// reported instead of <paramref name="issue"/>, with no freshness since no catalog exists.
    /// </summary>
    public static ToolEnvelope<TFacts?> Failure<TFacts>(FolderSession session, IssueItem issue, CancellationToken cancellationToken)
        where TFacts : class
    {
        ArgumentNullException.ThrowIfNull(session);

        return FolderProblem(session) is { } folderProblem
            ? new ToolEnvelope<TFacts?>(null, [], [], [], [folderProblem], new FreshnessInfo(IndexBuiltAt: null, StaleFiles: null))
            : Create<TFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [issue], cancellationToken);
    }

    /// <summary>The issue form of a catalog or index diagnostic.</summary>
    public static IssueItem ToIssue(CatalogDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        string severity = diagnostic.Code switch
        {
            "duplicate-id" => "warning",
            BundleReferenceResolver.UnresolvedCode => "information",
            _ => "error",
        };

        return new IssueItem(
            severity,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Line is int line ? $"{diagnostic.FilePath}:{line}" : diagnostic.FilePath);
    }
}
