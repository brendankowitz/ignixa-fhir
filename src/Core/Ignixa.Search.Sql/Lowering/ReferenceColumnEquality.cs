using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the reference identity predicate shared by leaf and composite reference lowering. Produces a
/// left-associated predicate in optional BaseUri → optional ResourceType → ResourceId order, branching on
/// <see cref="ReferenceSearchValue.Kind"/>.
/// </summary>
/// <remarks>
/// The spec requires that "a relative reference resolving to the same value as a specified absolute URL,
/// or vice versa, qualifies as a match". That reconciliation is achieved in two halves, mirroring the
/// reference implementation:
/// <list type="number">
///   <item>
///     <see cref="ReferenceSearchValueParser"/> collapses an absolute URL whose base equals this server's
///     base to <see cref="ReferenceKind.Internal"/> with a null BaseUri. Because the same parser runs on
///     both the index and the query path, the two forms converge on one representation before reaching
///     SQL — so an absolute self-reference search finds a relatively-stored row.
///   </item>
///   <item>
///     A bare relative search value is <see cref="ReferenceKind.InternalOrExternal"/> and emits no BaseUri
///     predicate at all, so it matches a stored row whether or not that row carries a base — the "or vice
///     versa" direction.
///   </item>
/// </list>
/// Emitting <c>BaseUri IS NULL</c> for the InternalOrExternal case, as a strict local/external XOR would,
/// breaks the second half. Only a value the parser positively identified as Internal may demand a null base.
///
/// No COLLATE override is emitted: dbo.ReferenceSearchParam.BaseUri is already declared
/// COLLATE Latin1_General_100_CS_AS. Forcing BIN2 on the column side made equality incompatible with the
/// index key ordering for no semantic gain over URI characters.
/// </remarks>
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

        // A value the parser could not resolve to a resource type is still constrained: the search
        // parameter itself declares which types it may point at, and the shipping engine narrows to
        // them. Without this a bare id matches a reference to any type carrying that id, which
        // returns rows that are not matches.
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

            // A stored row may carry a null type when the reference was indexed untyped; the
            // shipping engine admits those when there are multiple declared targets (where type
            // ambiguity exists at index time). For single-target parameters the shipping engine
            // uses a strict equality match and does not admit null-typed rows.
            if (declared.Count > 1)
            {
                targets = new Predicate.Or(targets, new Predicate.IsNull(new SqlColumnRef(table.TableName, resourceTypeColumn)));
            }

            return new Predicate.And(targets, idPredicate);
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
    /// therefore be left unconstrained.
    /// </summary>
    private static Predicate? BuildBaseUriPredicate(
        TableDescriptor table,
        string baseUriColumn,
        ReferenceSearchValue value,
        LeafContext context)
    {
        var column = new SqlColumnRef(table.TableName, baseUriColumn);

        if (value.BaseUri is not null)
        {
            return new Predicate.Equal(column, context.Parameter(value.BaseUri.ToString()));
        }

        return value.Kind == ReferenceKind.Internal ? new Predicate.IsNull(column) : null;
    }
}
