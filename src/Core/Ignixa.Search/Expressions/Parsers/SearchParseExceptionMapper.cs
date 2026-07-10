// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Ignixa.Search.Indexing;
using Superpower.Model;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchParseExceptionMapper
{
    internal static T RequireTokenization<T>(Result<T> result, string subject)
    {
        if (result.HasValue)
        {
            return result.Value;
        }

        throw Create(subject, result.ErrorPosition, result.FormatErrorMessageFragment());
    }

    internal static TResult RequireParsing<TKind, TResult>(TokenListParserResult<TKind, TResult> result, string subject)
        where TKind : struct
    {
        if (result.HasValue)
        {
            return result.Value;
        }

        throw Create(subject, result.ErrorPosition, result.FormatErrorMessageFragment());
    }

    private static InvalidSearchOperationException Create(string subject, Position position, string fragment)
    {
        return new InvalidSearchOperationException(string.Format(
            CultureInfo.InvariantCulture,
            Resources.MalformedSearchSyntax,
            subject,
            position.Line,
            position.Column,
            fragment));
    }
}
