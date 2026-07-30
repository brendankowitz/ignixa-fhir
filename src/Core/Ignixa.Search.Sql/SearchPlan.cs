using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// A lowered search, ready to emit. Inspect it with <c>Query.Explain()</c>, rewrite it with
/// <c>plan with { Query = rewritten }</c>, then call <see cref="Compile"/>.
/// </summary>
/// <remarks>
/// Constructing a plan never throws; validation happens in <see cref="Compile"/> so a rewritten plan is
/// checked on the same terms as the original.
/// </remarks>
public sealed record SearchPlan
{
    /// <summary>The lowered plan.</summary>
    public required QueryPlan Query { get; init; }

    /// <summary>
    /// The resource type this plan was compiled against, normalized: null means a system-level (cross-type)
    /// search. Exposed so callers read the compiler's own normalization rather than reimplementing it. A
    /// snapshot of the original compile — rewriting <see cref="Query"/> does not re-derive it.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>Build, Resolve, and Lower diagnostics. Null when <see cref="DiagnosticsLevel"/> is <see cref="SearchDiagnosticsLevel.None"/>.</summary>
    public SearchCompilationDiagnostics? Diagnostics { get; init; }

    /// <summary>Carried from <see cref="SearchPlanOptions.DiagnosticsLevel"/> so <see cref="Compile"/> emits at the same detail.</summary>
    public SearchDiagnosticsLevel DiagnosticsLevel { get; init; }

    /// <summary>Emits SQL, throwing <see cref="SearchCompilationException"/> when the plan cannot be emitted.</summary>
    public CompiledSearch Compile()
    {
        var result = TryCompile();
        return result.Succeeded ? result.Compiled : throw new SearchCompilationException(result.Failure);
    }

    /// <summary>Emits SQL, returning the failure as data when the plan cannot be emitted.</summary>
    public SearchCompilationResult TryCompile()
    {
        var includeTextRanges = DiagnosticsLevel == SearchDiagnosticsLevel.Full;

        EmittedSql emitted;
        try
        {
            emitted = SqlBuilder.Run(Query, new EmitOptions(includeTextRanges));
        }
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            var failure = new SearchCompilationFailure(
                CompilationStage.Emit, ex.Message, ParameterCode: null, Span: null, ex)
            {
                Diagnostics = DiagnosticsLevel == SearchDiagnosticsLevel.None ? null : Diagnostics,
            };

            return SearchCompilationResult.Failed(failure);
        }

        var compiled = new CompiledSearch(emitted.Sql, emitted.Parameters, Query)
        {
            Diagnostics = DiagnosticsLevel == SearchDiagnosticsLevel.None
                ? null
                : (Diagnostics ?? new SearchCompilationDiagnostics()) with
                {
                    SqlTextRanges = emitted.TextRanges ?? [],
                },
        };

        return SearchCompilationResult.Success(compiled);
    }
}
