// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Extension methods for sanitizing values before they are written to structured logs,
/// preventing log-injection attacks via embedded newline characters.
/// </summary>
public static class LogSanitizationExtensions
{
    /// <summary>The most expression text a log record carries.</summary>
    public const int MaxExpressionLogLength = LogSafeText.DefaultMaxLength;

    /// <summary>
    /// Returns a copy of <paramref name="value"/> safe to pass as a structured log argument, by
    /// replacing every character <see cref="LogSafeText.IsUnsafe"/> reports - CR, LF and other controls, the
    /// Unicode line and paragraph separators, bidirectional formatting characters - with a space, so a caller
    /// cannot forge or disguise log lines (CodeQL <c>cs/log-forging</c>). The result is always the same length as
    /// the input.
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
    /// <paramref name="value"/> with every unsafe character replaced by a space, or <paramref name="value"/>
    /// itself when it is <see langword="null"/> or contains none.
    /// </returns>
    public static string? SanitizeForLog(this string? value)
    {
        if (value is null || !value.Any(LogSafeText.IsUnsafe))
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                span[i] = LogSafeText.IsUnsafe(src[i]) ? ' ' : src[i];
            }
        });
    }

    /// <summary>
    /// Replaces every control character in <paramref name="value"/> and truncates it to at most
    /// <paramref name="maxLength"/> characters, without splitting a surrogate pair, so a caller-chosen string
    /// cannot forge log lines or dominate a record. See <see cref="LogSafeText.Sanitize"/>.
    /// </summary>
    /// <param name="value">The value to sanitize; <see langword="null"/> is returned unchanged.</param>
    /// <param name="maxLength">The longest text to keep, which must be positive.</param>
    public static string? SanitizeForLog(this string? value, int maxLength) =>
        LogSafeText.Sanitize(value, maxLength);

    /// <summary>
    /// Sanitizes and truncates FHIRPath expression or path text for logging, to <see cref="MaxExpressionLogLength"/> characters.
    /// </summary>
    public static string? SanitizeExpressionForLog(this string? expression) =>
        LogSafeText.Sanitize(expression, MaxExpressionLogLength);
}
