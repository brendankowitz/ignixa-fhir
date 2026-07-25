using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// Compiles a captured request URL through the same stages a server would: query-string parsing, the
/// real R4 search-parameter definitions, then Resolve/Lower/Emit via <see cref="SearchCompiler"/>.
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

        try
        {
            var trace = await SearchCompiler.CompileAsync(
                entry.ResourceType,
                parameters,
                OptionsBuilder,
                new CorpusSymbolResolver(),
                compartmentDefinitionManager: null,
                searchParameterDefinitionManager: Definitions,
                cancellationToken);

            if (trace.Sql is null)
            {
                var failure = trace.Failure;
                return CorpusCompilation.Failed(
                    entry,
                    failure is null ? "no-sql" : failure.Stage.ToString(),
                    failure?.Message ?? "compilation produced no SQL and recorded no failure");
            }

            return CorpusCompilation.Compiled(entry, trace.Sql.Sql);
        }
        catch (Exception exception) when (IsExpressibilityFailure(exception))
        {
            // SearchCompiler records Lower/Emit NotSupported and KeyNotFound as trace failures, but the
            // build stage ahead of it (search-parameter binding, value parsing) still throws. Those are
            // exactly the "the compiler cannot express this query" cases the report exists to surface.
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
}
