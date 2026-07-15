// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Globalization;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchSyntaxExceptionFactory
{
    internal static InvalidSearchOperationException Create(
        string source,
        int offset,
        string subject,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(detail);

        var clampedOffset = Math.Clamp(offset, 0, source.Length);
        var (line, column) = GetLineAndColumn(source, clampedOffset);

        return new InvalidSearchOperationException(string.Format(
            CultureInfo.InvariantCulture,
            Resources.MalformedSearchSyntax,
            subject,
            line,
            column,
            detail));
    }

    private static (int Line, int Column) GetLineAndColumn(string source, int offset)
    {
        var line = 1;
        var column = 1;

        for (var index = 0; index < offset; index++)
        {
            var character = source[index];

            if (character == '\r')
            {
                line++;
                column = 1;

                if (index + 1 < offset && source[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            if (character == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return (line, column);
    }
}
