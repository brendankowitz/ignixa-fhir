using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a Uri search value to a ParamSource over UriSearchParam.
/// <list type="bullet">
///   <item>No modifier: exact case-sensitive equality.</item>
///   <item>:below — self, or any descendant at a path-segment boundary.</item>
///   <item>:above — self, or any ancestor at a path-segment boundary.</item>
/// </list>
/// Version/Fragment are not part of the base UriSearchParam table; this rule covers the base Uri column only.
/// </summary>
/// <remarks>
/// Hierarchy is segment-aware, per the spec's "partial matching based on URL path segments". A bare
/// lexical prefix would make <c>url:below=http://acme.org/fhir/ValueSet</c> falsely match a stored
/// <c>http://acme.org/fhir/ValueSetOther</c>. Both directions therefore compare against the search value
/// with a single trailing separator appended, which admits the exact match and every proper
/// segment-boundary relative while rejecting same-prefix siblings.
///
/// No COLLATE override is emitted. dbo.UriSearchParam.Uri is already declared
/// COLLATE Latin1_General_100_CS_AS, which is case- and accent-sensitive and therefore equality-identical
/// to a binary collation over URI characters. Forcing BIN2 on the column side made equality incompatible
/// with the index key ordering and turned the hot terminology lookup (?url=...) from a seek into a scan.
/// </remarks>
internal static class UriLoweringRule
{
    private const char SegmentSeparator = '/';

    public static CteDefinition.ParamSource Lower(
        SearchParameterPredicateExpression predicate,
        UriSearchValue value,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var column = new SqlColumnRef(table.TableName, "Uri");

        Predicate predicateExpr = predicate.Modifier?.SearchModifierCode switch
        {
            null => new Predicate.Equal(column, context.Parameter(value.Uri)),

            SearchModifierCode.Below => new Predicate.Or(
                new Predicate.Equal(column, context.Parameter(value.Uri)),
                new Predicate.Like(column, context.Parameter(SegmentPrefix(value.Uri)), LikeMatch.StartsWith)),

            // LEFT(@base + '/', LEN(col)) = col still admits the exact match: the appended separator makes
            // the search value one character longer than an exact-matching stored value, so the LEFT()
            // yields exactly that value -- while a same-prefix sibling such as ".../ValueSetOther" fails
            // the character-for-character comparison.
            SearchModifierCode.Above => new Predicate.PrefixOfParameter(
                column, context.Parameter(SegmentPrefix(value.Uri))),

            var modifier => throw new NotSupportedException(
                $"Uri search does not support the ':{modifier}' modifier."),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
    }

    /// <summary>
    /// The search value with exactly one trailing segment separator, so that a caller-supplied trailing
    /// slash does not produce a doubled one.
    /// </summary>
    private static string SegmentPrefix(string uri) => uri.TrimEnd(SegmentSeparator) + SegmentSeparator;
}
