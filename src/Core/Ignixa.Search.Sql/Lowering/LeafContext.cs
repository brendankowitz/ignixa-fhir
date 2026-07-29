using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The leaf (tier-1) context: exposes symbol lookups and value parameterization only — no CteRef, no
/// Intersect/Union, no sibling access. By construction a leaf rule cannot see or affect the rest of the
/// plan, making the tier boundary a type rather than a convention.
/// </summary>
internal sealed class LeafContext
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
    /// Looks up a resource type's id without throwing, mapping an unfound or never-collected type to
    /// <see cref="SymbolTable.UnmatchableResourceTypeId"/> (-1) so multi-type callers can keep the entry;
    /// dropping it would collapse an all-unknown list to empty, widening to all types.
    /// </summary>
    public short ResourceTypeIdOrSentinel(string resourceType)
        => _symbols.TryGetResourceTypeId(resourceType, out var id)
            ? id
            : SymbolTable.UnmatchableResourceTypeId;

    /// <summary>
    /// The unsatisfiable predicate for a resource type the resolver could not find, or
    /// <see langword="null"/> when it resolved normally. Uses <see cref="Predicate.False"/> (not a bare
    /// <c>Equal(ResourceTypeId, -1)</c>) so the miss carries a reason CompilationDiagnosticsBuilder can report.
    /// </summary>
    public Predicate.False? UnmatchableResourceType(string resourceType)
        => ResourceTypeId(resourceType) == SymbolTable.UnmatchableResourceTypeId
            ? new Predicate.False($"No resource type '{resourceType}' exists in the catalog.")
            : null;

    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType) => _symbols.CompartmentMembership(compartmentType);

    /// <inheritdoc cref="SymbolTable.NotReferencedPath"/>
    public SearchParameterInfo? NotReferencedPath(string sourceResourceType, string referencePath) => _symbols.NotReferencedPath(sourceResourceType, referencePath);

    /// <inheritdoc cref="SymbolTable.SystemId"/>
    public int? SystemId(string system) => _symbols.SystemId(system);

    /// <inheritdoc cref="SymbolTable.QuantityCodeId"/>
    public int? QuantityCodeId(string code) => _symbols.QuantityCodeId(code);

    /// <summary>
    /// The ResourceTypeIds a reference parameter declares it may point at; empty leaves it unconstrained by
    /// type. Unknown targets map to the unmatchable sentinel (-1), not dropped: dropping all of them falls
    /// through to the id-only predicate and matches any type carrying that id — the false positive the
    /// type-narrowing pass exists to prevent.
    /// </summary>
    public IReadOnlyList<short> DeclaredTargetResourceTypeIds(SearchParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter.TargetResourceTypes is not { Count: > 0 } targets)
        {
            return [];
        }

        var ids = new List<short>(targets.Count);
        foreach (var target in targets)
        {
            ids.Add(ResourceTypeIdOrSentinel(target));
        }

        return ids;
    }

    public SqlParameterRef Parameter(object value) => new(value);
}
