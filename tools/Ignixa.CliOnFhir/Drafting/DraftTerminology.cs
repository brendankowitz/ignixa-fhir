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
/// and a display given must be the code's display or one of its designations, the expected display in the facts. On the draft's
/// primary coding (<see cref="DraftCoding.Primary"/>) a display the store finds wrong fails the draft, and one only the server
/// answered for is unknown; on any other coding it is a warning. The session's terminology store answers; a system it does not have,
/// or holds only in part without the code (a fragment), is asked of the terminology server only when <c>--tx-server</c> was given,
/// with the system and code alone (the display is compared here, so no text of the draft leaves the machine). A system neither has
/// is <c>unknown</c> with a <c>system-not-loaded</c> warning, never valid; every unknown check has an issue saying why.
/// </summary>
internal static class DraftTerminology
{
    public const string SystemNotLoadedCode = "system-not-loaded";

    /// <summary>A display given for a code its code system gives no display or designation: nothing to compare it with.</summary>
    public const string DisplayNotCheckedCode = "display-not-checked";

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
            bool remote = facts.Source?.Kind == AnswerSource.TxServerKind;

            // A concept the code system gives neither a display nor a designation has nothing a display can be compared with.
            bool unconfirmed = displayMatches == false && !remote && facts.Display is null
                && ConceptQuery.Find(state.Store, coding.System, null, coding.Code).Concept is { Designations.Count: 0 };
            if (unconfirmed)
            {
                displayMatches = null;
            }

            string outcome = unconfirmed && coding.Primary ? DraftCheck.Unknown : Outcome(facts, displayMatches, coding.Primary, remote);
            if (displayMatches == false)
            {
                issues.Add(Mismatch(coding, outcome));
            }
            else if (unconfirmed)
            {
                issues.Add(NothingToCompare(coding));
            }

