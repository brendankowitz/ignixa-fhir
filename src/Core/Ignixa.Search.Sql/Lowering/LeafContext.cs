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

    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType) => _symbols.CompartmentMembership(compartmentType);

    /// <inheritdoc cref="SymbolTable.NotReferencedPath"/>
    public SearchParameterInfo? NotReferencedPath(string sourceResourceType, string referencePath) => _symbols.NotReferencedPath(sourceResourceType, referencePath);

    /// <inheritdoc cref="SymbolTable.SystemId"/>
    public int? SystemId(string system) => _symbols.SystemId(system);

    /// <inheritdoc cref="SymbolTable.QuantityCodeId"/>
    public int? QuantityCodeId(string code) => _symbols.QuantityCodeId(code);

    /// <summary>
    /// The ResourceTypeIds a reference parameter declares it may point at. Empty when the parameter
    /// declares no targets, which leaves the reference unconstrained by type.
    /// </summary>
    /// <remarks>
    /// When the DB resolver could not find a declared target type, <c>Resolve.RunAsync</c> stored
    /// <see cref="SymbolTable.UnmatchableResourceTypeId"/> (-1) rather than omitting the key, so
    /// <see cref="SymbolTable.TryGetResourceTypeId"/> returns <c>(true, -1)</c> here and the
    /// sentinel is included rather than dropped. That sentinel contributes an OR arm that matches
    /// nothing — no catalog row carries id -1 — but it does not collapse the predicate. Dropping
    /// instead would be more dangerous: if the resolver missed every declared target, the resulting
    /// empty list falls through to the unconstrained id-only predicate, matching a reference to any
    /// resource type carrying that id. That is exactly the false-positive behaviour the
    /// type-narrowing pass exists to prevent.
    ///
    /// In the mixed case (<c>[Organization, UnknownType]</c>) the -1 arm contributes nothing to the
    /// OR and the resolvable arms still work correctly. This mirrors the convention established in
    /// <see cref="StructuralContext.LowerNotReferenced"/>, where a source type the resolver could
    /// not find also maps to the unmatchable sentinel.
    /// </remarks>
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
            if (_symbols.TryGetResourceTypeId(target, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public SqlParameterRef Parameter(object value) => new(value);
}
