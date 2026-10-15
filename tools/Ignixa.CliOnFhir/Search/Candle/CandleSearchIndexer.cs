// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
// Copied from fhir-candle src/FhirCandle.Engine/Search/CandleSearchService.cs (CreateIndexer, Index) at 6d0af1e (MIT);
// adapted for cli-on-fhir.

using System.Globalization;
using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Search.Candle;

/// <summary>
/// Ignixa's element search indexer with fhir-candle's converter substitutions (ContactPoint as
/// <c>system|value</c>, Quantity also indexed by unit) plus the synthetic <c>_lastUpdated</c> and (R5) <c>_profile</c>
/// entries, which the stock indexer never extracts. The query side (<see cref="CandleSearchQueryInterpreter"/>) relies on
/// all of these, so the index and the interpreter must come from the same place.
/// </summary>
public sealed class CandleSearchIndexer : ISearchIndexer
{
    private readonly ISearchIndexer _indexer;
    private readonly SearchParameterDefinitionManager _definitions;
    private readonly ReferenceSearchValueParser _referenceParser;

    public CandleSearchIndexer(
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
        _indexer = CreateIndexer(schema, loggerFactory, definitions, referenceParser);
    }

    /// <summary>
    /// Mirrors <see cref="SearchIndexerFactory.CreateInstance"/>'s reflection scan over Ignixa's converter types,
    /// substituting the ContactPoint and Quantity converters with candle versions; the factory offers no
    /// converter-customization hook.
    /// </summary>
    private static ElementSearchIndexer CreateIndexer(
        IFhirSchemaProvider schema,
        ILoggerFactory loggerFactory,
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
            .Where(x => x != typeof(ContactPointToTokenSearchValueConverter) && x != typeof(QuantityToQuantitySearchValueConverter))
            .Select(x => (IElementToSearchValueConverter)CreateWithArguments(x, wellKnownArguments))];

        converters.Add(new CandleContactPointToTokenSearchValueConverter());
        converters.Add(new CandleQuantityToQuantitySearchValueConverter());

        return new ElementSearchIndexer(
            new SupportedSearchParameterDefinitionManager(definitions),
            new FhirElementToSearchValueConverterManager([.. converters]),
            elementResolver,
            loggerFactory.CreateLogger<ElementSearchIndexer>());
    }

    private static object CreateWithArguments(Type type, object[] availableArguments)
    {
        ConstructorInfo constructor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();

        object[] arguments = [.. constructor.GetParameters()
            .Select(parameter => availableArguments.FirstOrDefault(a => parameter.ParameterType.IsAssignableFrom(a.GetType()))
                ?? throw new InvalidOperationException($"Unable to find a constructor argument of type {parameter.ParameterType} for {type}."))];

        return constructor.Invoke(arguments);
    }

    /// <summary>
    /// Extracts the FHIRPath-derived search index plus a synthetic <c>_lastUpdated</c> entry from
    /// <c>meta.lastUpdated</c> (Ignixa's indexer deliberately leaves it to its SQL layer, which reads the column directly)
    /// and, where the version defines <c>_profile</c> as a reference parameter (R5), synthetic <c>_profile</c> entries.
    /// </summary>
    public IReadOnlyCollection<SearchIndexEntry> Extract(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        List<SearchIndexEntry> entries = [.. _indexer.Extract(resource)];

        // Adapted: candle's elements hold an instant as a string; the folder model's schema-aware elements hold a FhirTemporal.
        string? lastUpdated = resource.FirstChild("meta")?.FirstChild("lastUpdated")?.Value switch
        {
            string text => text,
            FhirTemporal temporal => temporal.Literal,
            _ => null,
        };

        if (lastUpdated is not null &&
            DateTimeOffset.TryParse(lastUpdated, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedLastUpdated) &&
            _definitions.TryGetSearchParameter(resource.InstanceType, "_lastUpdated", out SearchParameterInfo lastUpdatedParameter))
        {
            entries.Add(new SearchIndexEntry(lastUpdatedParameter, new DateTimeSearchValue(parsedLastUpdated)));
        }

        if (!entries.Any(e => e.SearchParameter.Code == "_profile") &&
            _definitions.TryGetSearchParameter(resource.InstanceType, "_profile", out SearchParameterInfo profileParameter) &&
            profileParameter.Type == SearchParamType.Reference)
        {
            foreach (IElement profile in resource.Select("meta.profile"))
            {
                if (profile.Value is string profileUrl && !string.IsNullOrEmpty(profileUrl))
                {
                    entries.Add(new SearchIndexEntry(profileParameter, _referenceParser.Parse(profileUrl)));
                }
            }
        }

        return entries;
    }
}
