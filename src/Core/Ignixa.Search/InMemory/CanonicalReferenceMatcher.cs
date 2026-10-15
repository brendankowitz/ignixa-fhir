// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Compiles a reference parameter's query into a test of the canonical <see cref="UriSearchValue"/> entries
/// <see cref="CanonicalReferenceSearchIndexer"/> adds, with canonical semantics.
/// </summary>
/// <remarks>
/// A canonical is matched by its url, and a <c>|version</c> or <c>#fragment</c> the query gives must be equal too: a
/// search without a version matches every version (R5 §3.2.1.6.2, "[url] or [url]|[version]"). The query's original
/// text is compared, not the <see cref="ReferenceSearchValue"/> parsed from it, because the reference parser keeps only a
/// <c>Type/id</c>-shaped tail of a url and drops its version. Only plain equality (and its comma alternatives) applies; a
/// query with a modifier, or a value not parsed from text, matches no canonical.
/// </remarks>
internal static class CanonicalReferenceMatcher
{
    /// <summary>The canonical test for <paramref name="expression"/>, the unlowered body of a reference parameter, or null when none applies.</summary>
    public static Func<UriSearchValue, bool>? Compile(Expression expression) =>
        expression switch
        {
            SearchParameterPredicateExpression { Modifier: null, Value: ReferenceSearchValue { OriginalString: { Length: > 0 } text } } =>
                Matches(new UriSearchValue(text, separateCanonicalComponents: true)),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or UnionExpression => Any(Children(expression)),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } multiary => All(multiary.Expressions),
            _ => null,
        };

    private static Func<UriSearchValue, bool> Matches(UriSearchValue query) =>
        indexed => string.Equals(indexed.Uri, query.Uri, StringComparison.Ordinal)
            && (query.Version is null || string.Equals(indexed.Version, query.Version, StringComparison.Ordinal))
            && (query.Fragment is null || string.Equals(indexed.Fragment, query.Fragment, StringComparison.Ordinal));

    private static Func<UriSearchValue, bool>? Any(IEnumerable<Expression> alternatives)
    {
        List<Func<UriSearchValue, bool>> compiled = [.. alternatives.Select(Compile).OfType<Func<UriSearchValue, bool>>()];
        return compiled.Count == 0 ? null : indexed => compiled.Any(test => test(indexed));
    }

    private static Func<UriSearchValue, bool>? All(IEnumerable<Expression> conditions)
    {
        List<Func<UriSearchValue, bool>?> compiled = [.. conditions.Select(Compile)];
        return compiled.Count == 0 || compiled.Contains(null) ? null : indexed => compiled.All(test => test!(indexed));
    }

    private static IEnumerable<Expression> Children(Expression expression) =>
        expression switch
        {
            MultiaryExpression multiary => multiary.Expressions,
            UnionExpression union => union.Expressions,
            _ => [expression],
        };
}
