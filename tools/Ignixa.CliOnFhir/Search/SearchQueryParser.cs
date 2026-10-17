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
    /// <summary>The issue code for a parameter name the pipeline does not recognise, unless the search is lenient.</summary>
    public const string UnknownParameterCode = "unknown-search-parameter";

    /// <summary>Ends every <see cref="UnknownParameterCode"/> diagnostic.</summary>
    public const string LenientHint = " Pass lenient to ignore unknown parameters instead.";

    private readonly IFhirSchemaProvider _schema;
    private readonly ISearchParameterDefinitionManager _definitions;
    private readonly SearchOptionsBuilder _builder;
    private readonly ExpressionParser _expressionParser;
    private readonly ChainKeyDiagnosis _chains;
    private readonly QueryParameterParser _queryParameterParser = new();

    /// <param name="referenceParser">The parser the index was built with, so references are read the same way on both sides.</param>
    public SearchQueryParser(IFhirSchemaProvider schema, ISearchParameterDefinitionManager definitions, ReferenceSearchValueParser referenceParser)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(referenceParser);

        _schema = schema;
        _definitions = definitions;
        _expressionParser = new ExpressionParser(() => definitions, new SearchParameterExpressionParser(referenceParser, schema), schema);
        _builder = new SearchOptionsBuilder(_expressionParser, definitions);
        _chains = new ChainKeyDiagnosis(schema, definitions, KnownNames);
    }

    /// <summary>
    /// Parses <paramref name="request"/>. A parameter name the pipeline does not recognise fails the request as
    /// <c>unknown-search-parameter</c>, unless <see cref="SearchRequest.Lenient"/> is set, when it is a
    /// <c>parameter-ignored</c> warning instead; result parameters the tool does not apply are always such warnings.
    /// </summary>
    /// <exception cref="ToolRequestException">The request is malformed or asks for a feature this tool does not support.</exception>
    public ParsedSearch Parse(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_schema.ResourceTypeNames.Contains(request.ResourceType))
        {
            throw ToolRequestException.Error("unknown-resource-type", $"'{QueryEcho.Value(request.ResourceType)}' is not a {_schema.Version} resource type.");
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

        warnings.AddRange(IgnoredParameters(request.ResourceType, forBuilder, options, traces, request.Lenient));
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

    /// <remarks>
    /// Only the pipeline's request faults (<see cref="FhirException"/>) are reported as the caller's; anything else is a
    /// defect and propagates unchanged rather than being relabelled as unparseable input.
    /// </remarks>
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
            // The modifier check names its parameters already; other request faults carry only the value-level message.
            throw SearchFailures.From(ex, ex is SearchModifierNotSupportedException ? null : FailingAlone(resourceType, parameters));
        }
    }

    /// <summary>The parameters that fail to build on their own, quoted and comma-separated, or null when none does.</summary>
    private string? FailingAlone(string resourceType, List<QueryParameter> parameters)
    {
        List<string> failing = [];
        foreach (QueryParameter parameter in parameters)
        {
            try
            {
                SearchModifierNotSupportedException.ThrowIfAny(_builder.Build(resourceType, [parameter], _schema));
            }
            catch (FhirException)
            {
                failing.Add($"'{parameter.Name}={parameter.Value}'");
            }
        }

        return failing.Count > 0 ? string.Join(", ", failing) : null;
    }

    private static void AddSummary(string? summary, List<QueryParameter> parameters)
    {
        if (summary is null)
        {
            return;
        }

        if (!string.Equals(summary, "count", StringComparison.OrdinalIgnoreCase))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'summary' must be 'count' or omitted, not '{QueryEcho.Value(summary)}'.");
        }

        parameters.Add(new QueryParameter("_summary", "count"));
    }

    /// <summary>
    /// Rejects standard parameters the tool does not evaluate (see <see cref="ParameterPolicy"/>). Silently dropping them
    /// would return results that look filtered when they are not.
    /// </summary>
    private static void RejectUnsupportedFeatures(List<QueryParameter> parameters, string resourceType)
    {
        foreach (QueryParameter parameter in parameters)
        {
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
                yield return new IssueItem("warning", "parameter-ignored", $"'{QueryEcho.Value(segment)}' was ignored: a search parameter needs a name and a value ('name=value').", Location: null);
            }
        }
    }

    private IEnumerable<IssueItem> IgnoredParameters(string resourceType, List<QueryParameter> parameters, SearchOptions options, List<ParameterTrace> traces, bool lenient)
    {
        // A sort key the builder cannot use is dropped from the order, which changes which page is returned.
        if (options.UnsupportedParams.FirstOrDefault(name => name.StartsWith("_sort=", StringComparison.Ordinal)) is { } sort)
        {
            throw SearchFailures.NotSupported($"'{QueryEcho.Value(sort)}' cannot be sorted by: it is not a sortable (date, number, quantity, string, token or uri) search parameter of {resourceType}.");
        }

        List<IssueItem> ignored = [];
        foreach (ParameterTrace trace in traces.Where(trace => trace.Outcome is ParameterOutcome.Ignored))
        {
            string? unknown = Classify(resourceType, trace, lenient);
            ignored.Add(Unknown(resourceType, trace.Key, trace.Value, unknown, lenient));
        }

        HashSet<string> traced = [.. traces.Select(trace => trace.Key)];
        foreach (QueryParameter parameter in parameters.Where(parameter => !traced.Contains(parameter.Name)))
        {
            // Anything that would change the results was rejected up front (see ParameterPolicy), so what is left is a result
            // parameter the tool does not need, the searched type named by _type, or a name nobody recognises.
            bool resultParameter = parameter.Category is ParameterCategory.Type or ParameterCategory.Formatting
                or ParameterCategory.IncludesCount or ParameterCategory.IncludesContinuationToken
                || ParameterPolicy.ResultParameters.Contains(ParameterPolicy.BaseName(parameter.Name));
            if (resultParameter)
            {
                ignored.Add(Ignored($"{parameter.Name}={parameter.Value}", "it does not change which resources match, so this tool does not apply it"));
            }
            else if (options.UnsupportedParams.Contains(parameter.Name))
            {
                ignored.Add(Unknown(resourceType, parameter.Name, parameter.Value, detail: null, lenient));
            }
            else if (parameter.Category == ParameterCategory.Summary && options.Summary is not (SummaryType.Count or SummaryType.False or SummaryType.None))
            {
                ignored.Add(Ignored($"{parameter.Name}={parameter.Value}", "only _summary=count is supported"));
            }
        }

        return ignored;
    }

    /// <summary>
    /// A parameter the pipeline did not apply because its name is not recognised: a <c>parameter-ignored</c> warning in
    /// lenient mode, otherwise an <c>unknown-search-parameter</c> failure with a hint. A name the type does define (such
    /// as <c>_source</c>) but the pipeline does not evaluate always fails as <c>search-not-supported</c>.
    /// </summary>
    /// <param name="detail">The pipeline's own account, for a name inside a chain; null for a top-level name.</param>
    private IssueItem Unknown(string resourceType, string name, string value, string? detail, bool lenient)
    {
        string baseName = ParameterPolicy.BaseName(name);
        IReadOnlyList<string> codes = KnownNames(resourceType);
        if (detail is null && codes.Contains(baseName, StringComparer.Ordinal))
        {
            throw SearchFailures.NotSupported($"'{name}={value}' is not supported by this tool: {baseName} is a search parameter of {resourceType} that the search pipeline does not evaluate.");
        }

        string reason = detail?.TrimEnd('.') ?? $"'{QueryEcho.Value(baseName)}' is not a search parameter of {resourceType}";
        if (lenient)
        {
            return Ignored($"{name}={value}", $"{reason} (ignored because the search is lenient, so the results may be broader than asked)");
        }

        string hint = detail is null ? ParameterSuggestions.For(baseName, resourceType, codes) : "Check each parameter name in the chain.";
        throw ToolRequestException.Error(UnknownParameterCode, $"'{name}={value}': {reason}. {hint}{LenientHint}");
    }

    /// <summary>Every parameter name a search of <paramref name="resourceType"/> accepts: its search parameter codes and the result parameters.</summary>
    private IReadOnlyList<string> KnownNames(string resourceType)
    {
        IEnumerable<string> codes = _definitions.TryGetSearchParameters(resourceType, out IEnumerable<SearchParameterInfo> parameters)
            ? parameters.Select(parameter => parameter.Code)
            : [];
        return [.. codes.Concat(ParameterPolicy.ControlParameters).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why the pipeline ignored <paramref name="trace"/>: null when its name is simply not a search parameter of the type,
    /// an account of the link at fault when a name inside a chain is not (which lenient mode may ignore), and a failure
    /// when the key can never bind (a link of the wrong type, an impossible target) or is a construct the pipeline
    /// recognises but cannot evaluate (an ambiguous chain, a prefix or modifier the parser rejects), since ignoring that
    /// would silently widen the search.
    /// </summary>
    private string? Classify(string resourceType, ParameterTrace trace, bool lenient)
    {
        bool chained = trace.Key.Contains('.', StringComparison.Ordinal) || trace.Key.StartsWith("_has:", StringComparison.Ordinal);
        try
        {
            _expressionParser.Parse([resourceType], trace.Key, trace.Value);
            return null;
        }
        catch (FhirException ex) when (chained && ex is SearchParameterNotSupportedException or InvalidSearchOperationException
            && _chains.Diagnose(resourceType, trace.Key, trace.Value) is { } diagnosis)
        {
            if (lenient && diagnosis.Issue.Code == UnknownParameterCode)
            {
                return diagnosis.Issue.Diagnostics!.Replace(LenientHint, string.Empty, StringComparison.Ordinal);
            }

            throw diagnosis;
        }
        catch (SearchParameterNotSupportedException ex)
        {
            return chained ? ex.Issues.FirstOrDefault()?.Diagnostics ?? ex.Message : null;
        }
        catch (InvalidSearchOperationException ex)
        {
            throw SearchFailures.NotSupported($"'{trace.Key}={trace.Value}' is not supported by this tool: {ex.Issues.FirstOrDefault()?.Diagnostics ?? ex.Message}");
        }
        catch (FhirException)
        {
            return null;
        }
    }

    private static IssueItem Ignored(string parameter, string reason) =>
        new("warning", "parameter-ignored", $"'{QueryEcho.Value(parameter)}' did not filter the results: {reason}", Location: null);
}
