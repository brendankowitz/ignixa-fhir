using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>
/// The outcome of a <c>TryCreatePlan</c> call. On a result the compiler returned, exactly one of
/// <see cref="Plan"/> and <see cref="Failure"/> is non-null; test <see cref="Succeeded"/> rather than
/// either member.
/// </summary>
public sealed record SearchPlanResult(SearchPlan? Plan, SearchCompilationFailure? Failure)
{
    /// <summary>True when a plan was produced.</summary>
    [MemberNotNullWhen(true, nameof(Plan))]
    public bool Succeeded => Plan is not null;
}
