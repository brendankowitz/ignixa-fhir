// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;
using Ignixa.CliOnFhir.Validation;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// <c>draft_resource</c> / <c>cli-on-fhir draft</c>: composes a MedicationRequest, ServiceRequest or Observation from a small typed
/// input (never raw FHIR), dated by the session's <see cref="AsOf"/> where the input does not date it, and checks it without ever
/// passing a check it could not run: the validator (core definition and the requested profile, bindings by the loaded terminology),
/// each coded value against its code system, and each reference against the folder. The tool composes; it invents no clinical
/// content, so <c>derived</c> stays null. The caller's text (a note, a display) is resource content: it is in <c>facts.resource</c>
/// and nowhere else. With <c>out</c> the draft is also written to a file outside the folder, unless it is invalid.
/// </summary>
public static class DraftService
{
    public const int DefaultTimeoutSeconds = 30;

    public const int MaxTimeoutSeconds = 3600;

    /// <summary>
    /// The draft <paramref name="request"/> describes. A request that cannot be answered (input that is not the type's schema, an
    /// unsupported FHIR version, a profile that is not loaded, an output path in the folder, a run that takes too long) is an
    /// envelope with null facts and an <c>error</c> issue; a draft that fails a check is a draft with verdict <c>invalid</c>.
    /// </summary>
    /// <param name="asOf">The session's instant.</param>
    /// <param name="logger">Receives the details of a <c>draft-internal-error</c>.</param>
    public static Task<ToolEnvelope<DraftFacts?>> RunAsync(FolderSession session, DraftRequest request, AsOfClock asOf, ILogger logger, CancellationToken cancellationToken) =>
        RunAsync(session, request, asOf, logger, seams: null, timeoutSignal: CancellationToken.None, cancellationToken);

    /// <param name="seams">Where a test swaps the output directory.</param>
    /// <param name="timeoutSignal">Stops the run as its timeout does; a test passes one already cancelled.</param>
    internal static async Task<ToolEnvelope<DraftFacts?>> RunAsync(FolderSession session, DraftRequest request, AsOfClock asOf, ILogger logger, DraftOutputSeams? seams, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(asOf);
        ArgumentNullException.ThrowIfNull(logger);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<DraftFacts>(session, folderProblem, cancellationToken);
        }

