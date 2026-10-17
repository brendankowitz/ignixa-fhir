// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>A draft and what its checks said.</summary>
/// <param name="Resource">The draft resource: built from the caller's input only (the tool invents no clinical content), so its text is the caller's.</param>
/// <param name="Verdict"><c>valid</c> (every check passed, some perhaps with a warning), <c>invalid</c> (a check failed) or <c>unknown</c> (no check failed, but at least one could not be evaluated).</param>
/// <param name="Checks">Every check, in order: validation, then terminology for each coded value, then each reference.</param>
/// <param name="AsOf">The instant that dates it where the input did not (<c>authoredOn</c>, <c>effective</c>, <c>issued</c>), and where it came from.</param>
/// <param name="File">The file it was written to, with <c>out</c>.</param>
public sealed record DraftFacts(
    JsonObject Resource,
    string Verdict,
    IReadOnlyList<DraftCheck> Checks,
    AsOf AsOf,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DraftFileFacts? File = null)
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string Unknown = "unknown";

    /// <summary>The verdict the checks give.</summary>
    public static string VerdictOf(IEnumerable<DraftCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        List<string> outcomes = [.. checks.Select(check => check.Outcome)];
        return outcomes.Contains(DraftCheck.Fail) ? Invalid : outcomes.Contains(DraftCheck.Unknown) ? Unknown : Valid;
    }
}
