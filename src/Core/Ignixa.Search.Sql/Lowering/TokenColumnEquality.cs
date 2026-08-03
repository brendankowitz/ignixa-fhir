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
    /// The number of leading code characters stored inline before overflow begins.
    /// </summary>
    /// <remarks>
    /// This mirrors the split point every token row generator writes with, which is NOT the declared width
    /// of the Code column — the DDL declares VARCHAR(256) for TokenSearchParam.Code and for every composite
    /// Code1/Code2, while every generator in both data layers splits at 128
    /// (<c>TokenSearchParameterRowGenerator</c>, <c>TokenStringCompositeRowGenerator</c>, and siblings, in
    /// Ignixa.DataLayer.SqlServer and Ignixa.DataLayer.SqlEntityFramework alike). The compiler has to match
    /// the data as written rather than the column as declared, so this constant is deliberately NOT read off
    /// <see cref="SqlCatalog"/> — that catalog "describes the schema, not storage convention", by its own
    /// doc comment, and a split point is storage convention. It must be changed in lockstep with the row
    /// generators.
    /// <para>
    /// Deriving it from <c>Column(codeColumn).MaxLength</c> instead is silently wrong and does not show up
    /// in this assembly's own tests, because those tests would read the same wrong number and agree with
    /// themselves. It shows up only against real written rows: every overflowing code stops matching. That
    /// is why this is a constant with a comment rather than a lookup.
    /// </para>
    /// </remarks>
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
