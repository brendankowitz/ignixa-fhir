// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Compiles a reference parameter's query into a test of the canonical <see cref="UriSearchValue"/> entries
/// <see cref="CanonicalReferenceSearchIndexer"/> adds, with canonical semantics.
/// </summary>
/// <remarks>
/// A canonical is matched by its url, and a <c>|version</c> or <c>#fragment</c> the query gives must be equal too: a
/// search without a version matches every version (R5 §3.2.1.6.2, "[url] or [url]|[version]"). The query's original
/// text is compared, not the <see cref="ReferenceSearchValue"/> parsed from it, because the reference parser keeps only a
/// <c>Type/id</c>-shaped tail of a url and drops its version. Plain equality, its comma alternatives and a <c>:[type]</c>
/// modifier naming one of the parameter's targets apply (the type cannot be checked without resolving the canonical, so
/// the url decides). Anything else is refused rather than matching no canonical: any other modifier, a value not parsed
/// from text, or another expression shape.
/// </remarks>
internal static class CanonicalReferenceMatcher
{
    /// <summary>The canonical test for <paramref name="expression"/>, the unlowered body of a reference parameter that selects canonicals.</summary>
    /// <exception cref="SearchOperationNotSupportedException">The expression has no canonical meaning this matcher implements.</exception>
    public static Func<UriSearchValue, bool> Compile(Expression expression) =>
        expression switch
        {
            SearchParameterPredicateExpression predicate => Matches(new UriSearchValue(Text(predicate), separateCanonicalComponents: true)),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or UnionExpression => Any(Children(expression)),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } multiary => All(multiary.Expressions),
            _ => throw Refused($"the expression {expression} has no canonical meaning"),
        };

    private static string Text(SearchParameterPredicateExpression predicate)
    {
        SearchParameterInfo parameter = predicate.Parameter;
        if (predicate.Modifier is { } modifier
            && !(modifier.SearchModifierCode == SearchModifierCode.Type && parameter.TargetResourceTypes.Contains(modifier.ResourceType, StringComparer.OrdinalIgnoreCase)))
        {
            throw Refused($"the modifier ':{modifier}' on '{parameter.Code}' is not supported for canonical references");
        }

        return predicate.Value is ReferenceSearchValue { OriginalString: { Length: > 0 } text }
            ? text
            : throw Refused($"the value {predicate.Value} of '{parameter.Code}' was not parsed from text, so it cannot be compared with a canonical");
    }

    private static Func<UriSearchValue, bool> Matches(UriSearchValue query) =>
        indexed => string.Equals(indexed.Uri, query.Uri, StringComparison.Ordinal)
            && (query.Version is null || string.Equals(indexed.Version, query.Version, StringComparison.Ordinal))
            && (query.Fragment is null || string.Equals(indexed.Fragment, query.Fragment, StringComparison.Ordinal));

    private static Func<UriSearchValue, bool> Any(IEnumerable<Expression> alternatives)
    {
        List<Func<UriSearchValue, bool>> compiled = [.. alternatives.Select(Compile)];
        return indexed => compiled.Any(test => test(indexed));
    }

    private static Func<UriSearchValue, bool> All(IEnumerable<Expression> conditions)
    {
        List<Func<UriSearchValue, bool>> compiled = [.. conditions.Select(Compile)];
        return indexed => compiled.All(test => test(indexed));
    }

    private static SearchOperationNotSupportedException Refused(string why) =>
        new($"In-memory canonical search cannot answer this query: {why}.");

    private static IEnumerable<Expression> Children(Expression expression) =>
        expression switch
        {
            MultiaryExpression multiary => multiary.Expressions,
            UnionExpression union => union.Expressions,
            _ => [expression],
        };
}
