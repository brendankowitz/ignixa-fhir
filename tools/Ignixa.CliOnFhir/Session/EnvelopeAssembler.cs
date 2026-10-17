// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
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
    /// <see cref="FolderSession.CountStaleFiles"/>) and appends the session's cache issues (warnings every time, information events once, see <see cref="FolderSession.TakeCacheIssues"/>) and, when the folder
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

        return Create(session, facts, builtAt, evidence, omitted, absent, issues, session.AsOfClock.ForEnvelope(), cancellationToken);
    }

    /// <summary>
    /// <see cref="Create{TFacts}(FolderSession, TFacts, DateTimeOffset, IReadOnlyList{EvidenceItem}, IReadOnlyList{OmittedItem}, IReadOnlyList{AbsentItem}, IEnumerable{IssueItem}, CancellationToken)"/>
    /// stating <paramref name="asOf"/>, the instant this call's answer is anchored to (its own <c>asOf</c>, or a tool's default),
    /// rather than the session's. Set it here, before a response is fitted to its budget, so the size measured is the size sent.
    /// </summary>
    public static ToolEnvelope<TFacts> Create<TFacts>(
        FolderSession session,
        TFacts facts,
        DateTimeOffset builtAt,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<OmittedItem> omitted,
        IReadOnlyList<AbsentItem> absent,
        IEnumerable<IssueItem> issues,
        AsOf? asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(issues);

        StaleFilesCount stale = session.CountStaleFiles(cancellationToken);
        List<IssueItem> allIssues = [.. issues, .. session.TakeCacheIssues()];
        if (stale.Unavailable is { } reason)
        {
            allIssues.Add(new IssueItem("warning", "freshness-unavailable", $"Could not check whether files changed since they were read, so freshness.staleFiles is unknown: {reason}", Location: null));
        }

        return new ToolEnvelope<TFacts>(facts, evidence, omitted, absent, allIssues, new FreshnessInfo(builtAt, stale.Count)) { AsOf = asOf };
    }

    /// <summary>
    /// The most the envelope <see cref="Create"/> would build from these parts can measure, without building it: every cache
    /// issue the session holds (whether or not this response would deliver it), room for a <c>freshness-unavailable</c>
    /// warning, and the widest freshness. Unlike <see cref="Create"/> it consumes no cache event, so a caller fitting a
    /// response under the cap can measure as often as it needs and create once.
    /// </summary>
    public static int MaxSize<TFacts>(
        FolderSession session,
        TFacts facts,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<OmittedItem> omitted,
        IReadOnlyList<AbsentItem> absent,
        IEnumerable<IssueItem> issues,
        IReadOnlyList<NextCall> next)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(issues);

        var freshnessReserve = new IssueItem("warning", "freshness-unavailable", new string('x', 512), Location: null);
        var envelope = new ToolEnvelope<TFacts>(facts, evidence, omitted, absent, [.. issues, .. session.CacheIssues, freshnessReserve], new FreshnessInfo(DateTimeOffset.MaxValue, int.MaxValue)) { Next = next, AsOf = AsOf.SizingReserve };
        return EnvelopeJson.SizeOf(envelope);
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
            ? new ToolEnvelope<TFacts?>(null, [], [], [], [folderProblem], new FreshnessInfo(IndexBuiltAt: null, StaleFiles: null)) { AsOf = session.AsOfClock.WithoutReading() }
            : Create<TFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [issue], cancellationToken);
    }

    /// <summary>The issue form of a catalog or index diagnostic.</summary>
    public static IssueItem ToIssue(CatalogDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        string severity = diagnostic.Code switch
        {
            "duplicate-id" or FhirIds.InvalidCode => "warning",
            BundleReferenceResolver.UnresolvedCode or BundleReferenceResolver.CrossScopeCode => "information",
            _ => "error",
        };

        return new IssueItem(
            severity,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Line is int line ? $"{diagnostic.FilePath}:{line}" : diagnostic.FilePath);
    }
}
