using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a ReferenceToken composite to a single ParamSource over ReferenceTokenCompositeSearchParam.
/// Finds the Reference and Token components by their runtime ISearchValue type, not by array index --
/// some component definitions swap expressions (e.g. DocumentReference's relationship composite), so
/// the write path (RefTokenCompositeRowGenerator.cs) already resolves roles this way too. Mirrors
/// ReferenceLoweringRule's BaseUri throw and typed/untyped ResourceTypeId/ResourceId logic, and
/// TokenColumnEquality for the token slot.
/// </summary>
public static class ReferenceTokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short resourceTypeId)
    {
        var referenceComponent = components.FirstOrDefault(c => c.Value is ReferenceSearchValue)
            ?? throw new NotSupportedException($"ReferenceToken composite '{compositeParameter.Code}' has no Reference-typed component.");
        var tokenComponent = components.FirstOrDefault(c => c.Value is TokenSearchValue)
            ?? throw new NotSupportedException($"ReferenceToken composite '{compositeParameter.Code}' has no Token-typed component.");

        var table = SqlCatalog.Default.Table("ReferenceTokenCompositeSearchParam");

        var referencePredicate = ReferenceColumnEquality((ReferenceSearchValue)referenceComponent.Value, table, context);
        var tokenPredicate = TokenColumnEquality.Build(table, "Code2", (TokenSearchValue)tokenComponent.Value, context);

        var predicate = new Predicate.And(referencePredicate, tokenPredicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate ReferenceColumnEquality(ReferenceSearchValue value, TableDescriptor table, LeafContext context)
    {
        if (value.BaseUri is not null)
        {
            throw new NotSupportedException(
                $"Absolute/external reference search (BaseUri '{value.BaseUri}') is not supported by ReferenceTokenLoweringRule, " +
                "matching ReferenceLoweringRule's own scope note.");
        }

        var idPredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "ReferenceResourceId1"), context.Parameter(value.ResourceId));

        return string.IsNullOrEmpty(value.ResourceType)
            ? idPredicate
            : new Predicate.And(
                new Predicate.Equal(
                    new SqlColumnRef(table.TableName, "ReferenceResourceTypeId1"),
                    context.Parameter(context.ResourceTypeId(value.ResourceType))),
                idPredicate);
    }
}
