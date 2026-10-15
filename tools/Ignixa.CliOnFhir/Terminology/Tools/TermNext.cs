// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// Whether a caller-supplied value may be echoed in <c>next</c>: only when <see cref="TerminologyNames"/> keeps it unchanged.
/// A value the caller copied from untrusted data (a "system" holding a sentence) is never repeated back as a suggestion.
/// </summary>
internal static class TermNext
{
    /// <summary>Whether every given canonical (url or url|version, or a version) is shaped as what it names; nulls pass.</summary>
    public static bool Echoable(params string?[] canonicals) => canonicals.All(value => value is null || Same(value));

    private static bool Same(string value)
    {
        (string url, string? version) = TerminologyStore.SplitCanonical(value);
        return TerminologyNames.Canonical(url, version) == value || TerminologyNames.Version(value) == value;
    }
}