            string result = outcome switch
            {
                DraftCheck.Pass or DraftCheck.Warning => TermValidate.Valid,
                DraftCheck.Fail => TermValidate.Invalid,
                _ => TermValidate.Unknown,
            };
            checks.Add(new DraftCheck(
                "terminology",
                coding.Element,
                outcome,
                new TerminologyCheckDetails(coding.System, coding.Code, result, displayMatches == false ? TermValidate.DisplayMismatch : unconfirmed ? DisplayNotCheckedCode : facts.Reason, displayMatches, facts.Display, facts.Source)));
        }

        issues.AddRange(Unexplained(checks, issues));
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

    // A display that is not its code's fails the draft's primary coding when the store compared it (the two may name different things:
    // another strength of a drug), and leaves it unknown when only the server answered, which knows the preferred display alone and
    // was not sent the display; on any other coding it is a warning. A code the system lacks fails.
    private static string Outcome(TermValidateFacts facts, bool? displayMatches, bool primary, bool remote)
    {
        bool codeValid = facts.Result == TermValidate.Valid || (facts.Result == TermValidate.Invalid && facts.Reason == TermValidate.DisplayMismatch);
        if (!codeValid)
        {
            return facts.Result == TermValidate.Invalid ? DraftCheck.Fail : DraftCheck.Unknown;
        }

        return displayMatches != false ? DraftCheck.Pass : !primary ? DraftCheck.Warning : remote ? DraftCheck.Unknown : DraftCheck.Fail;
    }

    // Neither display is repeated: the given one is the caller's text, the expected one is in the check's details.
    private static IssueItem Mismatch(DraftCoding coding, string outcome)
    {
        string coded = TerminologyNames.Coded(coding.System, coding.Code);
        string where = $"the expected one is in details.expectedDisplay of the terminology check of {coding.Element} in facts.checks";
        return outcome switch
        {
            DraftCheck.Fail => new IssueItem("error", TermValidate.DisplayMismatch, $"The display given for {coded} at {coding.Element}, which says what the draft is, is not that code's display or one of its designations, so the code and the display may name different things and the draft is invalid; {where}. Correct the code or the display, or leave display out.", Location: null),
            DraftCheck.Unknown => new IssueItem("warning", TermValidate.DisplayMismatch, $"The display given for {coded} at {coding.Element}, which says what the draft is, is not the preferred display the terminology server gave ({where}); the server knows no designations here, so the draft is not reported valid. Correct the code or the display, or leave display out.", Location: null),
            _ => new IssueItem("warning", TermValidate.DisplayMismatch, $"The display given for {coded} at {coding.Element} is not that code's display or one of its designations; {where}. Correct it, or leave display out.", Location: null),
        };
    }

    private static IssueItem NothingToCompare(DraftCoding coding) => coding.Primary
        ? new IssueItem("warning", DisplayNotCheckedCode, $"The code system gives {TerminologyNames.Coded(coding.System, coding.Code)} no display and no designation, so the display given at {coding.Element}, which says what the draft is, could not be confirmed and the draft is not reported valid. Leave display out, or load the code system's full content.", Location: null)
        : new IssueItem("information", DisplayNotCheckedCode, $"The code system gives {TerminologyNames.Coded(coding.System, coding.Code)} no display and no designation, so the display given at {coding.Element} was not compared.", Location: null);

    // Every unknown check is explained by an issue: a system not loaded, once per system; a fragment without the code; and the server's
    // own notes (not found, not checked, unavailable). Any other reason gets a terminology-unknown warning, so none passes unseen.
    private static IEnumerable<IssueItem> Unexplained(List<DraftCheck> checks, List<IssueItem> issues)
    {
        List<(DraftCheck Check, TerminologyCheckDetails Details)> unknown = [.. checks
            .Where(check => check.Outcome == DraftCheck.Unknown && check.Details is TerminologyCheckDetails)
            .Select(check => (check, (TerminologyCheckDetails)check.Details))];
        foreach (IssueItem issue in NotLoaded(unknown.Where(item => TxFallback.SystemMissing(item.Details.Reason)).ToList()))
        {
            yield return issue;
        }

        foreach (IGrouping<string, (DraftCheck Check, TerminologyCheckDetails Details)> fragment in unknown.Where(item => item.Details.Reason == ConceptQuery.CodeNotInLoadedContent).GroupBy(item => item.Details.System, StringComparer.Ordinal))
        {
            yield return new IssueItem(
                "warning",
                ConceptQuery.CodeNotInLoadedContent,
                $"CodeSystem {TerminologyNames.Canonical(fragment.Key, null)} is loaded only in part (a fragment or an example), without the draft's codes at {string.Join(", ", fragment.Select(item => item.Check.Element))}, so they may still exist and were not checked; the draft is not reported valid. Load its complete content (--terminology), or check them with a terminology server (--tx-server).",
                Location: null);
        }

        HashSet<string> explained = new(StringComparer.Ordinal) { TermValidate.DisplayMismatch, DisplayNotCheckedCode, ConceptQuery.CodeNotInLoadedContent, TxAnswers.NotFoundOutcome, TxAnswers.NotCheckedOutcome };
        bool serverNoted = issues.Any(issue => issue.Code == StoreTerminologyService.TxUnavailableReason);
        foreach (var other in unknown.Where(item => !TxFallback.SystemMissing(item.Details.Reason) && !explained.Contains(item.Details.Reason) && !(serverNoted && item.Details.Reason.StartsWith(StoreTerminologyService.TxUnavailableReason, StringComparison.Ordinal))))
        {
            yield return new IssueItem("warning", "terminology-unknown", $"The code at {other.Check.Element} could not be checked ({TerminologyNames.Token(other.Details.Reason)}), so the draft is not reported valid.", Location: null);
        }
    }

    private static IEnumerable<IssueItem> NotLoaded(List<(DraftCheck Check, TerminologyCheckDetails Details)> missing) => missing
        .GroupBy(item => item.Details.System, StringComparer.Ordinal)
        .Select(group => new IssueItem(
            "warning",
            SystemNotLoadedCode,
            $"CodeSystem {TerminologyNames.Canonical(group.Key, null)} is not loaded, so the draft's codes from it ({string.Join(", ", group.Select(item => item.Check.Element))}) were not checked and the draft is not reported valid. Load a package that carries it (--terminology), or check it with a terminology server (--tx-server).",
            Location: null));

    private static bool Same(string left, string right) => string.Equals(TextFolding.FoldDisplay(left), TextFolding.FoldDisplay(right), StringComparison.Ordinal);
}
