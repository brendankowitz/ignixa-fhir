// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.Extensions;

/// <summary>
/// Extension methods for sanitizing values before they are written to structured logs,
/// preventing log-injection attacks via embedded newline characters.
/// </summary>
public static class LogSanitizationExtensions
{
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
