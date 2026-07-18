using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers one grouped compartment-membership entry (a single Reference-type search parameter, and
/// every resource type that shares it) to a CompartmentSource. Reuses ReferenceLoweringRule's exact
/// ReferenceResourceTypeId/ReferenceResourceId predicate construction -- compartment membership is,
/// structurally, an ordinary reference-equality predicate against a fixed (compartment type,
/// compartment id) pair; the only difference from an ordinary Observation?subject=Patient/123 search
/// is that CompartmentSource covers many resource types in one CTE instead of one.
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
        var predicate = new Predicate.And(
            new Predicate.Equal(
                new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"),
                context.Parameter(context.ResourceTypeId(compartmentType))),
            new Predicate.Equal(
                new SqlColumnRef(table.TableName, "ReferenceResourceId"),
                context.Parameter(compartmentId)));

        var resourceTypeIds = resourceTypes.Select(context.ResourceTypeId).ToList();
        return new CteDefinition.CompartmentSource(resourceTypeIds, context.SearchParamId(parameter), predicate);
    }
}
