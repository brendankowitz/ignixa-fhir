using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Reproduces the argument list <see cref="Resolve.RunAsync"/> had before it was collapsed onto
/// <see cref="CompilationContext"/>, so the existing corpus of Resolve tests migrates by renaming the
/// call and nothing else. New tests should build a context with <see cref="CompilationContextFactory"/>
/// and call <see cref="Resolve.RunAsync"/> directly.
/// </summary>
internal static class ResolveHarness
{
    public static Task<ResolvedSymbols> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        IReadOnlyList<SortExpression> sort,
        ISymbolResolver resolver,
        string? targetResourceType,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null,
        IReadOnlyList<string>? additionalResourceTypes = null,
        IReadOnlyList<AccessConstraint>? accessConstraints = null)
    {
        var context = CompilationContextFactory.For(
            expression,
            targetResourceType,
            includes: includes,
            revIncludes: revIncludes,
            sort: sort,
            accessConstraints: accessConstraints,
            resourceTypes: additionalResourceTypes);

        var deps = new SymbolResolution(resolver, compartmentDefinitionManager, searchParameterDefinitionManager);

        return Resolve.RunAsync(context, deps, cancellationToken);
    }
}
