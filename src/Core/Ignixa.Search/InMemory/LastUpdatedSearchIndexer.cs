// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Adds a <c>_lastUpdated</c> entry, read from <c>meta.lastUpdated</c>, to what another indexer extracts.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ElementSearchIndexer"/> emits no entry for intrinsic parameters because a database backend reads
/// them from its own columns. An in-memory index has no such column, so a caller searching it by
/// <c>_lastUpdated</c> wraps its indexer in this one; the entries a database stores are unaffected.
/// </para>
/// <para>
/// Only a FHIR instant (a date, a time to at least the second, and a time zone) is indexed. A value without a
/// zone would otherwise be read in the machine's local zone, so the same data would index differently from one
/// computer to the next; such a value, a partial date, or text is logged as a warning and the resource is left
/// unindexed for <c>_lastUpdated</c> rather than guessed at.
/// </para>
/// </remarks>
public sealed partial class LastUpdatedSearchIndexer : ISearchIndexer
{
    private readonly ISearchIndexer _inner;
    private readonly ISearchParameterDefinitionManager _definitions;
    private readonly ILogger _logger;

    /// <param name="inner">The indexer whose entries are extended.</param>
    /// <param name="definitions">Supplies the <c>_lastUpdated</c> definition the entry refers to; use the same manager the query side parses with.</param>
    /// <param name="logger">Receives a warning for each <c>meta.lastUpdated</c> that is not a FHIR instant.</param>
    public LastUpdatedSearchIndexer(ISearchIndexer inner, ISearchParameterDefinitionManager definitions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _definitions = definitions;
        _logger = logger;
    }

    public IReadOnlyCollection<SearchIndexEntry> Extract(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        IReadOnlyCollection<SearchIndexEntry> entries = _inner.Extract(resource);
        if (resource.FirstChild("meta")?.FirstChild("lastUpdated") is not { } lastUpdated ||
            !_definitions.TryGetSearchParameter(resource.InstanceType, SearchParameterNames.LastUpdated, out SearchParameterInfo parameter))
        {
            return entries;
        }

        if (!TryParseInstant(lastUpdated.Value, out DateTimeSearchValue? instant))
        {
            LogNotAnInstant(_logger, lastUpdated.Value, resource.InstanceType, resource.FirstChild("id")?.Value);
            return entries;
        }

        return [.. entries, new SearchIndexEntry(parameter, instant)];
    }

    private static bool TryParseInstant(object? value, [NotNullWhen(true)] out DateTimeSearchValue? instant)
    {
        instant = null;
        if (value is not FhirTemporal { HasTimezone: true } temporal || temporal.Precision < FhirTemporalPrecision.Second)
        {
            return false;
        }

        try
        {
            instant = DateTimeSearchValue.Parse(temporal.Literal);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "meta.lastUpdated '{Value}' of {ResourceType}/{Id} is not a FHIR instant (a date and time with a timezone), so the resource is not indexed for _lastUpdated.")]
    private static partial void LogNotAnInstant(ILogger logger, object? value, string resourceType, object? id);
}
