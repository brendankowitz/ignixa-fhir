// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search.Candle;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Parses FHIR search queries with Ignixa's own pipeline, wired as in
/// <c>src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs</c> (<c>CreateForTenant</c>).
/// Built once per session on the same search parameter definitions the index was extracted with, so parsing and
/// indexing agree. Safe to use concurrently.
/// </summary>
public sealed class SearchQueryParser
{
    private readonly IFhirSchemaProvider _schema;
    private readonly SearchOptionsBuilder _builder;
    private readonly ExpressionParser _expressionParser;
    private readonly QueryParameterParser _queryParameterParser = new();

    /// <param name="referenceParser">The parser the index was built with, so references are read the same way on both sides.</param>
    public SearchQueryParser(IFhirSchemaProvider schema, ISearchParameterDefinitionManager definitions, ReferenceSearchValueParser referenceParser)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(referenceParser);

        _schema = schema;

        // fhir-candle's parser overrides restore search semantics the stock parser diverges from (date ap, empty token
        // systems, composites); the interpreter in Candle/ evaluates what it produces.
        _expressionParser = new ExpressionParser(() => definitions, new CandleSearchParameterExpressionParser(referenceParser, schema), schema);
        _builder = new SearchOptionsBuilder(_expressionParser, definitions);
    }

    /// <summary>
    /// Parses <paramref name="request"/>. Every parameter that will not filter the results is returned as a
    /// <c>parameter-ignored</c> warning, so the caller knows which filters did not apply.
    /// </summary>
    /// <exception cref="ToolRequestException">The request is malformed or asks for a feature this tool does not support.</exception>
    public ParsedSearch Parse(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_schema.ResourceTypeNames.Contains(request.ResourceType))
        {
            throw ToolRequestException.Error("unknown-resource-type", $"'{request.ResourceType}' is not a {_schema.Version} resource type.");
        }

        List<IssueItem> warnings = [];
        List<QueryParameter> parameters = [.. _queryParameterParser.Parse(request.Query ?? string.Empty)];
        warnings.AddRange(DroppedByParser(request.Query));
        AddSummary(request.Summary, parameters);
        RejectUnsupportedFeatures(parameters, request.ResourceType);

        List<string> elementLists = [.. parameters.Where(parameter => parameter.Name == "_elements").Select(parameter => parameter.Value)];
        List<QueryParameter> forBuilder = [.. parameters.Where(parameter => parameter.Name != "_elements")];

        if (request.Elements is not null)
        {
            elementLists.Add(request.Elements);
        }

        ElementsProjection? elements = ElementsProjection.Parse(elementLists, _schema);

        List<ParameterTrace> traces = [];
        SearchOptions options = Build(request.ResourceType, forBuilder, traces);
        bool countOnly = options.Summary == SummaryType.Count;
        if (elements is not null && countOnly)
        {
            warnings.Add(new IssueItem("warning", "parameter-ignored", "'elements' was ignored because only the count was requested, so no resources are returned.", Location: null));
            elements = null;
        }

        warnings.AddRange(IgnoredParameters(request.ResourceType, forBuilder, options, traces));
        if (elements is not null)
        {
            warnings.AddRange(elements.Validate(request.ResourceType));
        }

        int? requestedCount = RequestedCount(forBuilder, options);
        return new ParsedSearch(options, elements, requestedCount, countOnly, traces, warnings);
    }

    private static int? RequestedCount(List<QueryParameter> parameters, SearchOptions options)
    {
        // The builder raises a count below 1 to 1, which would quietly answer a different question than the one asked.
        if (parameters.FirstOrDefault(parameter => parameter.Category == ParameterCategory.Count && int.TryParse(parameter.Value, System.Globalization.CultureInfo.InvariantCulture, out int value) && value < 1) is { } invalid)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'{invalid.Name}={invalid.Value}' must be at least 1. Use summary=count to get only the total.");
        }

        return parameters.Any(parameter => parameter.Category == ParameterCategory.Count) ? options.MaxItemCount : null;
    }

    private SearchOptions Build(string resourceType, List<QueryParameter> parameters, List<ParameterTrace> traces)
    {
        try
        {
            return BuildChecked(resourceType, parameters, traces);
        }
        catch (ToolRequestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The parsing pipeline throws arbitrary exception types for input it cannot handle (for example a
            // KeyNotFoundException for '_filter'), so name the parameter(s) that fail on their own.
            traces.Clear();
            throw NamedParseFailure(resourceType, parameters, ex);
        }
    }

    private SearchOptions BuildChecked(string resourceType, List<QueryParameter> parameters, List<ParameterTrace> traces)
    {
        try
        {
            SearchOptions options = _builder.Build(resourceType, parameters, _schema, traces);

            // The builder only records an unsupported modifier; executing without this check would widen the search.
            SearchModifierNotSupportedException.ThrowIfAny(options);
            return options;
        }
        catch (FhirException ex)
        {
            throw SearchFailures.From(ex);
        }
        catch (CandleMalformedSearchParameterException ex)
        {
            throw ToolRequestException.Error(SearchFailures.InvalidRequestCode, $"'{ex.ParameterCode}': {ex.Message}");
        }
    }

    private ToolRequestException NamedParseFailure(string resourceType, List<QueryParameter> parameters, Exception failure)
    {
        List<string> offenders = [];
        foreach (QueryParameter parameter in parameters)
        {
            try
            {
                _builder.Build(resourceType, [parameter], _schema);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                offenders.Add($"'{parameter.Name}={parameter.Value}' ({ex.GetType().Name})");
            }
        }

        string named = offenders.Count > 0
            ? string.Join(", ", offenders)
            : $"the query ({failure.GetType().Name}: {failure.Message})";
        return ToolRequestException.Error(SearchFailures.InvalidRequestCode, $"Could not parse {named}; check the parameter name, modifier and value syntax.");
    }

    private static void AddSummary(string? summary, List<QueryParameter> parameters)
    {
        if (summary is null)
        {
            return;
        }

        if (!string.Equals(summary, "count", StringComparison.OrdinalIgnoreCase))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'summary' must be 'count' or omitted, not '{summary}'.");
        }

        parameters.Add(new QueryParameter("_summary", "count"));
    }

    /// <summary>
    /// Rejects features the in-memory interpreter cannot honour. Silently dropping them would return results
    /// that look filtered, sorted or expanded when they are not.
    /// </summary>
    private static void RejectUnsupportedFeatures(List<QueryParameter> parameters, string resourceType)
    {
        foreach (QueryParameter parameter in parameters)
        {
            string? feature = parameter.Category switch
            {
                ParameterCategory.Sort => "sorting",
                ParameterCategory.Include => "_include",
                ParameterCategory.RevInclude => "_revinclude",
                _ => null,
            };
            if (feature is not null)
            {
                throw SearchFailures.NotSupported($"'{parameter.Name}': {feature} is not supported by this tool yet.");
            }

            if (ParameterPolicy.Rejection(parameter, resourceType) is { } reason)
            {
                throw SearchFailures.NotSupported($"'{parameter.Name}={parameter.Value}' is not supported by this tool: {reason}.");
            }
        }
    }

    /// <summary>Segments <see cref="QueryParameterParser"/> drops without a trace because they carry no value.</summary>
    private static IEnumerable<IssueItem> DroppedByParser(string? query)
    {
        foreach (string segment in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = segment.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0 || equals == segment.Length - 1 || equals == 0)
            {
                yield return new IssueItem("warning", "parameter-ignored", $"'{segment}' was ignored: a search parameter needs a name and a value ('name=value').", Location: null);
            }
        }
    }

    private IEnumerable<IssueItem> IgnoredParameters(string resourceType, List<QueryParameter> parameters, SearchOptions options, List<ParameterTrace> traces)
    {
        foreach (ParameterTrace trace in traces.Where(trace => trace.Outcome is ParameterOutcome.Ignored))
        {
            var ignored = (ParameterOutcome.Ignored)trace.Outcome;
            if (UnsupportedConstruct(resourceType, trace) is { } construct)
            {
                throw SearchFailures.NotSupported($"'{trace.Key}={trace.Value}' is not supported by this tool yet: {construct}");
            }

            yield return Ignored($"{trace.Key}={trace.Value}", ReadableReason(ignored.Reason, resourceType));
        }

        HashSet<string> traced = [.. traces.Select(trace => trace.Key)];
        foreach (QueryParameter parameter in parameters.Where(parameter => !traced.Contains(parameter.Name)))
        {
            // Anything that would change the results was rejected up front (see ParameterPolicy), so what is left is a result
            // parameter the tool does not need, the searched type named by _type, or a name nobody recognises.
            bool resultParameter = parameter.Category is ParameterCategory.Type or ParameterCategory.Formatting
                or ParameterCategory.IncludesCount or ParameterCategory.IncludesContinuationToken
                || ParameterPolicy.ResultParameters.Contains(ParameterPolicy.BaseName(parameter.Name));
            if (options.UnsupportedParams.Contains(parameter.Name) || resultParameter)
            {
                string reason = resultParameter
                    ? "it does not change which resources match, so this tool does not apply it"
                    : $"it is not a search parameter of {resourceType}";
                yield return Ignored($"{parameter.Name}={parameter.Value}", reason);
            }
            else if (parameter.Category == ParameterCategory.Summary && options.Summary is not (SummaryType.Count or SummaryType.False or SummaryType.None))
            {
                yield return Ignored($"{parameter.Name}={parameter.Value}", "only _summary=count is supported");
            }
        }
    }

    /// <summary>
    /// Why an ignored parameter is a construct the pipeline recognises but this tool cannot evaluate (a chain, <c>_has</c>, a
    /// prefix or modifier the parser rejects), or null when the name is simply not a search parameter. Ignoring the former
    /// would silently widen the search, so the caller fails the request instead; the latter stays a warning.
    /// </summary>
    private string? UnsupportedConstruct(string resourceType, ParameterTrace trace)
    {
        if (trace.Key.Contains('.', StringComparison.Ordinal) || trace.Key.StartsWith("_has", StringComparison.Ordinal))
        {
            return "chained and reverse-chained (_has) parameters need a later release";
        }

        try
        {
            _expressionParser.Parse([resourceType], trace.Key, trace.Value);
            return null;
        }
        catch (SearchParameterNotSupportedException)
        {
            return null;
        }
        catch (InvalidSearchOperationException ex)
        {
            return ex.Issues.FirstOrDefault()?.Diagnostics ?? ex.Message;
        }
        catch (FhirException)
        {
            return null;
        }
    }

    /// <summary>
    /// The pipeline records an unsupported parameter by its exception's message, which for an unknown parameter is the
    /// framework's generic "Exception of type ..." text because the detail lives in the exception's issues.
    /// </summary>
    private static string ReadableReason(string reason, string resourceType) =>
        reason.StartsWith("Exception of type ", StringComparison.Ordinal)
            ? $"it is not a search parameter of {resourceType}"
            : reason;

    private static IssueItem Ignored(string parameter, string reason) =>
        new("warning", "parameter-ignored", $"'{parameter}' did not filter the results: {reason}", Location: null);
}
