using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Sql;

/// <summary>
/// The compiler's only orchestrator. <paramref name="optionsBuilder"/> is required only by the query-string
/// entry points; the definition managers only by compartment searches, <c>$everything</c>, and
/// <c>_not-referenced</c> filters. Each throws <see cref="InvalidOperationException"/> naming itself when a
/// query needs it and it was not supplied.
/// </summary>
public sealed class SearchSqlCompiler(
    ISymbolResolver resolver,
    ISearchOptionsBuilder? optionsBuilder = null,
    ICompartmentDefinitionManager? compartmentDefinitionManager = null,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager = null,
    TimeProvider? timeProvider = null) : ISearchSqlCompiler
{
    private readonly SymbolResolution _deps = new(
        resolver ?? throw new ArgumentNullException(nameof(resolver)),
        compartmentDefinitionManager,
        searchParameterDefinitionManager);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SearchPlan> CreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await TryCreatePlanCoreAsync(resourceType, parameters, options, rethrowBuildFailures: true, cancellationToken);
        return result.Succeeded ? result.Plan : throw new SearchCompilationException(result.Failure);
    }

    public async Task<SearchPlanResult> TryCreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
        => await TryCreatePlanCoreAsync(resourceType, parameters, options, rethrowBuildFailures: false, cancellationToken);

    public async Task<SearchPlan> CreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await TryCreatePlanFromOptionsAsync(searchOptions, resourceType, options, cancellationToken);
        return result.Succeeded ? result.Plan : throw new SearchCompilationException(result.Failure);
    }

    public async Task<SearchPlanResult> TryCreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);

        options ??= new SearchPlanOptions();

        return await RunAsync(searchOptions, resourceType, options, outcomes: [], implicitParameters: [], cancellationToken);
    }

    private async Task<SearchPlanResult> TryCreatePlanCoreAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options,
        bool rethrowBuildFailures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        options ??= new SearchPlanOptions();

        if (optionsBuilder is null)
        {
            throw new InvalidOperationException(
                $"Compiling a query string requires an {nameof(ISearchOptionsBuilder)}; none was supplied to {nameof(SearchSqlCompiler)}.");
        }

        var outcomes = new List<ParameterTrace>();
        var traced = options.DiagnosticsLevel != SearchDiagnosticsLevel.None;

        SearchOptions searchOptions;
        try
        {
            searchOptions = optionsBuilder.Build(resourceType, parameters, schemaProvider: null, outcomes);
        }
        // Diagnostics carry whatever the builder collected before it threw, on the same terms as every
        // other failure path. Implicit parameters are necessarily empty: detecting them compares the
        // caller's parameters against the built SearchOptions, and there is no built SearchOptions yet.
        catch (FhirException ex) when (!rethrowBuildFailures)
        {
            return SearchPlanResult.Failed(
                new SearchCompilationFailure(CompilationStage.Build, ex.Message, ParameterCode: null, Span: null, ex)
                {
                    Diagnostics = Diagnostics(traced, outcomes, implicitParameters: [], planTrace: null),
                });
        }

        IReadOnlyList<ImplicitParameter> implicitParameters = traced
            ? CompilationDiagnosticsBuilder.DetectImplicit(parameters, searchOptions)
            : [];

        return await RunAsync(searchOptions, resourceType, options, outcomes, implicitParameters, cancellationToken);
    }

    private async Task<SearchPlanResult> RunAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions options,
        List<ParameterTrace> outcomes,
        IReadOnlyList<ImplicitParameter> implicitParameters,
        CancellationToken cancellationToken)
    {
        var traced = options.DiagnosticsLevel != SearchDiagnosticsLevel.None;

        CompilationContext context;
        try
        {
            context = CompilationContext.Create(searchOptions, resourceType, options, _timeProvider.GetUtcNow());
        }
        // Create eagerly maps ResourceVersionTypes and the surrogate bounds; both throw NotSupportedException
        // on a malformed SearchOptions. Reported at Build — it is input mapping, and runs before Resolve — so
        // the stage names where the failure actually happened, and the Try* contract holds: caller-input
        // errors come back as data, not a throw.
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            var failure = CompilationDiagnosticsBuilder.RecordFailure(outcomes, CompilationStage.Build, ex);
            return SearchPlanResult.Failed(
                failure with { Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace: null) });
        }

        var resolved = await Resolve.RunAsync(context, _deps, cancellationToken);

        if (resolved.Unresolved.Count > 0)
        {
            if (traced)
            {
                CompilationDiagnosticsBuilder.MarkUnresolved(outcomes, resolved.Unresolved);
            }

            var resolveFailure = CompilationDiagnosticsBuilder.ResolveFailure(resolved.Unresolved)!;
            return SearchPlanResult.Failed(
                resolveFailure with { Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace: null) });
        }

        LoweredPlan lowered;
        try
        {
            lowered = Lower.Run(context, resolved.Symbols);
        }
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            var failure = CompilationDiagnosticsBuilder.RecordFailure(outcomes, CompilationStage.Lower, ex);
            return SearchPlanResult.Failed(
                failure with { Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace: null) });
        }

        QueryPlanTrace? planTrace = null;
        if (options.DiagnosticsLevel == SearchDiagnosticsLevel.Full)
        {
            planTrace = CompilationDiagnosticsBuilder.BuildPlanTrace(lowered, outcomes);
        }

        if (traced)
        {
            CompilationDiagnosticsBuilder.MarkKnownMisses(outcomes, lowered);
        }

        var plan = new SearchPlan
        {
            Query = lowered.Plan,
            ResourceType = context.TargetResourceType,
            DiagnosticsLevel = options.DiagnosticsLevel,
            Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace),
        };

        return SearchPlanResult.Success(plan);
    }

    private static SearchCompilationDiagnostics? Diagnostics(
        bool traced,
        IReadOnlyList<ParameterTrace> outcomes,
        IReadOnlyList<ImplicitParameter> implicitParameters,
        QueryPlanTrace? planTrace)
        => traced
            ? new SearchCompilationDiagnostics
            {
                Parameters = outcomes,
                Implicit = implicitParameters,
                PlanTrace = planTrace,
            }
            : null;
}
