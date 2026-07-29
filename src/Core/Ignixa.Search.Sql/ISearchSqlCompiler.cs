using Ignixa.Search.Models;
using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql;

/// <summary>
/// Compiles a FHIR search into a <see cref="SearchPlan"/>. Call <see cref="SearchPlan.Compile"/> on the
/// result to emit SQL. The split is deliberate: creating a plan reads storage symbols and is therefore
/// asynchronous, while emitting from a plan is pure — and the seam between them is where a caller can
/// inspect or rewrite the plan.
/// </summary>
public interface ISearchSqlCompiler
{
    /// <summary>Builds, resolves, and lowers a query string. Throws <see cref="SearchCompilationException"/> on failure.</summary>
    Task<SearchPlan> CreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves and lowers an already-built <see cref="SearchOptions"/>, skipping the build stage. Throws
    /// <see cref="SearchCompilationException"/> on failure.
    /// </summary>
    Task<SearchPlan> CreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>As <see cref="CreatePlanAsync"/>, returning the failure as data instead of throwing.</summary>
    Task<SearchPlanResult> TryCreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>As <see cref="CreatePlanFromOptionsAsync"/>, returning the failure as data instead of throwing.</summary>
    Task<SearchPlanResult> TryCreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);
}
