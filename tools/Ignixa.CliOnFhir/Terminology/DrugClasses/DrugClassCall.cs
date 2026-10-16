// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Remote;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>
/// Runs a drug-class operation: an unreadable folder, a request error, a session without <c>--rxclass-server</c> or any
/// failed RxClass request fails the whole call (no partial answer). The terminology store is not loaded: drug classes
/// come from RxClass alone.
/// </summary>
internal static class DrugClassCall
{
    public const string NotEnabledCode = "rxclass-not-enabled";

    public static async Task<ToolEnvelope<TFacts?>> RunAsync<TFacts>(FolderSession session, Func<RxClassReads, Task<TerminologyAnswer<TFacts>>> answer, CancellationToken cancellationToken)
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
            RxClassClient client = session.Terminology.Options.RxClass ?? throw ToolRequestException.Error(
                NotEnabledCode,
                "Drug classes come from NLM RxClass, which this session does not use: start the server with --rxclass-server https://rxnav.nlm.nih.gov/REST (not with --offline). Without it, find drugs by RxNorm code or display text.");
            TerminologyAnswer<TFacts> result = await answer(new RxClassReads(client, session.Terminology.Overlap));
            return EnvelopeAssembler.Create<TFacts?>(session, result.Facts, session.Catalog.BuiltAt, result.Evidence, result.Omitted, result.Absent, result.Issues, cancellationToken) with { Next = result.Next };
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<TFacts>(session, ex.Issue, cancellationToken);
        }
    }
}
