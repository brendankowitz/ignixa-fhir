// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// The <c>next</c> calls of the terminology tools, and whether a caller-supplied value may be echoed in one: only when
/// <see cref="TerminologyNames"/> keeps it unchanged. A value the caller copied from untrusted data (a "system" holding a
/// sentence) is never repeated back as a suggestion.
/// </summary>
internal static class TermNext
{
    /// <summary>A call of the MCP <c>terminology</c> tool: <paramref name="operation"/> first, then <paramref name="args"/>.</summary>
    public static NextCall Call(string operation, IReadOnlyDictionary<string, object> args)
    {
        Dictionary<string, object> call = new(StringComparer.Ordinal) { ["operation"] = operation };
        foreach ((string name, object value) in args)
        {
            call[name] = value;
        }

        return new NextCall("terminology", call);
    }

    /// <summary>Whether every given canonical (url or url|version, or a version) is shaped as what it names; nulls pass.</summary>
    public static bool Echoable(params string?[] canonicals) => canonicals.All(value => value is null || Same(value));

    private static bool Same(string value)
    {
        (string url, string? version) = TerminologyStore.SplitCanonical(value);
        return TerminologyNames.Canonical(url, version) == value || TerminologyNames.Version(value) == value;
    }
}
