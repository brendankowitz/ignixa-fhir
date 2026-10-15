// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// Names terminology things in issue messages, <c>absent</c> and evidence. Urls, versions, codes and paths come from
/// packages, the folder or a terminology server, all untrusted: a value that is not shaped like what it names (for example
/// a "code" holding a sentence) is replaced by its digest (<see cref="FhirIds.Digest"/>), so such text never reaches a
/// message. Facts carry the values as written; they are data.
/// </summary>
public static partial class TerminologyNames
{
    /// <summary><c>url</c> or <c>url|version</c>, each part checked.</summary>
    public static string Canonical(string url, string? version) => version is null ? CheckedUrl(url) : $"{CheckedUrl(url)}|{Version(version)}";

    /// <summary><c>system|code</c>, each part checked.</summary>
    public static string Coded(string system, string code) => $"{CheckedUrl(system)}|{Code(code)}";

    public static string Version(string value) => Check(value, VersionShape(), 64);

    public static string Code(string value) => Check(value, CodeShape(), 64);

    /// <summary>A short machine token: a filter operator or property name, a content mode, an issue code.</summary>
    public static string Token(string value) => Check(value, TokenShape(), 64);

    /// <summary>A file path within a package or the folder.</summary>
    public static string Path(string value) => Check(value, PathShape(), 256);

    private static string CheckedUrl(string value) => Check(value, UrlShape(), 256);

    private static string Check(string value, Regex shape, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Length <= maxLength && shape.IsMatch(value) ? value : FhirIds.Digest(value);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.\\-]*:[^\\s\\p{C}\"'<>\\\\|^`{}]+$")]
    private static partial Regex UrlShape();

    [GeneratedRegex("^[A-Za-z0-9.\\-_+]+$")]
    private static partial Regex VersionShape();

    [GeneratedRegex("^[^\\s\\p{C}\"'<>\\\\|`]+$")]
    private static partial Regex CodeShape();

    [GeneratedRegex("^[A-Za-z0-9.\\-_:]+$")]
    private static partial Regex TokenShape();

    [GeneratedRegex("^[A-Za-z0-9._\\-/#@]+$")]
    private static partial Regex PathShape();
}
