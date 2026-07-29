using Ignixa.Search.Models;
using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql;

/// <summary>
/// Compiles a FHIR search into a <see cref="SearchPlan"/>. Call <see cref="SearchPlan.Compile"/> on the
/// result to emit SQL. The split is deliberate: creating a plan reads storage symbols and is therefore
/// asynchronous, while emitting from a plan is pure — and the seam between them is where a caller can
/// inspect or rewrite the plan.
/// </summary>
/// <remarks>
/// Three exception contracts, and the difference between them is the point:
/// <list type="bullet">
/// <item>A compilation failure — an unresolved search parameter, a construct the lowerer refuses — throws
/// <see cref="SearchCompilationException"/> from the <c>CreatePlan</c> methods and is returned as a
/// <see cref="SearchCompilationFailure"/> by the <c>TryCreatePlan</c> ones.</item>
/// <item>A query-string parse error surfaces as the original <c>FhirException</c> the options builder threw,
/// deliberately <em>not</em> wrapped, so the FHIR layer above can render its <c>OperationOutcome</c> issues
/// unchanged. Only <see cref="TryCreatePlanAsync"/> converts it, into a
/// <see cref="CompilationStage.Build"/> failure.</item>
/// <item>A missing dependency is programmer error, not caller input, so it throws
/// <see cref="InvalidOperationException"/> naming itself from every entry point that needs it — the
/// <c>TryCreatePlan</c> methods included. They trade throwing for data on failed <em>compiles</em>, not on
/// a misconfigured compiler.</item>
/// </list>
/// </remarks>
public interface ISearchSqlCompiler
{
    /// <summary>
    /// Builds, resolves, and lowers a query string. Requires an <c>ISearchOptionsBuilder</c>. See the
    /// remarks on <see cref="ISearchSqlCompiler"/> for what it throws.
    /// </summary>
    Task<SearchPlan> CreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves and lowers an already-built <see cref="SearchOptions"/>, skipping the build stage — and so
    /// needs no <c>ISearchOptionsBuilder</c>. Throws <see cref="SearchCompilationException"/> on failure.
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
