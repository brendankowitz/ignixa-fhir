// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

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
/// Following such a value is not supported. A chain, <c>_has</c> or include through a parameter that selects only
/// canonicals is refused with <see cref="SearchOperationNotSupportedException"/> when the query is compiled, whatever the
/// data; one through a parameter that also selects references follows the references and skips the canonicals.
/// </para>
/// <para>
/// The parameters are the shipped ones <c>KnownCanonicalReferenceParameters</c> lists, a census-checked static table (so no
/// expression is typed at run time), and only elements that are canonicals at run time are indexed. A parameter registered
/// from a package is not listed, so its canonicals are not indexed. Thread-safe. Experimental (<c>IGNIXAEXP001</c>).
/// </para>
/// </remarks>
[Experimental(InMemoryExperimental.DiagnosticId)]
public sealed class CanonicalReferenceSearchIndexer : ISearchIndexer
{
    private const string CanonicalType = "canonical";

    private readonly ISearchIndexer _inner;
    private readonly ISearchParameterDefinitionManager _definitions;
    private readonly ConcurrentDictionary<string, IReadOnlyList<SearchParameterInfo>> _parametersByType = new(StringComparer.Ordinal);
    private readonly CanonicalToUriSearchValueConverter _converter = new();

    /// <param name="inner">The indexer whose entries are extended.</param>
    /// <param name="definitions">The definitions the inner indexer and the query side use.</param>
    public CanonicalReferenceSearchIndexer(ISearchIndexer inner, ISearchParameterDefinitionManager definitions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(definitions);

        _inner = inner;
        _definitions = definitions;
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

    private IReadOnlyList<SearchParameterInfo> CanonicalReferenceParameters(string resourceType) =>
        _definitions.TryGetSearchParameters(resourceType, out IEnumerable<SearchParameterInfo>? parameters)
            ? [.. parameters.Where(parameter => parameter.CanonicalReferences != CanonicalReferenceKind.None && !string.IsNullOrWhiteSpace(parameter.Expression))]
            : [];
}
