using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers one grouped compartment-membership entry (a Reference-type search parameter plus every
/// resource type that shares it) to a CompartmentSource. Structurally this is an ordinary
/// reference-equality predicate against a fixed (compartment type, compartment id) pair — the same shape
/// as an Observation?subject=Patient/123 search — except it covers many resource types in one CTE.
/// </summary>
public static class CompartmentLoweringRule
{
    public static CteDefinition.CompartmentSource Lower(
        SearchParameterInfo parameter,
        IReadOnlyList<string> resourceTypes,
        string compartmentType,
        string compartmentId,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var resourceTypeIds = resourceTypes.Select(context.ResourceTypeId).ToList();

        if (context.UnmatchableResourceType(compartmentType) is { } unmatchable)
        {
            return new CteDefinition.CompartmentSource(resourceTypeIds, context.SearchParamId(parameter), unmatchable);
        }

        Predicate predicate = new Predicate.And(
            new Predicate.Equal(
                new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"),
                context.Parameter(context.ResourceTypeId(compartmentType))),
            new Predicate.Equal(
                new SqlColumnRef(table.TableName, "ReferenceResourceId"),
                context.Parameter(compartmentId)));

        return new CteDefinition.CompartmentSource(resourceTypeIds, context.SearchParamId(parameter), predicate);
    }
}
