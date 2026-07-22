using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Shared predicate builder for quantity value and identity constraints, used by both the leaf
/// <see cref="Leaf.QuantityLoweringRule"/> and the composite <see cref="Composite.TokenQuantityLoweringRule"/>.
/// Always includes the numeric range predicate; conjoins <c>SystemId</c> equality when the search value
/// carries a non-empty system, and <c>QuantityCodeId</c> equality when it carries a non-empty code.
/// A non-empty system or code that the symbol table resolves to <see langword="null"/> (known miss)
/// causes an immediate <see cref="Predicate.False"/> return — empty system or code means no constraint,
/// not a null-guard predicate.
/// </summary>
internal static class QuantityColumnPredicate
{
    /// <summary>
    /// Builds the full quantity predicate: numeric range AND (optionally) system AND (optionally) code.
    /// </summary>
    /// <param name="table">The table the columns belong to.</param>
    /// <param name="lowColumn">Low-value column name (e.g. <c>LowValue</c> or <c>LowValue2</c>).</param>
    /// <param name="highColumn">High-value column name (e.g. <c>HighValue</c> or <c>HighValue2</c>).</param>
    /// <param name="systemColumn">SystemId column name (e.g. <c>SystemId</c> or <c>SystemId2</c>).</param>
    /// <param name="codeColumn">QuantityCodeId column name (e.g. <c>QuantityCodeId</c> or <c>QuantityCodeId2</c>).</param>
    /// <param name="comparator">The search comparator.</param>
    /// <param name="value">The quantity search value.</param>
    /// <param name="context">The leaf context for symbol resolution and value parameterization.</param>
    /// <returns>
    /// A predicate combining numeric range with identity constraints, or <see cref="Predicate.False"/>
    /// on a known-miss for either non-empty identity value.
    /// </returns>
    public static Predicate Build(
        TableDescriptor table,
        string lowColumn,
        string highColumn,
        string systemColumn,
        string codeColumn,
        SearchComparator comparator,
        QuantitySearchValue value,
        LeafContext context)
    {
        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("QuantitySearchValue has neither Low nor High set.");

        var lowColumnRef = new SqlColumnRef(table.TableName, lowColumn);
        var highColumnRef = new SqlColumnRef(table.TableName, highColumn);

        int? systemId = null;
        if (!string.IsNullOrEmpty(value.System))
        {
            var resolved = context.SystemId(value.System);
            if (resolved is null)
                return new Predicate.False();
            systemId = resolved;
        }

        int? quantityCodeId = null;
        if (!string.IsNullOrEmpty(value.Code))
        {
            var resolved = context.QuantityCodeId(value.Code);
            if (resolved is null)
                return new Predicate.False();
            quantityCodeId = resolved;
        }

        Predicate result = NumericRangeComparison.Build(context, lowColumnRef, highColumnRef, comparator, comparisonValue);

        if (systemId is { } resolvedSystem)
        {
            result = new Predicate.And(result,
                new Predicate.Equal(new SqlColumnRef(table.TableName, systemColumn), context.Parameter(resolvedSystem)));
        }

        if (quantityCodeId is { } resolvedCode)
        {
            result = new Predicate.And(result,
                new Predicate.Equal(new SqlColumnRef(table.TableName, codeColumn), context.Parameter(resolvedCode)));
        }

        return result;
    }
}
