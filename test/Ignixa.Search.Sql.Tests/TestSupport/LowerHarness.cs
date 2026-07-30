using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Reproduces the argument list <see cref="Lower.Run"/> had before it was collapsed onto
/// <c>CompilationContext</c>. Every argument, including the <see cref="LowerOptions"/> initialiser, is
/// unchanged, so migrating a test is a one-token rename.
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

        // The harness keeps the old flat flags and maps them onto the closed option unions, so the call sites
        // it exists to preserve stay unchanged.
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
                    ? new ResultShape.Count(RestrictToSortPhase: options.CountPhaseScoped)
                    : options.IncludesOnly
                        ? new ResultShape.IncludesPage(options.IncludeBoundary)
                        : ResultShape.Default,
                IncludeLimit = includeLimit,
                Paging = options.OffsetPage is { } offset
                    ? new SearchPaging.Offset(offset) { Phase = sortPhase }
                    : new SearchPaging.Keyset(options.Top, page) { Phase = sortPhase },
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
