using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the reference identity predicate shared by leaf and composite reference lowering. Left-associated
/// in optional BaseUri → optional ResourceType → ResourceId order, branching on
/// <see cref="ReferenceSearchValue.Kind"/>. InternalOrExternal emits no BaseUri predicate so a relative
/// search matches a stored row with or without a base; emitting <c>BaseUri IS NULL</c> there would break that.
/// </summary>
internal static class ReferenceColumnEquality
{
    public static Predicate Build(
        TableDescriptor table,
        string baseUriColumn,
        string resourceTypeColumn,
        string resourceIdColumn,
        ReferenceSearchValue value,
        LeafContext context,
        SearchParameterInfo parameter)
    {
        Predicate idPredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, resourceIdColumn),
            context.Parameter(value.ResourceId));

        // A value the parser could not resolve to a resource type is still constrained to the types the
        // search parameter declares (matching the shipping engine). Without this a bare id matches a
        // reference to any type carrying that id, returning non-matches.
        if (string.IsNullOrEmpty(value.ResourceType))
        {
            var declared = context.DeclaredTargetResourceTypeIds(parameter);
            if (declared.Count == 0)
            {
                return idPredicate;
            }

            Predicate targets = new Predicate.Equal(
                new SqlColumnRef(table.TableName, resourceTypeColumn),
                context.Parameter(declared[0]));

            for (var i = 1; i < declared.Count; i++)
            {
                targets = new Predicate.Or(
                    targets,
                    new Predicate.Equal(
                        new SqlColumnRef(table.TableName, resourceTypeColumn),
                        context.Parameter(declared[i])));
            }

            // A stored NULL type means the reference was indexed without resolvable type info — ambiguous
            // only when the parameter admits several target types. For a single-target parameter the type
            // is unambiguous, so admitting NULL rows would widen the match (matching the shipping engine).
            if (declared.Count > 1)
            {
                targets = new Predicate.Or(targets, new Predicate.IsNull(new SqlColumnRef(table.TableName, resourceTypeColumn)));
            }

            return new Predicate.And(targets, idPredicate);
        }

        if (context.UnmatchableResourceType(value.ResourceType) is { } unmatchable)
        {
            return unmatchable;
        }

        Predicate typePredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, resourceTypeColumn),
            context.Parameter(context.ResourceTypeId(value.ResourceType)));

        Predicate? baseUriPredicate = BuildBaseUriPredicate(table, baseUriColumn, value, context);

        return baseUriPredicate is not null
            ? new Predicate.And(new Predicate.And(baseUriPredicate, typePredicate), idPredicate)
            : new Predicate.And(typePredicate, idPredicate);
    }

    /// <summary>
    /// The BaseUri constraint, or null when the reference may be internal or external and the base must
    /// therefore be left unconstrained. Branches on <see cref="ReferenceSearchValue.Kind"/>, not on BaseUri:
    /// reading BaseUri first would let an External value with no base fail open into the InternalOrExternal
    /// match set (that pair is rejected by <see cref="ReferenceSearchValue"/>'s ctor, but don't rely on it).
    /// </summary>
    private static Predicate? BuildBaseUriPredicate(
        TableDescriptor table,
        string baseUriColumn,
        ReferenceSearchValue value,
        LeafContext context)
    {
        var column = new SqlColumnRef(table.TableName, baseUriColumn);

        return value.Kind switch
        {
            ReferenceKind.Internal => new Predicate.IsNull(column),
            ReferenceKind.External => value.BaseUri is { } externalBase
                ? new Predicate.Equal(column, context.Parameter(externalBase.ToString()))
                : throw new InvalidOperationException(
                    $"External reference to {value.ResourceType}/{value.ResourceId} carries no base URI."),
            ReferenceKind.InternalOrExternal => value.BaseUri is not null
                ? new Predicate.Equal(column, context.Parameter(value.BaseUri.ToString()))
                : null,
            _ => throw new NotSupportedException($"Unknown ReferenceKind '{value.Kind}'."),
        };
    }
}
