using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the token equality predicate for a composite's or leaf's token slot, supporting the full
/// FHIR token qualifier semantics: bare code, |code, system|, system|code, unknown system → false,
/// and text-only → unsupported.
/// </summary>
/// <remarks>
/// A code longer than <see cref="InlineCodeWidth"/> is stored split across two columns: the Code column
/// holds the first <see cref="InlineCodeWidth"/> characters and the CodeOverflow column holds the
/// REMAINDER. That is the opposite convention from StringSearchParam, whose TextOverflow holds the whole
/// value, so a long code cannot be matched by comparing either column alone — both halves are compared.
/// A code of exactly <see cref="InlineCodeWidth"/> characters needs an <c>CodeOverflow IS NULL</c> guard:
/// without it, the truncated prefix of a longer stored code would false-positive match. A shorter code
/// needs no guard, because a truncated prefix is always exactly <see cref="InlineCodeWidth"/> long and so
/// can never equal it — which keeps the common case a plain sargable equality against the indexed column.
/// </remarks>
internal static class TokenColumnEquality
{
    /// <summary>
    /// The number of leading code characters stored inline before overflow begins.
    /// </summary>
    /// <remarks>
    /// This mirrors the split point every token row generator writes with, which is NOT the declared width
    /// of the Code column (VARCHAR(256)) — the generators split at 128. The compiler has to match the data
    /// as written rather than the column as declared, so this constant is deliberately not read off
    /// <see cref="SqlCatalog"/>. It must be changed in lockstep with the row generators.
    /// </remarks>
    private const int InlineCodeWidth = 128;

    public static Predicate Build(TableDescriptor table, string systemColumn, string codeColumn, string overflowColumn, TokenSearchValue value, LeafContext context)
    {
        int? systemId = value.System is { Length: > 0 } system ? context.SystemId(system) : null;
        if (value.System is { Length: > 0 } missedSystem && systemId is null)
        {
            return new Predicate.False($"No resource uses the token system '{missedSystem}'.");
        }

        Predicate? systemPredicate = value.System switch
        {
            null => null,
            "" => new Predicate.IsNull(new SqlColumnRef(table.TableName, systemColumn)),
            _ => new Predicate.Equal(new SqlColumnRef(table.TableName, systemColumn), context.Parameter(systemId!.Value)),
        };

        Predicate? codePredicate = BuildCodePredicate(table, codeColumn, overflowColumn, value.Code, context);

        return (systemPredicate, codePredicate) switch
        {
            ({ } systemOnly, null) => systemOnly,
            (null, { } codeOnly) => codeOnly,
            ({ } systemPart, { } codePart) => new Predicate.And(systemPart, codePart),
            _ => throw new NotSupportedException("Token search requires a system or code; display text is not a code."),
        };
    }

    private static Predicate? BuildCodePredicate(TableDescriptor table, string codeColumn, string overflowColumn, string? code, LeafContext context)
    {
        var column = new SqlColumnRef(table.TableName, codeColumn);
        var overflow = new SqlColumnRef(table.TableName, overflowColumn);

        return code switch
        {
            null or "" => null,
            { Length: < InlineCodeWidth } => new Predicate.Equal(column, context.Parameter(code)),
            { Length: InlineCodeWidth } => new Predicate.And(
                new Predicate.IsNull(overflow),
                new Predicate.Equal(column, context.Parameter(code))),
            _ => new Predicate.And(
                new Predicate.Equal(column, context.Parameter(code[..InlineCodeWidth])),
                new Predicate.Equal(overflow, context.Parameter(code[InlineCodeWidth..]))),
        };
    }
}
