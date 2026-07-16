using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The tier-1 (leaf) context: exposes symbol lookups and value parameterization only -- no CteRef,
/// no Intersect/Union, no sibling access. A leaf rule cannot see or affect the rest of the plan by
/// construction (design doc: "enforce the tier boundary as a type, not convention").
/// </summary>
public sealed class LeafContext
{
    private readonly SymbolTable _symbols;

    public LeafContext(SymbolTable symbols)
    {
        _symbols = symbols;
    }

    public short SearchParamId(SearchParameterInfo parameter) => _symbols.SearchParamId(parameter);

    public short ResourceTypeId(string resourceType) => _symbols.ResourceTypeId(resourceType);

    public SqlParameterRef Parameter(object value) => new(value);
}
