using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The leaf (tier-1) context: exposes symbol lookups and value parameterization only — no CteRef, no
/// Intersect/Union, no sibling access. By construction a leaf rule cannot see or affect the rest of the
/// plan, making the tier boundary a type rather than a convention.
/// </summary>
public sealed class LeafContext
{
    private readonly SymbolTable _symbols;

    public LeafContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime = null)
    {
        _symbols = symbols;
        ApproximationReferenceTime = approximationReferenceTime;
    }

    public DateTimeOffset? ApproximationReferenceTime { get; }

    public short SearchParamId(SearchParameterInfo parameter) => _symbols.SearchParamId(parameter);

    public short ResourceTypeId(string resourceType) => _symbols.ResourceTypeId(resourceType);

    /// <summary>
    /// The unsatisfiable predicate for a resource type the resolver could not find, or
    /// <see langword="null"/> when it resolved normally.
    /// </summary>
    /// <remarks>
    /// <see cref="SymbolTable.UnmatchableResourceTypeId"/> matches no row, so a plain
    /// <c>Equal(ResourceTypeId, -1)</c> is already unsatisfiable — but only
    /// <see cref="Predicate.False"/> carries the reason, and SearchCompiler.MarkKnownMisses recognises
    /// nothing else. Emitting it here makes an unknown resource type as diagnosable as the unknown token
    /// system handled in <see cref="TokenColumnEquality"/>, instead of a miss the caller can only find by
    /// reading the emitted SQL for a magic -1.
    /// </remarks>
    public Predicate.False? UnmatchableResourceType(string resourceType)
        => ResourceTypeId(resourceType) == SymbolTable.UnmatchableResourceTypeId
            ? new Predicate.False($"No resource type '{resourceType}' exists in the catalog.")
            : null;

    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType) => _symbols.CompartmentMembership(compartmentType);

    /// <inheritdoc cref="SymbolTable.SystemId"/>
    public int? SystemId(string system) => _symbols.SystemId(system);

    /// <inheritdoc cref="SymbolTable.QuantityCodeId"/>
    public int? QuantityCodeId(string code) => _symbols.QuantityCodeId(code);

    public SqlParameterRef Parameter(object value) => new(value);
}
