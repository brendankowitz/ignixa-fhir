// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers.Syntax;
using Superpower;
using Superpower.Model;

namespace Ignixa.Search.Expressions.Parsers;

internal sealed class SearchValueTokenizer : Tokenizer<SearchValueTokenKind>
{
    private SearchValueTokenizer()
    {
    }

    internal static SearchValueTokenizer Instance { get; } = new();

    protected override IEnumerable<Result<SearchValueTokenKind>> Tokenize(TextSpan span)
    {
        while (!span.IsAtEnd)
        {
            TextSpan start = span;
            SearchValueTokenKind? separator = span[0] switch
            {
                ',' => SearchValueTokenKind.Comma,
                '$' => SearchValueTokenKind.Dollar,
                '|' => SearchValueTokenKind.Pipe,
                _ => null,
            };

            if (separator is { } kind)
            {
                TextSpan remainder = span.Skip(1);
                yield return Result.Value(kind, start, remainder);
                span = remainder;
                continue;
            }

            var length = 0;
            while (length < span.Length)
            {
                char current = span[length];
                if (current is ',' or '$' or '|')
                {
                    break;
                }

                if (current == '\\')
                {
                    if (length + 1 >= span.Length ||
                        span[length + 1] is not ('\\' or ',' or '$' or '|'))
                    {
                        yield return Result.Empty<SearchValueTokenKind>(
                            span.Skip(length),
                            ["valid FHIR escape"]);
                        yield break;
                    }

                    length += 2;
                }
                else
                {
                    length++;
                }
            }

            TextSpan textRemainder = span.Skip(length);
            yield return Result.Value(SearchValueTokenKind.Text, start, textRemainder);
            span = textRemainder;
        }
    }
}
