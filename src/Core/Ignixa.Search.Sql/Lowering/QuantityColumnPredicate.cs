using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Shared predicate builder for quantity value and identity constraints, used by both the leaf
/// <see cref="Leaf.QuantityLoweringRule"/> and the composite <see cref="Composite.TokenQuantityLoweringRule"/>.
/// Always includes the numeric range predicate; conjoins a system constraint and a <c>QuantityCodeId</c>
/// equality according to the search value's three-state system/code convention.
/// <para>
/// The system follows the same pattern as a token's, per the spec's statement that quantity's system and
/// code follow the token pattern: a null system (<c>5.4</c>, <c>5.4|</c> absent entirely) constrains
/// nothing, an empty system (<c>5.4||mg</c>) emits <c>SystemId IS NULL</c> so a quantity that does carry a
/// system cannot match, and a non-empty system emits <c>SystemId = @id</c>. A non-empty system or code the
/// symbol table resolves to <see langword="null"/> (known miss) causes an immediate
/// <see cref="Predicate.False"/> return.
/// </para>
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
        if (value.System is { Length: > 0 } system)
        {
            systemId = context.SystemId(system);
            if (systemId is null)
                return new Predicate.False($"No resource uses the quantity system '{system}'.");
        }

        int? quantityCodeId = null;
        if (value.Code is { Length: > 0 } code)
        {
            quantityCodeId = context.QuantityCodeId(code);
            if (quantityCodeId is null)
                return new Predicate.False($"No resource uses the quantity code '{code}'.");
        }

        Predicate result = NumericRangeComparison.Build(context, lowColumnRef, highColumnRef, comparator, comparisonValue);

        Predicate? systemPredicate = value.System switch
        {
            null => null,
            "" => new Predicate.IsNull(new SqlColumnRef(table.TableName, systemColumn)),
            _ => new Predicate.Equal(new SqlColumnRef(table.TableName, systemColumn), context.Parameter(systemId!.Value)),
        };

        if (systemPredicate is not null)
        {
            result = new Predicate.And(result, systemPredicate);
        }

        if (quantityCodeId is { } resolvedCode)
        {
            result = new Predicate.And(result,
                new Predicate.Equal(new SqlColumnRef(table.TableName, codeColumn), context.Parameter(resolvedCode)));
        }

        return result;
    }
}
