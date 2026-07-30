using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// Compiles a captured request URL through the same stages a server would: query-string parsing, the
/// real R4 search-parameter definitions, then Resolve/Lower/Emit via <see cref="SearchSqlCompiler"/>.
/// Nothing here is faked except the symbol ids (see <see cref="CorpusSymbolResolver"/>), so a failure
/// is a genuine statement about what the compiler can and cannot do with a real-world query.
/// </summary>
public static class CorpusCompiler
{
    private static readonly R4CoreSchemaProvider Schema = new();
    private static readonly QueryParameterParser QueryParser = new();
    private static readonly SearchParameterDefinitionManager Definitions =
        new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    private static readonly SearchOptionsBuilder OptionsBuilder = new(
        new ExpressionParser(() => Definitions, new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema), Schema),
        Definitions);

    // The real R4 compartment definitions -- the same source a server uses to expand a patient
    // compartment into its membership search parameters. Resolve needs this to lower a
    // PatientEverythingExpression; without it the operation cannot name its compartment members.
    private static readonly CompartmentDefinitionManager Compartments = new(FhirVersion.R4);

    public static async Task<CorpusCompilation> CompileAsync(CorpusEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        IReadOnlyList<QueryParameter> parameters;
        try
        {
            parameters = QueryParser.Parse(entry.QueryString);
        }
        catch (Exception exception) when (IsExpressibilityFailure(exception))
        {
            return CorpusCompilation.Failed(entry, "query-parse", exception.Message);
        }

        // A captured /Patient/{id}/$everything URL is a FHIR operation, not a query-string search. The
        // corpus previously dropped the $everything segment and compiled it as a bare GET /Patient?...,
        // so the operation was never exercised here. Rebuild the real PatientEverythingExpression and hand
        // it to the compiler as an operation override so Resolve/Lower run the honest compartment traversal.
        var operationExpression = TryBuildEverythingExpression(entry, parameters);
        var compartmentDefinitionManager = operationExpression is null ? null : Compartments;

        try
        {
            var compiler = new SearchSqlCompiler(
                new CorpusSymbolResolver(),
                OptionsBuilder,
                compartmentDefinitionManager: compartmentDefinitionManager,
                searchParameterDefinitionManager: Definitions);

            var result = await compiler.TryCreatePlanAsync(
                entry.ResourceType,
                parameters,
                new SearchPlanOptions
                {
                    OperationExpression = operationExpression,
                    DiagnosticsLevel = SearchDiagnosticsLevel.None,
                },
                cancellationToken);

            if (!result.Succeeded)
            {
                var planFailure = result.Failure!;
                return CorpusCompilation.Failed(entry, planFailure.Stage.ToString(), planFailure.Message);
            }

            var compiledResult = result.Plan.TryCompile();

            if (!compiledResult.Succeeded)
            {
                var emitFailure = compiledResult.Failure!;
                return CorpusCompilation.Failed(entry, emitFailure.Stage.ToString(), emitFailure.Message);
            }

            return CorpusCompilation.Compiled(entry, compiledResult.Compiled.Sql);
        }
        catch (Exception exception) when (IsExpressibilityFailure(exception))
        {
            // The facade handles Build (FhirException), Lower (NotSupportedException, KeyNotFoundException),
            // and Emit failures internally, returning them via SearchPlanResult or SearchCompilationResult.
            // This catch handles the residual cases that still propagate out: Resolve.RunAsync is not
            // wrapped inside the facade, so a NotSupportedException or KeyNotFoundException from the
            // resolve stage reaches here and is recorded as a corpus data point rather than crashing the run.
            return CorpusCompilation.Failed(entry, StageOf(exception), exception.Message);
        }
    }

    /// <summary>
    /// Whether an exception means "the compiler cannot express this query" (a corpus data point) rather
    /// than a defect in the compiler itself. A NullReferenceException, an IndexOutOfRangeException, or any
    /// other unexpected type is a real bug and must fail the test loudly instead of being recorded as a
    /// known expressiveness limit; OperationCanceledException must propagate so cooperative cancellation
    /// still works. Only the four families the build/lower/emit stages throw for genuinely-unsupported
    /// input are treated as data.
    /// </summary>
    private static bool IsExpressibilityFailure(Exception exception) => exception switch
    {
        OperationCanceledException => false,
        NotSupportedException or KeyNotFoundException or FhirException => true,
        _ => false,
    };

    private static string StageOf(Exception exception) => exception switch
    {
        NotSupportedException => "not-supported",
        KeyNotFoundException => "unresolved-symbol",
        _ => "build:" + exception.GetType().Name,
    };

    /// <summary>
    /// Rebuilds the <see cref="PatientEverythingExpression"/> a captured <c>/Patient/{id}/$everything</c>
    /// URL stands for, or null when the entry is an ordinary search. The patient id comes from the path;
    /// <c>_since</c> and <c>_type</c> come from the already-parsed query parameters. A <c>_since</c> value
    /// that is not a parseable instant is left unset rather than failing -- the point is to exercise the
    /// operation's compartment traversal, and an unparseable bound would only be dropped downstream anyway.
    /// </summary>
    private static PatientEverythingExpression? TryBuildEverythingExpression(
        CorpusEntry entry,
        IReadOnlyList<QueryParameter> parameters)
    {
        var path = entry.Url;
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3
            || !segments[0].Equals("Patient", StringComparison.Ordinal)
            || !segments[2].Equals("$everything", StringComparison.Ordinal))
        {
            return null;
        }

        var patientId = segments[1];

        DateTimeOffset? sinceDate = null;
        HashSet<string>? filteredResourceTypes = null;

        foreach (var parameter in parameters)
        {
            if (parameter.Name.Equals("_since", StringComparison.Ordinal)
                && DateTimeOffset.TryParse(
                    parameter.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var since))
            {
                sinceDate = since;
            }
            else if (parameter.Name.Equals("_type", StringComparison.Ordinal))
            {
                filteredResourceTypes ??= new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in parameter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    filteredResourceTypes.Add(type);
                }
            }
        }

        return new PatientEverythingExpression(
            patientId,
            sinceDate: sinceDate,
            filteredResourceTypes: filteredResourceTypes);
    }
}
