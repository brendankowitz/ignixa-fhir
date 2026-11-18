// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace Ignixa.FhirPath;

/// <summary>
/// Makes caller-controlled text (expression text, paths) safe to write to a log: control characters
/// cannot forge log lines and the length is bounded.
/// </summary>
/// <remarks>
/// Lives in the lowest assembly every logging library (Search, Validation, DeId, Application) already
/// references, so there is a single implementation.
/// </remarks>
public static class LogSafeText
{
    /// <summary>The default bound for expression and path text.</summary>
    public const int DefaultMaxLength = 200;

    /// <summary>
    /// Replaces every control character with a space and truncates to <paramref name="maxLength"/> UTF-16 units
    /// without splitting a surrogate pair. A truncated value ends with a marker naming how many units were dropped.
    /// </summary>
    /// <param name="value">The text; <see langword="null"/> is returned unchanged.</param>
    /// <param name="maxLength">The longest text to keep; must be positive.</param>
    public static string? Sanitize(string? value, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        if (value is null)
        {
            return null;
        }

        var keep = value.Length;
        if (keep > maxLength)
        {
            keep = maxLength;
            if (char.IsHighSurrogate(value[keep - 1]))
            {
                keep--;
            }
        }

        var builder = new StringBuilder(keep + 32);
        for (var i = 0; i < keep; i++)
        {
            builder.Append(char.IsControl(value[i]) ? ' ' : value[i]);
        }

        if (keep < value.Length)
        {
            builder.Append("...[").Append(value.Length - keep).Append(" more characters]");
        }

        return builder.ToString();
    }
}
