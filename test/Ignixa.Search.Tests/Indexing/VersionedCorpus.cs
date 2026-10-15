// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// One FHIR version's production indexer, definitions and query builder, so a test indexes a resource and searches it
/// exactly as the in-memory and FileSystem backends do, and can read the index values the SQL row generators receive.
/// </summary>
internal sealed class VersionedCorpus
{
    private static readonly Dictionary<FhirVersion, VersionedCorpus> ByVersion = [];

    private readonly SearchOptionsBuilder _builder;

    private VersionedCorpus(FhirVersion version)
    {
        Schema = version.GetSchemaProvider();
        Definitions = new SearchParameterDefinitionManager(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);
        Indexer = SearchIndexerFactory.CreateInstance(Schema, NullLoggerFactory.Instance, Definitions, NullFhirBaseUriProvider.Instance);
        _builder = new SearchOptionsBuilder(
            new ExpressionParser(
                () => Definitions,
                new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema),
                Schema),
            Definitions);
    }

    public IFhirSchemaProvider Schema { get; }

    public SearchParameterDefinitionManager Definitions { get; }

    public ISearchIndexer Indexer { get; }

    public static VersionedCorpus For(FhirVersion version)
    {
        lock (ByVersion)
        {
            if (!ByVersion.TryGetValue(version, out VersionedCorpus? corpus))
            {
                corpus = new VersionedCorpus(version);
                ByVersion[version] = corpus;
            }

            return corpus;
        }
    }

    public IElement Parse(string json) => JsonSourceNodeFactory.Parse(json).ToElement(Schema);

    public (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) Index(string json)
    {
        IElement element = Parse(json);
        return (new ResourceKey(element.InstanceType, (string)element.FirstChild("id")!.Value!), Indexer.Extract(element));
    }

    /// <summary>The values indexed for <paramref name="code"/> on the resource, in extraction order.</summary>
    public IReadOnlyList<ISearchValue> Values(string json, string code) =>
        [.. Index(json).Index.Where(entry => entry.SearchParameter.Code == code).Select(entry => entry.Value)];

    /// <summary>Builds the options for <paramref name="query"/>, recording rather than failing on dropped parameters.</summary>
    public SearchOptions Options(string resourceType, string query) =>
        _builder.Build(resourceType, new QueryParameterParser().Parse(query), Schema);

    /// <summary>The ids of <paramref name="resources"/> the in-memory interpreter matches, failing if any parameter was dropped.</summary>
    public string[] Match(string resourceType, string query, params string[] resources)
    {
        SearchOptions options = Options(resourceType, query);
        options.UnsupportedParams.ShouldBeEmpty($"'{query}' was not fully parsed");
        var interpreter = new SearchQueryInterpreter();
        SearchPredicate predicate = options.Expression.AcceptVisitor(interpreter, interpreter.InitialContext);
        return [.. predicate([.. resources.Select(Index)]).Select(match => match.Location.Id)];
    }
}
