// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// One code a criterion lists: <c>system|code</c>, or a bare code that matches in any system (as a FHIR token search does).
/// Only codes a search query can carry unescaped are representable, so every instance can be written into a query.
/// </summary>
internal sealed record CohortCode
{
    private const string UnsafeCharacters = "&,$\\|#";

    private static readonly SearchValues<char> Unsafe = SearchValues.Create(UnsafeCharacters);

    private CohortCode(string? system, string code)
    {
        System = system;
        Code = code;
    }

    /// <summary>The code system url; null matches the code in any system.</summary>
    public string? System { get; }

    public string Code { get; }

    /// <summary>The code as a search token value: <c>system|code</c>, or the bare code.</summary>
    public string Token => System is null ? Code : $"{System}|{Code}";

    /// <summary>Parses <c>system|code</c> or a bare code.</summary>
    /// <param name="text">The code as the caller wrote it.</param>
    /// <param name="where">Where the request has it, for example <c>conditions[0].codes[1]</c>.</param>
    /// <exception cref="ToolRequestException">Blank, an empty system or code, or a character a search query cannot carry.</exception>
    public static CohortCode Parse(string text, string where)
    {
        string trimmed = text.Trim();
        int bar = trimmed.IndexOf('|', StringComparison.Ordinal);
        string? system = bar < 0 ? null : trimmed[..bar];
        string code = bar < 0 ? trimmed : trimmed[(bar + 1)..];
        if (code.Length == 0 || system is { Length: 0 })
        {
            throw ToolRequestException.Error("invalid-parameter", $"{where} '{text}' is not a code: write 'system|code' (http://snomed.info/sct|44054006) or a bare code, which matches in any system.");
        }

        return From(system, code) ?? throw ToolRequestException.Error("invalid-parameter", $"{where} '{text}' holds a character a search cannot carry ({string.Join(' ', UnsafeCharacters.ToCharArray())}).");
    }

    /// <summary>The code, or null when it holds a character a search query cannot carry (a code read from terminology).</summary>
    public static CohortCode? From(string? system, string code) =>
        code.AsSpan().ContainsAny(Unsafe) || system.AsSpan().ContainsAny(Unsafe) || string.IsNullOrWhiteSpace(code)
            ? null
            : new CohortCode(string.IsNullOrEmpty(system) ? null : system, code);

    /// <summary>Whether a coding with <paramref name="system"/> and <paramref name="code"/> is this code.</summary>
    public bool Matches(string? system, string? code) =>
        string.Equals(code, Code, StringComparison.Ordinal) && (System is null || string.Equals(system, System, StringComparison.Ordinal));
}
