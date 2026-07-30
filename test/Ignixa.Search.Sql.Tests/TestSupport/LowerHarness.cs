using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Flattens <see cref="Lower.Run"/>'s inputs into one positional argument list, mapping them onto the
/// option unions so a test can state just the lowering inputs it cares about.
/// </summary>
internal static class LowerHarness
{
    public static LoweredPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        IReadOnlyList<SortExpression> sort,
        SortPhase sortPhase,
        PageSpec? page,
        LowerOptions? options = null)
    {
        options ??= new LowerOptions();

        // Paging now hangs off ResultShape.Matches, so a count or an includes page has nowhere to carry one.
        // Fail loudly rather than dropping the argument: a test that asks for the combination is asking for
        // something the API no longer expresses, and a silent drop would let it pass while proving nothing.
        if ((options.CountOnly || options.IncludesOnly)
            && (page is not null || options.Top is not null || options.OffsetPage is not null))
        {
            throw new InvalidOperationException(
                "A count or includes-page shape cannot carry paging: SearchPaging hangs off ResultShape.Matches. " +
                "See ResultShapeUnionTests for the type-level pin that replaced the runtime guards.");
        }

        // The harness takes each lowering input as a separate argument and maps them onto the option unions.
        var context = CompilationContextFactory.For(
            expression,
            targetResourceType,
            includes: includes,
            revIncludes: revIncludes,
            sort: sort,
            accessConstraints: options.AccessConstraints,
            resourceTypes: options.ResourceTypes,
            allowedResourceTypes: options.AllowedResourceTypes,
            approximationReferenceTime: options.ApproximationReferenceTime,
            visibility: options.Visibility,
            surrogateRange: options.SurrogateRange,
            options: new SearchPlanOptions
            {
                Shape = options.CountOnly
                    ? options.CountPhaseScoped
                        ? new ResultShape.Count.CurrentSortPhase()
                        : new ResultShape.Count.AllMatches()
                    : options.IncludesOnly
                        ? new ResultShape.IncludesPage(options.IncludeBoundary)
                        : new ResultShape.Matches(options.OffsetPage is { } offset
                            ? new SearchPaging.Offset(offset)
                            : new SearchPaging.Keyset(options.Top, page)),
                IncludeLimit = includeLimit,
                SortPhase = sortPhase,
                SearchParameterHash = options.SearchParameterHash?.Value as string,
            });

        return Lower.Run(
            context with
            {
                TargetResourceType = ResolveTargetType(targetResourceType, options),
                SystemLevelSearch = options.SystemLevelSearch,
            },
            symbols);
    }

    /// <summary>
    /// Lowers with no paging at all — the shape <see cref="Run"/> cannot produce for a match plan, since it
    /// always materialises a <c>Keyset</c> to carry its <c>top</c> and <c>page</c> arguments.
    /// </summary>
    public static LoweredPlan RunWithoutPaging(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<SortExpression> sort,
        SortPhase sortPhase = SortPhase.Valued)
    {
        var context = CompilationContextFactory.For(
            expression,
            targetResourceType,
            includes: [],
            revIncludes: [],
            sort: sort,
            options: new SearchPlanOptions { SortPhase = sortPhase });

        return Lower.Run(context, symbols);
    }

    /// <summary>
    /// <c>LowerOptions.SystemLevelSearch</c> and <c>targetResourceType</c> were independent inputs before the
    /// collapse: a null target with the flag <c>false</c> is a wildcard compartment search, distinct from a
    /// system-level search (flag <c>true</c>). The context carries the flag explicitly (see
    /// <see cref="CompilationContext.SystemLevelSearch"/>); this only mirrors the original rule that setting
    /// the flag with a named target asked for cross-type leaf lowering, which the context expresses as a null
    /// target.
    /// </summary>
    private static string? ResolveTargetType(string? targetResourceType, LowerOptions options)
        => options.SystemLevelSearch ? null : (string.IsNullOrEmpty(targetResourceType) ? null : targetResourceType);
}
