// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;
using Superpower;
using Superpower.Parsers;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchValueGrammar
{
    private static readonly TokenListParser<SearchValueTokenKind, string> Text =
        Token.EqualTo(SearchValueTokenKind.Text)
            .Select(token => token.ToStringValue());

    private static readonly TokenListParser<SearchValueTokenKind, string> PipeText =
        Token.EqualTo(SearchValueTokenKind.Pipe)
            .Select(token => token.ToStringValue());

    private static readonly TokenListParser<SearchValueTokenKind, string> DollarText =
        Token.EqualTo(SearchValueTokenKind.Dollar)
            .Select(token => token.ToStringValue());

    private static readonly (string Literal, SearchComparator Comparator)[] SearchComparators =
        Enum.GetValues<SearchComparator>()
            .Select(comparator => (comparator.GetLiteral(), comparator))
            .OrderByDescending(pair => pair.Item1.Length)
            .ToArray();

    private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> Missing =
        Text.Where(value => bool.TryParse(value, out _), "\"true\" or \"false\"")
            .Select(value => (SearchValueSyntax)new MissingValueSyntax(bool.Parse(value)));

    private static readonly TokenListParser<SearchValueTokenKind, string?> OfTypeSegment =
        Text.Or(DollarText)
            .AtLeastOnce()
            .Select(parts => (string?)string.Concat(parts))
            .OptionalOrDefault();

    private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> OfTypeItem =
        from system in OfTypeSegment
        from _ in Token.EqualTo(SearchValueTokenKind.Pipe)
        from code in OfTypeSegment
        from __ in Token.EqualTo(SearchValueTokenKind.Pipe)
        from identifier in OfTypeSegment
        select (SearchValueSyntax)new OfTypeValueSyntax(
            system ?? string.Empty,
            code ?? string.Empty,
            identifier ?? string.Empty);

    private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> OfType =
        WrapAlternatives(
            OfTypeItem.AtLeastOnceDelimitedBy(
                Token.EqualTo(SearchValueTokenKind.Comma)));

    private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> TextModifier =
        Token.Matching<SearchValueTokenKind>(
                _ => true,
                "text modifier value")
            .AtLeastOnce()
            .Select(tokens => (SearchValueSyntax)new AtomicValueSyntax(
                string.Concat(tokens.Select(token => token.ToStringValue())),
                SearchComparator.Eq));

    internal static SearchValueSyntax Parse(
        SearchParamType searchType,
        SearchModifier? modifier,
        string source)
    {
        if (modifier?.SearchModifierCode == SearchModifierCode.Missing &&
            !bool.TryParse(source, out _))
        {
            throw new InvalidSearchOperationException(
                Resources.InvalidValueTypeForMissingModifier);
        }

        TokenListParser<SearchValueTokenKind, SearchValueSyntax> parser =
            modifier?.SearchModifierCode switch
            {
                SearchModifierCode.Missing => Missing,
                SearchModifierCode.OfType => OfType,
                SearchModifierCode.Text => TextModifier,
                _ when searchType == SearchParamType.Composite => Composite(),
                _ => Scalar(searchType is
                    SearchParamType.Date or
                    SearchParamType.Number or
                    SearchParamType.Quantity),
            };

        var tokenizationResult = SearchValueTokenizer.Instance.TryTokenize(source);
        var tokens = SearchParseExceptionMapper.RequireTokenization(tokenizationResult, "search value");
        var parseResult = parser.AtEnd().TryParse(tokens);
        return SearchParseExceptionMapper.RequireParsing(parseResult, "search value");
    }

    private static TokenListParser<SearchValueTokenKind, string> Segment(bool includeDollar)
    {
        TokenListParser<SearchValueTokenKind, string> part = includeDollar
            ? Text.Or(PipeText).Or(DollarText)
            : Text.Or(PipeText);

        return part.AtLeastOnce().Select(parts => string.Concat(parts));
    }

    private static AtomicValueSyntax ParseAtomic(string rawText, bool supportsComparator)
    {
        if (supportsComparator)
        {
            foreach ((string literal, SearchComparator comparator) in SearchComparators)
            {
                if (rawText.StartsWith(literal, StringComparison.Ordinal))
                {
                    return new AtomicValueSyntax(rawText[literal.Length..], comparator);
                }
            }
        }

        return new AtomicValueSyntax(rawText, SearchComparator.Eq);
    }

    private static TokenListParser<SearchValueTokenKind, SearchValueSyntax> Scalar(bool supportsComparator)
    {
        TokenListParser<SearchValueTokenKind, SearchValueSyntax> atomic = Segment(includeDollar: true)
            .Select(raw => (SearchValueSyntax)ParseAtomic(raw, supportsComparator));

        return WrapAlternatives(
            atomic.AtLeastOnceDelimitedBy(Token.EqualTo(SearchValueTokenKind.Comma)));
    }

    private static TokenListParser<SearchValueTokenKind, SearchValueSyntax> Composite()
    {
        TokenListParser<SearchValueTokenKind, AtomicValueSyntax> component = Segment(includeDollar: false)
            .Select(raw => ParseAtomic(raw, supportsComparator: true));
        TokenListParser<SearchValueTokenKind, SearchValueSyntax> composite = component
            .AtLeastOnceDelimitedBy(Token.EqualTo(SearchValueTokenKind.Dollar))
            .Select(components => (SearchValueSyntax)new CompositeValueSyntax(
                components.ToImmutableArray()));

        return WrapAlternatives(
            composite.AtLeastOnceDelimitedBy(Token.EqualTo(SearchValueTokenKind.Comma)));
    }

    private static TokenListParser<SearchValueTokenKind, SearchValueSyntax> WrapAlternatives(
        TokenListParser<SearchValueTokenKind, SearchValueSyntax[]> parser)
    {
        return parser.Select(items => items.Length == 1
            ? items[0]
            : new AlternativesValueSyntax(items.ToImmutableArray()));
    }
}