        List<IssueItem> notes = [];
        try
        {
            return await WithinTimeAsync(session, request, asOf, notes, seams, timeoutSignal, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return notes.Count == 0
                ? EnvelopeAssembler.Failure<DraftFacts>(session, ex.Issue, cancellationToken)
                : EnvelopeAssembler.Create<DraftFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [ex.Issue, .. notes], cancellationToken);
        }
#pragma warning disable CA1031 // The boundary of the verb: anything unexpected is logged in full and reported as an error, never as a success.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "The draft failed unexpectedly.");
            var error = new IssueItem("error", "draft-internal-error", $"The draft failed unexpectedly with {ex.GetType().Name} (digest {FhirIds.Digest(ex.Message)}); this is a defect of the tool, not of the request, and its details are in the log on stderr.", Location: null);
            return EnvelopeAssembler.Create<DraftFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [error, .. notes], cancellationToken);
        }
    }

    private static async Task<ToolEnvelope<DraftFacts?>> WithinTimeAsync(FolderSession session, DraftRequest request, AsOfClock asOf, List<IssueItem> notes, DraftOutputSeams? seams, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        int seconds = request.TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (seconds is < 1 or > MaxTimeoutSeconds)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'timeout' must be 1 to {MaxTimeoutSeconds} seconds, not {seconds}.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSignal);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            return await ExecuteAsync(session, request, asOf, notes, seams, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw ToolRequestException.Error("draft-timeout", $"The draft was not checked within {seconds} seconds and was stopped, and nothing was written. A first draft loads the terminology packages and builds the validation checks, and a terminology server can be slow; raise the timeout (up to {MaxTimeoutSeconds}).");
        }
    }

    private static async Task<ToolEnvelope<DraftFacts?>> ExecuteAsync(FolderSession session, DraftRequest request, AsOfClock clock, List<IssueItem> notes, DraftOutputSeams? seams, CancellationToken cancellationToken)
    {
        CheckVersion(session);
        if (!DraftBuilder.ResourceTypes.Contains(request.ResourceType, StringComparer.Ordinal))
        {
            throw ToolRequestException.Error("invalid-parameter", $"A draft is a {string.Join(", ", DraftBuilder.ResourceTypes)}; '{Views.ViewIssues.Word(request.ResourceType)}' is not one of them.");
        }

        AsOfResolution asOf = clock.Resolve();
        List<IssueItem> issues = [.. asOf.Issues];
        JsonObject input = DraftInput.Read(request.Input);
        DraftContent content = DraftBuilder.Build(request.ResourceType, input, request.Profile, asOf.Value);
        string id = (string)content.Resource["id"]!;
        OutputTarget? target = request.Out is { } directory ? DraftOutput.Target(session.RootPath, directory, request.ResourceType, id, request.Overwrite) : null;

        // The terminology store first (the folder's, then the configured or core packages), then each requested package.
        TerminologyState terminology = await session.Terminology.GetAsync(cancellationToken);
        ProfileSet profiles = await DraftValidation.ProfilesAsync(session, request.Packages ?? [], cancellationToken);
        LoadedProfile? profile = request.Profile is { } canonical ? DraftValidation.Profile(profiles, canonical, request.ResourceType) : null;
        terminology = session.Terminology.Current ?? terminology;

        cancellationToken.ThrowIfCancellationRequested();
        (DraftCheck validation, IssueItem? validationNote) = DraftValidation.Check(session, profiles, profile, content.Resource, request.ResourceType, id);
        (List<DraftCheck> codes, List<IssueItem> codeIssues) = await DraftTerminology.CheckAsync(session, terminology, content.Codings, cancellationToken);
        (List<DraftCheck> references, List<IssueItem> referenceIssues, List<EvidenceItem> evidence) = DraftReferences.Check(session, content.References);
        List<DraftCheck> checks = [validation, .. codes, .. references];
        string verdict = DraftFacts.VerdictOf(checks);

        issues.AddRange(referenceIssues);
        if (validationNote is not null)
        {
            issues.Add(validationNote);
        }

        issues.AddRange(codeIssues.Distinct());
        issues.AddRange(profiles.Problems);
        issues.AddRange(terminology.LoadIssues);

        DraftFileFacts? file = null;
        if (target is not null)
        {
            if (verdict == DraftFacts.Invalid)
            {
                issues.Add(new IssueItem("warning", "out-not-written", $"The draft is invalid, so it was not written to '{target.Path}'; correct the input and draft it again.", Location: null));
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                file = await DraftOutput.WriteAsync(target, content.Resource, notes, seams, cancellationToken);
                issues.AddRange(notes);
                if (verdict == DraftFacts.Unknown)
                {
                    issues.Add(WrittenUnknown(file, checks));
                }
            }
        }

        var facts = new DraftFacts(content.Resource, verdict, checks, asOf.Value, file);
        List<EvidenceItem> cited = [.. evidence.Distinct()];

        // A draft is answered whole or not at all: its input is small enough that this holds on the CLI, and a smaller response cap
        // must refuse it rather than cut the resource.
        if (EnvelopeAssembler.MaxSize<DraftFacts?>(session, facts, cited, [], [], issues, []) > session.MaxResponseBytes)
        {
            throw ToolRequestException.Error("draft-too-large", $"The draft and its checks do not fit the response limit of {session.MaxResponseBytes} bytes{(file is null ? string.Empty : $"; it was written to '{file.Path}'")}. Shorten its text (note, dosageText).");
        }

        return EnvelopeAssembler.Create<DraftFacts?>(session, facts, session.Catalog.BuiltAt, cited, [], [], issues, cancellationToken);
    }

    // Only an invalid draft is withheld; one written with checks that did not conclude says which, so the file is not taken as checked.
    private static IssueItem WrittenUnknown(DraftFileFacts file, List<DraftCheck> checks)
    {
        IEnumerable<string> open = checks.Where(check => check.Outcome == DraftCheck.Unknown).Select(check => check.Details switch
        {
            TerminologyCheckDetails { Reason: TermValidate.DisplayMismatch or DraftTerminology.DisplayNotCheckedCode } => $"{check.Element} (its display could not be confirmed)",
            TerminologyCheckDetails details => $"{check.Element} ({TerminologyNames.Token(details.Reason)})",
            _ => $"{check.Check} of {check.Element}",
        });
        return new IssueItem("warning", "out-written-unknown", $"The draft was written to '{file.Path}' although its verdict is unknown, not valid: {string.Join("; ", open)} could not be checked (their issues say why). Check them before the file is used.", Location: null);
    }

    // R4 and R4B define these three resources alike. STU3 has no ServiceRequest and puts the encounter in 'context'; R5 makes the
    // medication and the reasons CodeableReferences: drafts there would be other resources, so they are refused, not guessed.
    private static void CheckVersion(FolderSession session)
    {
        if (session.Schema.Version is not (FhirVersion.R4 or FhirVersion.R4B))
        {
            throw ToolRequestException.Error("draft-unsupported-version", $"Drafts are built for FHIR R4 and R4B; this folder is read as {session.Schema.Version}, whose MedicationRequest, ServiceRequest and Observation differ in shape (STU3 has no ServiceRequest and names the encounter 'context'; R5 uses CodeableReference for the medication and the reasons).");
        }
    }
}
