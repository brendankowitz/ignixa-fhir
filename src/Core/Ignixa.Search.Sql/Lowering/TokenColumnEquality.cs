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

        // The split point is the Code column's declared width. microsoft/fhir-server derives it the same
        // way (VLatest.TokenSearchParam.Code.Metadata.MaxLength), and so do this repo's row generators via
        // SearchParamColumnWidths, so a database either server populated splits here too. A literal that
        // drifted from the DDL would search for a prefix no row stores — every overflowing code would
        // silently match nothing.
        int inlineCodeWidth = table.Column(codeColumn).MaxLength
            ?? throw new NotSupportedException(
                $"Column {table.TableName}.{codeColumn} declares no width, so the code overflow split point is unknown.");

        return code switch
        {
            null or "" => null,
            { } shorter when shorter.Length < inlineCodeWidth => new Predicate.Equal(column, context.Parameter(code)),
            { } exact when exact.Length == inlineCodeWidth => new Predicate.And(
                new Predicate.IsNull(overflow),
                new Predicate.Equal(column, context.Parameter(code))),
            _ => new Predicate.And(
                new Predicate.Equal(column, context.Parameter(code[..inlineCodeWidth])),
                new Predicate.Equal(overflow, context.Parameter(code[inlineCodeWidth..]))),
        };
    }
}
