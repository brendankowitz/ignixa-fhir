// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
// Converter scan and _profile entries copied from fhir-candle src/FhirCandle.Engine/Search/CandleSearchService.cs
// (CreateIndexer, Index) at 6d0af1e (MIT); adapted for cli-on-fhir.

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// The folder's search indexer: Ignixa's <see cref="ElementSearchIndexer"/> with three tool-local converter substitutions
/// (<see cref="SystemValueContactPointConverter"/>, <see cref="UnitAwareQuantityConverter"/>,
/// <see cref="UrnAwareReferenceConverter"/>), the in-memory
/// <c>_lastUpdated</c> entry from <see cref="LastUpdatedSearchIndexer"/>, and, where the version defines <c>_profile</c>
/// as a reference parameter (R5), <c>_profile</c> entries the stock indexer does not extract.
/// </summary>
/// <remarks>
/// The substitutions and <c>_profile</c> stay here rather than in <c>Ignixa.Search</c> because changing the shared
/// converters or indexer changes the rows the SQL backend stores; see the task report for the reindex implications.
/// Entry order (stock, then <c>_lastUpdated</c>, then <c>_profile</c>) matches what earlier versions persisted.
/// </remarks>
internal sealed class FolderSearchIndexer : ISearchIndexer
{
    private readonly ISearchIndexer _indexer;
    private readonly SearchParameterDefinitionManager _definitions;
    private readonly ReferenceSearchValueParser _referenceParser;

    /// <param name="loggerFactory">Creates the <see cref="ElementSearchIndexer"/>-category logger both the stock indexer and
    /// the <c>_lastUpdated</c> entry report extraction problems through.</param>
    public FolderSearchIndexer(
        IFhirSchemaProvider schema,
        ILoggerFactory loggerFactory,
        SearchParameterDefinitionManager definitions,
        ReferenceSearchValueParser referenceParser)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(referenceParser);

        _definitions = definitions;
        _referenceParser = referenceParser;
        ILogger<ElementSearchIndexer> logger = loggerFactory.CreateLogger<ElementSearchIndexer>();
        _indexer = new LastUpdatedSearchIndexer(CreateIndexer(schema, logger, definitions, referenceParser), definitions, logger);
    }

    /// <summary>
    /// Mirrors <see cref="SearchIndexerFactory.CreateInstance"/>'s reflection scan over Ignixa's converter types,
    /// substituting the ContactPoint, Quantity and Reference converters; the factory offers no converter-customization hook.
    /// </summary>
    private static ElementSearchIndexer CreateIndexer(
        IFhirSchemaProvider schema,
        ILogger<ElementSearchIndexer> logger,
        SearchParameterDefinitionManager definitions,
        ReferenceSearchValueParser referenceParser)
    {
        var elementResolver = new LightweightReferenceToElementResolver(referenceParser, schema);
        var codeSystems = new CodeSystemResolver(schema.Version);
        object[] wellKnownArguments = [schema, referenceParser, elementResolver, codeSystems, schema.Version];

        List<IElementToSearchValueConverter> converters = [.. typeof(ElementSearchIndexer)
            .Assembly
            .ExportedTypes
            .Where(x => typeof(IElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsGenericType)
            .Where(x => x != typeof(ContactPointToTokenSearchValueConverter) && x != typeof(QuantityToQuantitySearchValueConverter)
                && x != typeof(ResourceReferenceToReferenceSearchValueConverter))
            .Select(x => (IElementToSearchValueConverter)CreateWithArguments(x, wellKnownArguments))];

        converters.Add(new SystemValueContactPointConverter());
        converters.Add(new UnitAwareQuantityConverter());
        converters.Add(new UrnAwareReferenceConverter(referenceParser));

        return new ElementSearchIndexer(
            new SupportedSearchParameterDefinitionManager(definitions),
            new FhirElementToSearchValueConverterManager([.. converters]),
            elementResolver,
            logger);
    }

    private static object CreateWithArguments(Type type, object[] availableArguments)
    {
        ConstructorInfo constructor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();

        object[] arguments = [.. constructor.GetParameters()
            .Select(parameter => availableArguments.FirstOrDefault(a => parameter.ParameterType.IsAssignableFrom(a.GetType()))
                ?? throw new InvalidOperationException($"Unable to find a constructor argument of type {parameter.ParameterType} for {type}."))];

        return constructor.Invoke(arguments);
    }

    public IReadOnlyCollection<SearchIndexEntry> Extract(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        IReadOnlyCollection<SearchIndexEntry> entries = _indexer.Extract(resource);
        if (entries.Any(e => e.SearchParameter.Code == "_profile") ||
            !_definitions.TryGetSearchParameter(resource.InstanceType, "_profile", out SearchParameterInfo profileParameter) ||
            profileParameter.Type != SearchParamType.Reference)
        {
            return entries;
        }

        List<SearchIndexEntry> withProfiles = [.. entries];
        foreach (IElement profile in resource.Select("meta.profile"))
        {
            if (profile.Value is string profileUrl && !string.IsNullOrEmpty(profileUrl))
            {
                withProfiles.Add(new SearchIndexEntry(profileParameter, _referenceParser.Parse(profileUrl)));
            }
        }

        return withProfiles;
    }
}
