using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>
/// The outcome of a <c>TryCreatePlan</c> call: exactly one of <see cref="Plan"/> and
/// <see cref="Failure"/> is non-null. Test <see cref="Succeeded"/> rather than either member.
/// </summary>
/// <remarks>
/// Constructed only through <see cref="Success"/> and <see cref="Failed"/>. The positional-record form
/// would publish a constructor that accepts both members, or neither, and a <c>with</c> expression would
/// reach the same contradictory states through the compiler-generated copy constructor -- which is what
/// makes the <see cref="MemberNotNullWhenAttribute"/> pair below sound rather than aspirational.
/// </remarks>
public sealed record SearchPlanResult
{
    private SearchPlanResult(SearchPlan? plan, SearchCompilationFailure? failure)
    {
        Plan = plan;
        Failure = failure;
    }

    /// <summary>The plan, when compilation reached one.</summary>
    public SearchPlan? Plan { get; }

    /// <summary>The failure, when it did not.</summary>
    public SearchCompilationFailure? Failure { get; }

    /// <summary>True when a plan was produced.</summary>
    [MemberNotNullWhen(true, nameof(Plan))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Plan is not null;

    /// <summary>A successful outcome carrying <paramref name="plan"/>.</summary>
    public static SearchPlanResult Success(SearchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SearchPlanResult(plan, failure: null);
    }

    /// <summary>A failed outcome carrying <paramref name="failure"/>.</summary>
    public static SearchPlanResult Failed(SearchCompilationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new SearchPlanResult(plan: null, failure);
    }
}
