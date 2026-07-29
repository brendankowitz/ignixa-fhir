using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Shared predicate builder for quantity value and identity constraints, used by the leaf
/// <see cref="Leaf.QuantityLoweringRule"/> and composite <see cref="Composite.TokenQuantityLoweringRule"/>.
/// Always includes the numeric range; system/code follow the token three-state convention (null → no
/// constraint, empty → <c>IS NULL</c>, non-empty → <c>= @id</c>); a known miss returns <see cref="Predicate.False"/>.
/// </summary>
internal static class QuantityColumnPredicate
{
    /// <summary>
    /// Builds the full quantity predicate: numeric range AND (optionally) system AND (optionally) code, or
    /// <see cref="Predicate.False"/> on a known-miss for either non-empty identity value. Column-name
    /// parameters let a composite pass the <c>*2</c> columns.
    /// </summary>
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
