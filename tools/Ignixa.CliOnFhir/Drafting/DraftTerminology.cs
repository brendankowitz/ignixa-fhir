// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// Checks each coded value of a draft against its code system, as <c>term validate --system</c> does: the code must be in the system,
/// and a display given must be the code's display or one of its designations (a <c>warning</c> with the expected display in the
/// facts, not a failure). The session's terminology store answers; a system it does not have, or holds only in part without the
/// code (a fragment), is asked of the terminology server only when <c>--tx-server</c> was given, with the system and code alone (the display is compared here, so no text of the draft
/// leaves the machine). A system neither has is <c>unknown</c> with a <c>system-not-loaded</c> warning, never valid.
/// </summary>
internal static class DraftTerminology
{
    public const string SystemNotLoadedCode = "system-not-loaded";

    /// <summary>One check per coding, in order, and the issues: the answers' own notes and one warning per system not loaded.</summary>
    public static async Task<(List<DraftCheck> Checks, List<IssueItem> Issues)> CheckAsync(FolderSession session, TerminologyState state, IReadOnlyList<DraftCoding> codings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(codings);

        List<DraftCheck> checks = [];
        List<IssueItem> issues = [];
        foreach (DraftCoding coding in codings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (TermValidateFacts facts, IReadOnlyList<IssueItem> notes) = await AnswerAsync(session, state, coding, cancellationToken);

            // The answer's own display warning points at the term tool's facts; the draft's says where its expected display is.
            issues.AddRange(notes.Where(note => note.Code != TermValidate.DisplayMismatch));
            // A local answer has compared the display with the designations too; a remote one was asked without it.
            bool? displayMatches = facts.DisplayMatches ?? (coding.Display is null || facts.Display is null ? null : Same(coding.Display, facts.Display));
            string outcome = Outcome(facts, displayMatches);
            if (displayMatches == false)
            {
                issues.Add(new IssueItem("warning", TermValidate.DisplayMismatch, $"The display given for {TerminologyNames.Coded(coding.System, coding.Code)} at {coding.Element} is not that code's display or one of its designations; the expected one is in details.expectedDisplay of that element's terminology check in facts.checks.", Location: null));
            }

            checks.Add(new DraftCheck(
                "terminology",
                coding.Element,
                outcome,
                new TerminologyCheckDetails(coding.System, coding.Code, outcome == DraftCheck.Warning ? TermValidate.Valid : facts.Result, displayMatches == false ? TermValidate.DisplayMismatch : facts.Reason, displayMatches, facts.Display, facts.Source)));
        }

        issues.AddRange(NotLoaded(checks));
        return (checks, issues);
    }

    private static async Task<(TermValidateFacts Facts, IReadOnlyList<IssueItem> Notes)> AnswerAsync(FolderSession session, TerminologyState state, DraftCoding coding, CancellationToken cancellationToken)
    {
        TerminologyAnswer<TermValidateFacts> local = TermValidate.AgainstSystem(state.Store, coding.System, coding.Code, coding.Display, version: null);
        if (session.Terminology.Options.TxServer is not { } tx || !(TxFallback.ValidationMissing(local.Facts.Reason) || local.Facts.Reason == ConceptQuery.CodeNotInLoadedContent))
        {
            return (local.Facts, local.Issues);
        }

        try
        {
            TerminologyAnswer<TermValidateFacts> remote = await TxAnswers.ValidateAsync(tx, coding.Code, coding.System, display: null, valueSet: null, version: null, TxFallback.Why(local.Absent, local.Facts.Reason), session.UtcNow(), cancellationToken);
            return (remote.Facts, remote.Issues);
        }
        catch (ToolRequestException ex)
        {
            // An unreachable or failing server decides nothing: the code stays unchecked, with the server's issue code.
            string reason = $"{StoreTerminologyService.TxUnavailableReason} ({TerminologyNames.Token(ex.Issue.Code)})";
            return (local.Facts with { Reason = reason }, [.. local.Issues, new IssueItem("warning", StoreTerminologyService.TxUnavailableReason, $"The terminology server could not check {TerminologyNames.Coded(coding.System, coding.Code)} at {coding.Element} ({TerminologyNames.Token(ex.Issue.Code)}), so it is unknown.", Location: null)]);
        }
    }

    // A code of the system with a display that is not its own still passes, with a warning; a code the system lacks fails.
    private static string Outcome(TermValidateFacts facts, bool? displayMatches) => facts.Result switch
    {
        TermValidate.Valid => displayMatches == false ? DraftCheck.Warning : DraftCheck.Pass,
        TermValidate.Invalid when facts.Reason == TermValidate.DisplayMismatch => DraftCheck.Warning,
        TermValidate.Invalid => DraftCheck.Fail,
        _ => DraftCheck.Unknown,
    };

    private static IEnumerable<IssueItem> NotLoaded(List<DraftCheck> checks) => checks
        .Where(check => check.Details is TerminologyCheckDetails { Result: TermValidate.Unknown } details && TxFallback.SystemMissing(details.Reason))
        .GroupBy(check => ((TerminologyCheckDetails)check.Details).System, StringComparer.Ordinal)
        .Select(group => new IssueItem(
            "warning",
            SystemNotLoadedCode,
            $"CodeSystem {TerminologyNames.Canonical(group.Key, null)} is not loaded, so the draft's codes from it ({string.Join(", ", group.Select(check => check.Element))}) were not checked and the draft is not reported valid. Load a package that carries it (--terminology), or check it with a terminology server (--tx-server).",
            Location: null));

    private static bool Same(string left, string right) => string.Equals(TextFolding.Fold(left), TextFolding.Fold(right), StringComparison.Ordinal);
}
