// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// Runs a terminology tool the same way for every tool: an unreadable folder or a request error fails the call; the store
/// is loaded on first use; a store with nothing loaded because every source failed fails the call with why; every answer
/// carries the store's load problems after its own issues.
/// </summary>
internal static class TerminologyCall
{
    /// <param name="session">The folder session.</param>
    /// <param name="answer">The tool's answer over the loaded state.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="evenWhenUnusable">
    /// Answer even when nothing could be loaded: for <c>terminology_load_package</c>, the remedy the unusable-store error names.
    /// </param>
    public static async Task<ToolEnvelope<TFacts?>> RunAsync<TFacts>(
        FolderSession session,
        Func<TerminologyState, Task<TerminologyAnswer<TFacts>>> answer,
        CancellationToken cancellationToken,
        bool evenWhenUnusable = false)
        where TFacts : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(answer);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<TFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            TerminologyState state = await session.Terminology.GetAsync(cancellationToken);
            if (state.IsUnusable && !evenWhenUnusable)
            {
                return EnvelopeAssembler.Create<TFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], state.FailureIssues(), cancellationToken);
            }

            TerminologyAnswer<TFacts> result = await answer(state);
            return EnvelopeAssembler.Create<TFacts?>(
                session,
                result.Facts,
                session.Catalog.BuiltAt,
                result.Evidence,
                result.Omitted,
                result.Absent,
                [.. result.Issues, .. session.Terminology.Current?.LoadIssues ?? state.LoadIssues],
                cancellationToken) with { Next = result.Next };
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<TFacts>(session, ex.Issue, cancellationToken);
        }
    }

    /// <summary>A synchronous answer.</summary>
    public static Task<ToolEnvelope<TFacts?>> RunAsync<TFacts>(FolderSession session, Func<TerminologyState, TerminologyAnswer<TFacts>> answer, CancellationToken cancellationToken)
        where TFacts : class =>
        RunAsync(session, state => Task.FromResult(answer(state)), cancellationToken);
}
