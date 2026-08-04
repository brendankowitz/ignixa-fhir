using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the token equality predicate for a leaf's or composite's token slot, supporting the full FHIR
/// token qualifier semantics (bare code, |code, system|, system|code, unknown system → false, text-only →
/// unsupported). A long code splits across Code + CodeOverflow (leading chars / remainder — opposite to
/// StringSearchParam), so both halves compare; an exactly-width code adds a <c>CodeOverflow IS NULL</c> guard.
/// </summary>
internal static class TokenColumnEquality
{
    /// <summary>
    /// The character count at which the row generators split an overflowing token code: the leading
    /// <see cref="InlineCodeWidth"/> characters go to the Code column and the remainder to CodeOverflow.
    /// This is a storage convention, not a schema fact, and it must change in lockstep with the literal
    /// 128 in every token row generator — the writers and this reader are deliberately two independent
    /// sources of truth, so a divergence is caught rather than propagated.
    /// <para>
    /// It is deliberately NOT read from <c>SqlCatalog</c>, whose own summary says it "describes the
    /// schema, not storage convention". The Code column is declared VARCHAR(256) while the generators
    /// split at 128, so deriving the split point from the column width searches for a prefix no row ever
    /// stores: every code longer than 128 characters silently matches nothing. Tests inside this assembly
    /// cannot detect that, because the only width they can read is the same one the rule would read;
    /// the guard lives in the row generators' test project instead.
    /// </para>
    /// </summary>
    internal const int InlineCodeWidth = 128;

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
            { } shorter when shorter.Length < InlineCodeWidth => new Predicate.Equal(column, context.Parameter(code)),
            { } exact when exact.Length == InlineCodeWidth => new Predicate.And(
                new Predicate.IsNull(overflow),
                new Predicate.Equal(column, context.Parameter(code))),
            _ => new Predicate.And(
                new Predicate.Equal(column, context.Parameter(code[..InlineCodeWidth])),
                new Predicate.Equal(overflow, context.Parameter(code[InlineCodeWidth..]))),
        };
    }
}
