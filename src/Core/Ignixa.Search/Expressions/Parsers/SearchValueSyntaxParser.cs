// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchValueSyntaxParser
{
    private static readonly ImmutableArray<(string Literal, SearchComparator Comparator)> SearchComparators =
        Enum.GetValues<SearchComparator>()
            .Select(comparator => (comparator.GetLiteral(), comparator))
            .OrderByDescending(pair => pair.Item1.Length)
            .ToImmutableArray();

    internal static SearchValueSyntax Parse(
        SearchParamType searchType,
        SearchModifier? modifier,
        string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (modifier?.SearchModifierCode == SearchModifierCode.Missing)
        {
            return ParseMissing(source);
        }

        ValidateEscapes(source);

        if (modifier?.SearchModifierCode == SearchModifierCode.Text)
        {
            return ParseText(source);
        }

        if (modifier?.SearchModifierCode == SearchModifierCode.OfType)
        {
            return ParseOfType(source);
        }

        if (searchType == SearchParamType.Composite)
        {
            return ParseComposite(source);
        }

        return ParseScalar(
            source,
            searchType is SearchParamType.Date or SearchParamType.Number or SearchParamType.Quantity);
    }

    private static SearchValueSyntax ParseMissing(string source)
    {
        if (!bool.TryParse(source, out bool isMissing))
        {
            throw new InvalidSearchOperationException(Resources.InvalidValueTypeForMissingModifier);
        }

        return new MissingValueSyntax(isMissing);
    }

    private static SearchValueSyntax ParseText(string source)
    {
        if (source.Length == 0)
        {
            throw SyntaxError(source, 0, "nonempty text value");
        }

        return new AtomicValueSyntax(source, SearchComparator.Eq);
    }

    private static SearchValueSyntax ParseScalar(string source, bool supportsComparator)
    {
        if (source.Length == 0)
        {
            throw SyntaxError(source, 0, "nonempty scalar value");
        }

        int comma = FindUnescaped(source, ',', 0);
        if (comma < 0)
        {
            return ParseAtomic(source, 0, source.Length, supportsComparator);
        }

        var items = ImmutableArray.CreateBuilder<SearchValueSyntax>();
        int partStart = 0;

        while (comma >= 0)
        {
            items.Add(ParseAtomic(source, partStart, comma - partStart, supportsComparator));
            partStart = comma + 1;
            comma = FindUnescaped(source, ',', partStart);
        }

        items.Add(ParseAtomic(source, partStart, source.Length - partStart, supportsComparator));
        return new AlternativesValueSyntax(items.ToImmutable());
    }

    private static AtomicValueSyntax ParseAtomic(
        string source,
        int start,
        int length,
        bool supportsComparator)
    {
        if (length == 0)
        {
            throw SyntaxError(source, start, "nonempty value");
        }

        if (supportsComparator)
        {
            ReadOnlySpan<char> value = source.AsSpan(start, length);

            foreach ((string literal, SearchComparator comparator) in SearchComparators)
            {
                if (value.StartsWith(literal.AsSpan(), StringComparison.Ordinal))
                {
                    return new AtomicValueSyntax(
                        Slice(source, start + literal.Length, length - literal.Length),
                        comparator);
                }
            }
        }

        return new AtomicValueSyntax(Slice(source, start, length), SearchComparator.Eq);
    }

    private static SearchValueSyntax ParseOfType(string source)
    {
        if (source.Length == 0)
        {
            throw SyntaxError(source, 0, "of-type value with exactly two unescaped pipes");
        }

        int comma = FindUnescaped(source, ',', 0);
        if (comma < 0)
        {
            return ParseOfTypeItem(source, 0, source.Length);
        }

        var items = ImmutableArray.CreateBuilder<SearchValueSyntax>();
        int itemStart = 0;

        while (comma >= 0)
        {
            items.Add(ParseOfTypeItem(source, itemStart, comma - itemStart));
            itemStart = comma + 1;
            comma = FindUnescaped(source, ',', itemStart);
        }

        items.Add(ParseOfTypeItem(source, itemStart, source.Length - itemStart));
        return new AlternativesValueSyntax(items.ToImmutable());
    }

    private static OfTypeValueSyntax ParseOfTypeItem(string source, int start, int length)
    {
        int end = start + length;
        int firstPipe = FindUnescaped(source, '|', start, end);

        if (firstPipe < 0)
        {
            throw SyntaxError(source, end, "of-type value with exactly two unescaped pipes");
        }

        int secondPipe = FindUnescaped(source, '|', firstPipe + 1, end);
        if (secondPipe < 0)
        {
            throw SyntaxError(source, end, "of-type value with exactly two unescaped pipes");
        }

        int thirdPipe = FindUnescaped(source, '|', secondPipe + 1, end);
        if (thirdPipe >= 0)
        {
            throw SyntaxError(source, thirdPipe, "of-type value with exactly two unescaped pipes");
        }

        return new OfTypeValueSyntax(
            Slice(source, start, firstPipe - start),
            Slice(source, firstPipe + 1, secondPipe - firstPipe - 1),
            Slice(source, secondPipe + 1, end - secondPipe - 1));
    }

    private static SearchValueSyntax ParseComposite(string source)
    {
        if (source.Length == 0)
        {
            throw SyntaxError(source, 0, "nonempty composite value");
        }

        int comma = FindUnescaped(source, ',', 0);
        if (comma < 0)
        {
            return ParseCompositeItem(source, 0, source.Length);
        }

        var items = ImmutableArray.CreateBuilder<SearchValueSyntax>();
        int itemStart = 0;

        while (comma >= 0)
        {
            items.Add(ParseCompositeItem(source, itemStart, comma - itemStart));
            itemStart = comma + 1;
            comma = FindUnescaped(source, ',', itemStart);
        }

        items.Add(ParseCompositeItem(source, itemStart, source.Length - itemStart));
        return new AlternativesValueSyntax(items.ToImmutable());
    }

    private static CompositeValueSyntax ParseCompositeItem(string source, int start, int length)
    {
        int end = start + length;
        int dollar = FindUnescaped(source, '$', start, end);

        if (dollar < 0 || dollar >= end)
        {
            return new CompositeValueSyntax(
                ImmutableArray.Create(ParseAtomic(source, start, length, supportsComparator: true)));
        }

        var components = ImmutableArray.CreateBuilder<AtomicValueSyntax>();
        int componentStart = start;

        while (dollar >= 0 && dollar < end)
        {
            components.Add(ParseAtomic(
                source,
                componentStart,
                dollar - componentStart,
                supportsComparator: true));
            componentStart = dollar + 1;
            dollar = FindUnescaped(source, '$', componentStart, end);
        }

        components.Add(ParseAtomic(
            source,
            componentStart,
            end - componentStart,
            supportsComparator: true));
        return new CompositeValueSyntax(components.ToImmutable());
    }

    private static void ValidateEscapes(string source)
    {
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] != '\\')
            {
                continue;
            }

            if (index + 1 >= source.Length ||
                source[index + 1] is not ('\\' or ',' or '$' or '|'))
            {
                throw SyntaxError(
                    source,
                    index,
                    "valid FHIR escape for backslash, comma, dollar, or pipe");
            }

            index++;
        }
    }

    private static int FindUnescaped(string source, char delimiter, int start)
    {
        return FindUnescaped(source, delimiter, start, source.Length);
    }

    private static int FindUnescaped(string source, char delimiter, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (source[index] == '\\')
            {
                index++;
                continue;
            }

            if (source[index] == delimiter)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Slice(string source, int start, int length)
    {
        return start == 0 && length == source.Length
            ? source
            : source.Substring(start, length);
    }

    private static InvalidSearchOperationException SyntaxError(
        string source,
        int offset,
        string expectation)
    {
        return SearchSyntaxExceptionFactory.Create(
            source,
            offset,
            "search value",
            $"expected {expectation}");
    }
}
