// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Superpower;
using Superpower.Parsers;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchKeyGrammar
{
    private static readonly TokenListParser<SearchKeyTokenKind, string> Identifier =
        Token.EqualTo(SearchKeyTokenKind.Identifier).Select(token => token.ToStringValue());

    private static readonly TokenListParser<SearchKeyTokenKind, string> IncludeSource =
        Token.EqualTo(SearchKeyTokenKind.Asterisk).Value("*").Or(Identifier);

    private static readonly TokenListParser<SearchKeyTokenKind, string?> WildcardOrIdentifier =
        Token.EqualTo(SearchKeyTokenKind.Asterisk).Value((string?)null)
            .Or(Identifier.Select(identifier => (string?)identifier));

    private static readonly TokenListParser<SearchKeyTokenKind, string?> ReferencePath =
        Token.EqualTo(SearchKeyTokenKind.Asterisk).Value((string?)null)
            .Or(Identifier
                .Where(identifier => char.IsLetter(identifier[0]))
                .Select(identifier => (string?)identifier));

    private static readonly TokenListParser<SearchKeyTokenKind, string> Has =
        Identifier.Where(identifier => string.Equals(identifier, "_has", StringComparison.Ordinal));

    private static readonly TokenListParser<SearchKeyTokenKind, string?> OptionalQualifier =
        (from _ in Token.EqualTo(SearchKeyTokenKind.Colon)
         from qualifier in Identifier
         select qualifier)
        .OptionalOrDefault((string?)null);

    private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax?> OptionalForward =
        (from _ in Token.EqualTo(SearchKeyTokenKind.Dot)
         from nested in Superpower.Parse.Ref(() => Key!)
         select (SearchKeySyntax?)nested)
        .OptionalOrDefault();

    private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax> ParameterOrForward =
        from name in Identifier
        from qualifier in OptionalQualifier
        from next in OptionalForward
        select next is null
            ? (SearchKeySyntax)new ParameterKeySyntax(name, qualifier)
            : new ForwardChainKeySyntax(name, qualifier, next);

    private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Reverse =
        from _ in Has
        from __ in Token.EqualTo(SearchKeyTokenKind.Colon)
        from sourceResourceType in Identifier
        from ___ in Token.EqualTo(SearchKeyTokenKind.Colon)
        from referenceName in Identifier
        from ____ in Token.EqualTo(SearchKeyTokenKind.Colon)
        from next in Superpower.Parse.Ref(() => Key!)
        select (SearchKeySyntax)new ReverseChainKeySyntax(sourceResourceType, referenceName, next);

    private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Key =
        Reverse.Try().Or(ParameterOrForward);

    private static readonly TokenListParser<SearchKeyTokenKind, IncludeKeySyntax> Include =
        (from source in IncludeSource
         from _ in Token.EqualTo(SearchKeyTokenKind.Colon)
         from __ in Token.EqualTo(SearchKeyTokenKind.Asterisk)
         select new IncludeKeySyntax(source, null, null, true))
        .Try()
        .Or(
            from source in IncludeSource
            from _ in Token.EqualTo(SearchKeyTokenKind.Colon)
            from parameter in Identifier
            from targetResourceType in OptionalQualifier
            select new IncludeKeySyntax(source, parameter, targetResourceType, false));

    private static readonly TokenListParser<SearchKeyTokenKind, NotReferencedKeySyntax> NotReferenced =
        from sourceResourceType in WildcardOrIdentifier
        from _ in Token.EqualTo(SearchKeyTokenKind.Colon)
        from referencePath in ReferencePath
        select new NotReferencedKeySyntax(sourceResourceType, referencePath);

    internal static SearchKeySyntax ParseParameter(string key)
    {
        return ParseCore(key, "search key", Key);
    }

    internal static IncludeKeySyntax ParseInclude(string includeValue)
    {
        return ParseCore(includeValue, "include key", Include);
    }

    internal static NotReferencedKeySyntax ParseNotReferenced(string notReferencedValue)
    {
        return ParseCore(notReferencedValue, "_not-referenced value", NotReferenced);
    }

    private static T ParseCore<T>(
        string source,
        string subject,
        TokenListParser<SearchKeyTokenKind, T> parser)
    {
        var tokenizationResult = SearchKeyTokenizer.Instance.TryTokenize(source);
        var tokens = SearchParseExceptionMapper.RequireTokenization(tokenizationResult, subject);

        var parseResult = parser.AtEnd().TryParse(tokens);
        return SearchParseExceptionMapper.RequireParsing(parseResult, subject);
    }
}
