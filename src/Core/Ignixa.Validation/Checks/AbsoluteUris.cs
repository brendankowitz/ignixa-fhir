// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Whether a value is an absolute URI: one that starts with a scheme (RFC 3986 section 3.1,
/// <c>ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":"</c>) and that <see cref="Uri"/> parses as absolute.
/// </summary>
/// <remarks>
/// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> alone is platform-dependent: on Unix it reads <c>/foo</c> as a
/// <c>file:</c> path and calls it absolute, so a relative reference passed there and failed on Windows. Requiring the scheme
/// decides it from the text, the same way everywhere.
/// </remarks>
internal static partial class AbsoluteUris
{
    public static bool IsAbsolute(string? value) =>
        !string.IsNullOrEmpty(value) && Scheme().IsMatch(value) && Uri.TryCreate(value, UriKind.Absolute, out _);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
