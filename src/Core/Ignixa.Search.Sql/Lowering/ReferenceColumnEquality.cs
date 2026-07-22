using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the reference identity predicate shared by leaf and composite reference lowering. Produces a
/// left-associated predicate in BaseUri → optional ResourceType → ResourceId order, distinguishing
/// local references (<c>BaseUri IS NULL</c>) from external ones (<c>BaseUri = @p COLLATE BIN2</c>).
/// </summary>
internal static class ReferenceColumnEquality
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    public static Predicate Build(
        TableDescriptor table,
        string baseUriColumn,
        string resourceTypeColumn,
        string resourceIdColumn,
        ReferenceSearchValue value,
        LeafContext context)
    {
        // BaseUri: IS NULL for local, Equal with BIN2 collation for external.
        Predicate baseUriPredicate = value.BaseUri is null
            ? new Predicate.IsNull(new SqlColumnRef(table.TableName, baseUriColumn))
            : new Predicate.Equal(
                new SqlColumnRef(table.TableName, baseUriColumn),
                context.Parameter(value.BaseUri.ToString()),
                BinaryCollation);

        // Optional resource type, resolved through the symbol table.
        Predicate? typePredicate = string.IsNullOrEmpty(value.ResourceType)
            ? null
            : new Predicate.Equal(
                new SqlColumnRef(table.TableName, resourceTypeColumn),
                context.Parameter(context.ResourceTypeId(value.ResourceType)));

        // Required resource ID.
        Predicate idPredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, resourceIdColumn),
            context.Parameter(value.ResourceId));

        // Left-associated: (BaseUri AND Type) AND Id, or (BaseUri) AND Id when untyped.
        Predicate combined = typePredicate is not null
            ? new Predicate.And(new Predicate.And(baseUriPredicate, typePredicate), idPredicate)
            : new Predicate.And(baseUriPredicate, idPredicate);

        return combined;
    }
}
