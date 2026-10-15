// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Adds, to what another indexer extracts, the <c>canonical</c> values of reference search parameters (R5 <c>_profile</c>
/// over <c>meta.profile</c>, <c>instantiates-canonical</c>, <c>QuestionnaireResponse-questionnaire</c>), as canonical
/// <see cref="UriSearchValue"/>s with url, version and fragment separated.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ElementSearchIndexer"/> indexes nothing for a reference parameter whose element is a canonical: the storage
/// for them (canonical in <c>UriSearchParam</c>, resolved by a value join) is the open #430 work, so no backend stores a
/// row for them. An in-memory index can still answer them, because <see cref="SearchQueryInterpreter"/> compares a
/// reference query value against these entries with canonical semantics: the url must be equal, and a version or
/// fragment in the query must be equal too, so <c>url</c> matches every version and <c>url|1.0</c> only that one. No
/// <c>Type/id</c> tail is parsed, so two profiles whose urls differ only after a resource-type-shaped segment stay distinct.
/// </para>
/// <para>
/// Following such a value is not supported: a chain, <c>_has</c> or include through the parameter throws
/// <see cref="SearchOperationNotSupportedException"/> rather than following nothing.
/// </para>
/// <para>
/// The parameters are found by typing each reference parameter's expression with the FHIRPath analyzer, once per resource
/// type (an expression it cannot type is not considered), and only elements that are canonicals at run time are indexed. Thread-safe. Experimental (<c>IGNIXAEXP001</c>).
/// </para>
/// </remarks>
[Experimental(InMemoryExperimental.DiagnosticId)]
public sealed class CanonicalReferenceSearchIndexer : ISearchIndexer
{
    private const string CanonicalType = "canonical";

    private readonly ISearchIndexer _inner;
    private readonly ISearchParameterDefinitionManager _definitions;
    private readonly FhirPathAnalyzer _analyzer;
    private readonly Lock _analyzerGate = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<SearchParameterInfo>> _parametersByType = new(StringComparer.Ordinal);
    private readonly CanonicalToUriSearchValueConverter _converter = new();

    /// <param name="inner">The indexer whose entries are extended.</param>
    /// <param name="definitions">The definitions the inner indexer and the query side use.</param>
    /// <param name="schema">The schema the expressions are typed against.</param>
    public CanonicalReferenceSearchIndexer(ISearchIndexer inner, ISearchParameterDefinitionManager definitions, IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(schema);

        _inner = inner;
        _definitions = definitions;
        _analyzer = new FhirPathAnalyzer(schema);
    }

    public IReadOnlyCollection<SearchIndexEntry> Extract(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        IReadOnlyCollection<SearchIndexEntry> entries = _inner.Extract(resource);
        IReadOnlyList<SearchParameterInfo> parameters = _parametersByType.GetOrAdd(resource.InstanceType, CanonicalReferenceParameters);
        if (parameters.Count == 0)
        {
            return entries;
        }

        var context = new FhirEvaluationContext { Resource = resource };
        List<SearchIndexEntry> canonicals = [.. parameters.SelectMany(parameter => resource
            .Select(parameter.Expression, context)
            .Where(element => element.InstanceType == CanonicalType)
            .SelectMany(_converter.ConvertTo)
            .Select(value => new SearchIndexEntry(parameter, value)))];

        return canonicals.Count == 0 ? entries : [.. entries, .. canonicals];
    }

    private IReadOnlyList<SearchParameterInfo> CanonicalReferenceParameters(string resourceType)
    {
        if (!_definitions.TryGetSearchParameters(resourceType, out IEnumerable<SearchParameterInfo>? parameters))
        {
            return [];
        }

        lock (_analyzerGate)
        {
            // Only an expression the analyzer types as possibly canonical is evaluated (one it cannot type is left to the
            // inner indexer), and of what it selects only the elements that are canonicals at run time are indexed.
            return [.. parameters.Where(parameter =>
                parameter.Type == SearchParamType.Reference
                && !string.IsNullOrWhiteSpace(parameter.Expression)
                && _analyzer.InferTypes(parameter.Expression, resourceType).Types.Any(type => type.TypeName == CanonicalType))];
        }
    }
}
