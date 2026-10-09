// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Extension methods for sanitizing values before they are written to structured logs,
/// preventing log-injection attacks via embedded newline characters.
/// </summary>
public static class LogSanitizationExtensions
{
    /// <summary>
    /// Returns a copy of <paramref name="value"/> safe to pass as a structured log argument, by
    /// replacing any CR/LF characters with a space so a caller cannot forge extra log lines
    /// (CodeQL <c>cs/log-forging</c>). The result is always the same length as the input.
    /// </summary>
    /// <remarks>
    /// This is for log arguments only. Never apply it to a value used functionally (for example a
    /// route value that drives authorization or a data operation); sanitize at the log call site.
    /// CodeQL recognizes this method as a log-injection barrier through
    /// <c>.github/codeql/extensions/log-sanitization.model.yml</c>, which names it by namespace and
    /// type: renaming or moving this method requires updating that model, or every caller's
    /// <c>cs/log-forging</c> alert reopens.
    /// </remarks>
    /// <param name="value">The value to sanitize; <see langword="null"/> is returned unchanged.</param>
    /// <returns>
    /// <paramref name="value"/> with every CR and LF character replaced by a space, or
    /// <paramref name="value"/> itself when it is <see langword="null"/> or contains neither.
    /// </returns>
    public static string? SanitizeForLog(this string? value)
    {
        if (value is null || !value.AsSpan().ContainsAny('\r', '\n'))
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, src) =>
        {
            src.AsSpan().CopyTo(span);
            span.Replace('\r', ' ');
            span.Replace('\n', ' ');
        });
    }
}
