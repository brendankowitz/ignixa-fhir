// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>When a terminology tool asks the remote server: only for content the local store does not have.</summary>
internal static class TxFallback
{
    /// <summary>A system (or the requested version of it) is not loaded locally; a missing code in a loaded system is not a reason.</summary>
    public static bool SystemMissing(string outcome) => outcome is CodeSystemResolution.NotLoaded or CodeSystemResolution.VersionNotLoaded;

    /// <summary>A validation the store could not decide because the system or ValueSet is missing or its expansion incomplete.</summary>
    public static bool ValidationMissing(string reason) =>
        SystemMissing(reason) || reason is TermExpand.ValueSetNotLoaded or ValueSetExpansion.IncompleteCode;

    /// <summary>Why the server was asked, from what the local answer checked (already safe-named).</summary>
    public static string Why(IReadOnlyList<AbsentItem> absent, string? reason = null) => absent.Count > 0
        ? $"{absent[0].What} is not available locally ({reason ?? "not loaded"})"
        : $"the local terminology could not decide it ({reason ?? "incomplete"})";
}
