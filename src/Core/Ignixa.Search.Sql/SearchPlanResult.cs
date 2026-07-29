using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>The outcome of a <c>TryCreatePlan</c> call: exactly one of <see cref="Plan"/> or <see cref="Failure"/> is non-null.</summary>
public sealed record SearchPlanResult(SearchPlan? Plan, SearchCompilationFailure? Failure)
{
    /// <summary>True when a plan was produced.</summary>
    [MemberNotNullWhen(true, nameof(Plan))]
    public bool Succeeded => Plan is not null;
}
