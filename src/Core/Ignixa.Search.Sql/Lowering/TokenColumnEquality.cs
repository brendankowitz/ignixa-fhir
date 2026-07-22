using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the token equality predicate for a composite's or leaf's token slot, supporting the full
/// FHIR token qualifier semantics: bare code, |code, system|, system|code, unknown system → false,
/// and text-only → unsupported.
/// </summary>
internal static class TokenColumnEquality
{
    public static Predicate Build(TableDescriptor table, string systemColumn, string codeColumn, TokenSearchValue value, LeafContext context)
    {
        int? systemId = value.System is { Length: > 0 } system ? context.SystemId(system) : null;
        if (value.System is { Length: > 0 } && systemId is null)
        {
            return new Predicate.False();
        }

        Predicate? systemPredicate = value.System switch
        {
            null => null,
            "" => new Predicate.IsNull(new SqlColumnRef(table.TableName, systemColumn)),
            _ => new Predicate.Equal(new SqlColumnRef(table.TableName, systemColumn), context.Parameter(systemId!.Value)),
        };

        Predicate? codePredicate = string.IsNullOrEmpty(value.Code)
            ? null
            : new Predicate.Equal(new SqlColumnRef(table.TableName, codeColumn), context.Parameter(value.Code));

        return (systemPredicate, codePredicate) switch
        {
            ({ } systemOnly, null) => systemOnly,
            (null, { } codeOnly) => codeOnly,
            ({ } systemPart, { } codePart) => new Predicate.And(systemPart, codePart),
            _ => throw new NotSupportedException("Token search requires a system or code; display text is not a code."),
        };
    }
}
