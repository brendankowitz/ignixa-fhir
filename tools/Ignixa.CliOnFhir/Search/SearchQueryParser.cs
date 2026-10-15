// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
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
    private static readonly ParameterCategory[] UnsupportedCategories =
    [
        ParameterCategory.Type,
        ParameterCategory.Formatting,
        ParameterCategory.ContinuationToken,
        ParameterCategory.IncludesCount,
        ParameterCategory.IncludesContinuationToken,
    ];

    private readonly IFhirSchemaProvider _schema;
    private readonly SearchOptionsBuilder _builder;
    private readonly QueryParameterParser _queryParameterParser = new();

    public SearchQueryParser(IFhirSchemaProvider schema, ISearchParameterDefinitionManager definitions)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(definitions);

        _schema = schema;

        // A local folder has no service base URI, so absolute references are kept verbatim, as the index builder does.
        var referenceParser = new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance);
        var expressionParser = new ExpressionParser(() => definitions, new SearchParameterExpressionParser(referenceParser, schema), schema);
        _builder = new SearchOptionsBuilder(expressionParser, definitions);
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
        RejectUnsupportedFeatures(parameters);

        List<IReadOnlySet<string>> idFilters = [];
        List<string> elementLists = [];
        List<QueryParameter> forBuilder = [];
        foreach (QueryParameter parameter in parameters)
        {
            switch (parameter.Name)
            {
                case "_id":
                    idFilters.Add(parameter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal));
                    break;
                case "_elements":
                    elementLists.Add(parameter.Value);
                    break;
                default:
                    forBuilder.Add(parameter);
                    break;
            }
        }

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
        return new ParsedSearch(options, idFilters, elements, requestedCount, countOnly, warnings);
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
            SearchOptions options = _builder.Build(resourceType, parameters, _schema, traces);

            // The builder only records an unsupported modifier; executing without this check would widen the search.
            SearchModifierNotSupportedException.ThrowIfAny(options);
            return options;
        }
        catch (FhirException ex)
        {
            throw SearchFailures.From(ex);
        }
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
    private static void RejectUnsupportedFeatures(List<QueryParameter> parameters)
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

            if (parameter.Name.StartsWith("_id:", StringComparison.Ordinal))
            {
                throw SearchFailures.NotSupported($"'{parameter.Name}': modifiers on _id are not supported; use '_id=a,b'.");
            }

            if (parameter.Name.Split(':')[0] == "_lastUpdated")
            {
                throw SearchFailures.NotSupported($"'{parameter.Name}': _lastUpdated is not supported because meta.lastUpdated is not indexed.");
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

    private static IEnumerable<IssueItem> IgnoredParameters(string resourceType, List<QueryParameter> parameters, SearchOptions options, List<ParameterTrace> traces)
    {
        foreach (ParameterTrace trace in traces.Where(trace => trace.Outcome is ParameterOutcome.Ignored))
        {
            var ignored = (ParameterOutcome.Ignored)trace.Outcome;
            yield return Ignored($"{trace.Key}={trace.Value}", ReadableReason(ignored.Reason, resourceType));
        }

        HashSet<string> traced = [.. traces.Select(trace => trace.Key)];
        foreach (QueryParameter parameter in parameters.Where(parameter => !traced.Contains(parameter.Name)))
        {
            if (options.UnsupportedParams.Contains(parameter.Name) || UnsupportedCategories.Contains(parameter.Category))
            {
                yield return Ignored($"{parameter.Name}={parameter.Value}", "this tool does not support it");
            }
            else if (parameter.Category == ParameterCategory.Summary && options.Summary is not (SummaryType.Count or SummaryType.False or SummaryType.None))
            {
                yield return Ignored($"{parameter.Name}={parameter.Value}", "only _summary=count is supported");
            }
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
