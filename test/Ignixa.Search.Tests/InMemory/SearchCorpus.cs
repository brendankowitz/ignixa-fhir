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

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// R4 resources indexed with Ignixa's own indexer and queries parsed with its own options builder, so the in-memory
/// search is exercised on exactly what production hands it.
/// </summary>
internal static class SearchCorpus
{
    public static IFhirSchemaProvider Schema { get; } = FhirVersion.R4.GetSchemaProvider();

    public static SearchParameterDefinitionManager Definitions { get; } =
        new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    public static ISearchIndexer Indexer { get; } =
        SearchIndexerFactory.CreateInstance(Schema, NullLoggerFactory.Instance, Definitions, NullFhirBaseUriProvider.Instance);

    private static SearchOptionsBuilder Builder { get; } = new(
        new ExpressionParser(
            () => Definitions,
            new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema),
            Schema),
        Definitions);

    /// <summary>Parses and indexes one resource.</summary>
    public static (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) Index(string json) =>
        Index(Indexer, json);

    /// <summary>Parses one resource and indexes it with <paramref name="indexer"/>.</summary>
    public static (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) Index(ISearchIndexer indexer, string json)
    {
        IElement element = JsonSourceNodeFactory.Parse(json).ToElement(Schema);
        var key = new ResourceKey(element.InstanceType, (string)element.FirstChild("id")!.Value!);
        return (key, indexer.Extract(element));
    }

    /// <summary>
    /// Builds the options for <paramref name="query"/> against <paramref name="resourceType"/>, failing when the builder
    /// dropped any parameter, so a test can never pass on a query that did not filter.
    /// </summary>
    public static SearchOptions Options(string resourceType, string query)
    {
        SearchOptions options = Builder.Build(resourceType, new QueryParameterParser().Parse(query), Schema);
        options.UnsupportedParams.ShouldBeEmpty($"'{query}' was not fully parsed");
        return options;
    }

    /// <summary>The ids of <paramref name="resources"/> that the default interpreter matches for <paramref name="query"/>, in input order.</summary>
    public static string[] Match(string resourceType, string query, params string[] resources) =>
        Match(resourceType, query, [.. resources.Select(Index)]);

    /// <summary>The ids of the indexed <paramref name="resources"/> that the default interpreter matches for <paramref name="query"/>, in input order.</summary>
    public static string[] Match(string resourceType, string query, IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> resources)
    {
        SearchOptions options = Options(resourceType, query);
        var interpreter = new SearchQueryInterpreter();
        SearchPredicate predicate = options.Expression.AcceptVisitor(interpreter, interpreter.InitialContext);
        return [.. predicate(resources).Select(match => match.Location.Id)];
    }
}
