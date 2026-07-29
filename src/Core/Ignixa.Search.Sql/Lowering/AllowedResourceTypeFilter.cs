// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Enforces a global resource-type allow-list on every row-producing stage (the SMART-on-FHIR clinical
/// scope). Unlike <see cref="AccessConstraintApplier"/>'s per-type narrowing, only named types may appear;
/// an unlisted type reached via <c>_include</c> would otherwise fail open. Chain targets are deliberately
/// not filtered here — see the enforcement site in <see cref="Lower"/>.
/// </summary>
internal sealed class AllowedResourceTypeFilter
{
    private readonly IReadOnlyList<string> _allowedTypeNames;
    private readonly IReadOnlyList<short> _allowedTypeIds;

    /// <summary>
    /// Resolves the allow-list names to ids once. A name absent from the symbol table maps to
    /// <see cref="SymbolTable.UnmatchableResourceTypeId"/> (-1) rather than being dropped, so an all-unknown
    /// allow-list matches no row instead of collapsing to empty and widening back to every type.
    /// </summary>
    public AllowedResourceTypeFilter(IReadOnlyList<string>? allowedResourceTypes, SymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        if (allowedResourceTypes is not { Count: > 0 })
        {
            _allowedTypeNames = Array.Empty<string>();
            _allowedTypeIds = Array.Empty<short>();
            return;
        }

        _allowedTypeNames = allowedResourceTypes;
        _allowedTypeIds = allowedResourceTypes
            .Select(name => symbols.TryGetResourceTypeId(name, out var id) ? id : SymbolTable.UnmatchableResourceTypeId)
            .ToList();
    }

    /// <summary>Whether the allow-list is inactive (null or empty). Every enforcement site short-circuits on
    /// this so an unrestricted plan is byte-identical to one compiled before the allow-list existed —
    /// the same "empty is inert" philosophy as <see cref="AccessConstraintApplier.IsEmpty"/>.</summary>
    public bool IsEmpty => _allowedTypeIds.Count == 0;

    /// <summary>
    /// Restricts the match set to the allow-list by intersecting it with the allowed types' base set —
    /// correct for every match shape because the allow-list removes everything not on it, so the
    /// subtract-then-union dance <see cref="AccessConstraintApplier.ApplyToTypes"/> needs is not wanted here.
    /// </summary>
    public CteRef RestrictMatch(CteRef match, StructuralContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // LowerMultiTypeResourceSource resolves each name to its id or the unmatchable sentinel and is fed a
        // non-empty list here (IsEmpty is false at every call site), so it never trips ForTypes' empty guard.
        return context.Intersect(match, context.LowerMultiTypeResourceSource(_allowedTypeNames));
    }

    /// <summary>
    /// Restricts one include/:iterate stage by intersecting its <see cref="IncludeStage.OutputTypeIds"/>
    /// with the allowed ids; a wildcard stage (null output types) becomes the full allowed set. An empty
    /// intersection substitutes the unmatchable sentinel (-1), not an empty list (which the emitter renders
    /// as no filter — fail open); the stage is kept, not dropped, to preserve <c>QueryPlan.Includes</c> indices.
    /// </summary>
    public IncludeStage RestrictStage(IncludeStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        IReadOnlyList<short> restricted = stage.OutputTypeIds is null
            ? _allowedTypeIds
            : stage.OutputTypeIds.Where(_allowedTypeIds.Contains).ToList();

        if (restricted.Count == 0)
        {
            restricted = new[] { SymbolTable.UnmatchableResourceTypeId };
        }

        return stage with { OutputTypeIds = restricted };
    }
}
