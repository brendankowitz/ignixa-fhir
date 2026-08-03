using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>Builds a <see cref="CompilationContext"/> for a test without going through the facade.</summary>
internal static class CompilationContextFactory
{
    public static readonly DateTimeOffset DefaultReferenceTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static CompilationContext For(
        Expression? expression,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression>? includes = null,
        IReadOnlyList<IncludeExpression>? revIncludes = null,
        IReadOnlyList<SortExpression>? sort = null,
        IReadOnlyList<AccessConstraint>? accessConstraints = null,
        IReadOnlyList<string>? resourceTypes = null,
        IReadOnlyList<string>? allowedResourceTypes = null,
        DateTimeOffset? approximationReferenceTime = null,
        ResourceVisibility? visibility = null,
        SurrogateIdRange? surrogateRange = null,
        SearchPlanOptions? options = null)
        => new()
        {
            Expression = expression,
            TargetResourceType = string.IsNullOrEmpty(targetResourceType) ? null : targetResourceType,
            Includes = includes ?? [],
            RevIncludes = revIncludes ?? [],
            Sort = sort ?? [],
            AccessConstraints = accessConstraints ?? [],
            ResourceTypes = resourceTypes ?? [],
            AllowedResourceTypes = allowedResourceTypes ?? [],
            ApproximationReferenceTime = approximationReferenceTime ?? DefaultReferenceTime,
            Visibility = visibility,
            SurrogateRange = surrogateRange,
            Options = options ?? new SearchPlanOptions(),
        };
}
