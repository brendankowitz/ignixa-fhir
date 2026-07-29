// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Enforces a global resource-type allow-list structurally on every row-producing stage. Unlike
/// <see cref="AccessConstraintApplier"/> — which narrows only the types it lists and leaves unlisted types
/// untouched (a per-type <em>narrowing</em>) — this is an allow-list: only the named types may appear in any
/// result, and a type absent from a non-empty list is denied outright. A narrowing cannot express "the
/// caller may see nothing else", so a type reached through an <c>_include</c> that happens to carry no
/// constraint would otherwise pass unguarded, which is a fail-open authorization bypass. This is the
/// SMART-on-FHIR clinical-scope allow-list, and the two concepts are enforced side by side.
/// </summary>
/// <remarks>
/// Enforcement runs on every stage that produces rows, matching where <see cref="AccessConstraintApplier"/>
/// runs:
/// <list type="bullet">
/// <item><description>The match set — single-type, multi-type, system-level, or <c>$everything</c> — via
/// <see cref="RestrictMatch"/>: it intersects the produced rows with the allowed types' base set, so a
/// single-type match on an unpermitted type produces nothing and a multi-type match keeps only its allowed
/// rows.</description></item>
/// <item><description>Each <c>_include</c>/<c>_revinclude</c>/<c>:iterate</c> stage via
/// <see cref="RestrictStage"/>: it intersects the stage's <see cref="IncludeStage.OutputTypeIds"/> with the
/// allowed ids, mirroring the legacy SQL generator, which renders exactly this as an
/// <c>outputTypeColumn IN (&lt;allowed ids&gt;)</c> filter on the include join.</description></item>
/// </list>
/// Chain targets are deliberately <em>not</em> filtered here; see the note at the allow-list enforcement
/// site in <see cref="Lower"/> for the parity rationale.
/// </remarks>
internal sealed class AllowedResourceTypeFilter
{
    private readonly IReadOnlyList<string> _allowedTypeNames;
    private readonly IReadOnlyList<short> _allowedTypeIds;

    /// <summary>
    /// Resolves the allow-list names to ids once. A name absent from the symbol table maps to
    /// <see cref="SymbolTable.UnmatchableResourceTypeId"/> (-1) rather than being dropped — the same
    /// fail-closed treatment <see cref="LeafContext.ResourceTypeIdOrSentinel"/> gives a multi-type list, so
    /// an all-unknown allow-list stays a set of ids that match no row instead of collapsing to empty and
    /// widening back to every type. Through <see cref="SearchSqlCompiler"/> every name is already resolved
    /// (Resolve collects the allow-list from the same <c>CompilationContext</c> Lower reads), so the
    /// sentinel only arises for a caller that hand-builds a context naming a type it never put in the table.
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
    /// Restricts the match set to the allow-list by intersecting it with the allowed types' base set. This
    /// is the whole match-set enforcement for every shape: a single-type match on an unpermitted type
    /// intersects to nothing (the base set does not contain that type), and a multi-type / system-level /
    /// <c>$everything</c> match keeps exactly its rows whose type is allowed. A plain intersect is correct
    /// here precisely because the allow-list is not a per-type narrowing — everything not on it is removed —
    /// so the subtract-then-union dance <see cref="AccessConstraintApplier.ApplyToTypes"/> needs (to keep
    /// unconstrained types) is neither needed nor wanted.
    /// </summary>
    public CteRef RestrictMatch(CteRef match, StructuralContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // LowerMultiTypeResourceSource resolves each name to its id or the unmatchable sentinel and is fed a
        // non-empty list here (IsEmpty is false at every call site), so it never trips ForTypes' empty guard.
        return context.Intersect(match, context.LowerMultiTypeResourceSource(_allowedTypeNames));
    }

    /// <summary>
    /// Restricts one include/:iterate stage to the allow-list by intersecting its
    /// <see cref="IncludeStage.OutputTypeIds"/> with the allowed ids. A wildcard stage (null output types,
    /// which fails open on its own) becomes the full allowed set — the case most in need of this, since a
    /// wildcard <c>_include</c> can otherwise reach any type.
    /// </summary>
    /// <remarks>
    /// The empty-intersection case is the fail-open hazard this method exists to close. The emitter renders
    /// the output-type filter only when <c>OutputTypeIds is { Count: &gt; 0 }</c>, so an EMPTY list would
    /// emit NO filter and the stage would fail OPEN — returning every type it can reach, the exact opposite
    /// of "this stage may return nothing". So when the intersection is empty this substitutes the single
    /// unmatchable sentinel (-1): the emitter renders it as <c>outputTypeColumn = -1</c>, which no real
    /// (always-positive) ResourceTypeId satisfies, making the stage provably empty.
    /// <para>
    /// Keeping the stage in place with a sentinel — rather than dropping it — is a deliberate safety choice.
    /// <see cref="IncludeStage.SeedStages"/> holds indices into <c>QueryPlan.Includes</c> computed by Lower's
    /// topological sort; dropping a stage would shift every later index and could cascade (a stage seeded
    /// only by the dropped one would itself need dropping), and getting that re-indexing wrong is an
    /// authorization or correctness bug. A sentinel stage leaves every index valid and simply contributes no
    /// rows: a later stage that seeds from it finds an empty inc CTE, which is the correct behaviour.
    /// </para>
    /// </remarks>
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
