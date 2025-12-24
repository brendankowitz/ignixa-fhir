// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text.Json;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.EventStore;

public class SqlSourceEventStore(
    IDbContextFactory<FhirDbContext> contextFactory,
    ILogger<SqlSourceEventStore> logger) : ISourceEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var eventsList = events.ToList();
        var entities = new List<SourceEventEntity>(eventsList.Count);
        var timestamp = DateTimeOffset.UtcNow;

        // Capture current max TransactionId for deterministic reindex cutoff
        // Resources with TransactionId <= this value existed before these events
        var currentTransactionId = await context.Transactions
            .Where(t => t.IsVisible)
            .MaxAsync(t => (long?)t.SurrogateIdRangeFirstValue, cancellationToken) ?? 0L;

        foreach (var evt in eventsList)
        {
            var entity = new SourceEventEntity
            {
                StreamId = evt.StreamId,
                EventType = evt.EventType,
                EventData = JsonSerializer.Serialize(evt.Data, JsonOptions),
                Timestamp = timestamp,
                TransactionId = currentTransactionId
            };

            context.SourceEvents.Add(entity);
            entities.Add(entity);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Appended {Count} events to event store (TransactionId cutoff: {TransactionId})",
            eventsList.Count,
            currentTransactionId);

        return entities.Select((e, i) => new SourceEvent(
            e.EventId,
            e.StreamId,
            e.EventType,
            eventsList[i].Data,
            e.Timestamp)).ToList();
    }

    public async IAsyncEnumerable<SourceEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        
        await foreach (var entity in context.SourceEvents
            .OrderBy(e => e.EventId)
            .AsNoTracking()
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return DeserializeEvent(entity);
        }
    }

    public async IAsyncEnumerable<SourceEvent> ReadFromAsync(
        long afterEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await foreach (var entity in context.SourceEvents
            .Where(e => e.EventId > afterEventId)
            .OrderBy(e => e.EventId)
            .AsNoTracking()
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return DeserializeEvent(entity);
        }
    }

    public async IAsyncEnumerable<SourceEvent> ReadStreamAsync(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        
        await foreach (var entity in context.SourceEvents
            .Where(e => e.StreamId == streamId)
            .OrderBy(e => e.EventId)
            .AsNoTracking()
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return DeserializeEvent(entity);
        }
    }

    private SourceEvent DeserializeEvent(SourceEventEntity entity)
    {
        var dataType = GetEventDataType(entity.EventType);

        try
        {
            var data = JsonSerializer.Deserialize(entity.EventData, dataType, JsonOptions)
                ?? throw new InvalidOperationException($"Deserialization returned null for EventId {entity.EventId}");

            return new SourceEvent(entity.EventId, entity.StreamId, entity.EventType, data, entity.Timestamp, entity.TransactionId);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize event {EventId} of type {EventType}", entity.EventId, entity.EventType);
            throw new InvalidOperationException(
                $"Failed to deserialize event {entity.EventId} of type '{entity.EventType}'. " +
                $"This may indicate database corruption or version mismatch.", ex);
        }
    }

    private static Type GetEventDataType(string eventType) => eventType switch
    {
        nameof(PackageUploaded) => typeof(PackageUploaded),
        nameof(PackageActivated) => typeof(PackageActivated),
        nameof(PackageDeactivated) => typeof(PackageDeactivated),
        nameof(SearchParameterActivated) => typeof(SearchParameterActivated),
        nameof(SearchParameterReindexStarted) => typeof(SearchParameterReindexStarted),
        nameof(SearchParameterReindexCompleted) => typeof(SearchParameterReindexCompleted),
        nameof(SearchParameterReindexFailed) => typeof(SearchParameterReindexFailed),
        nameof(SearchParameterDeactivated) => typeof(SearchParameterDeactivated),
        nameof(SearchParameterDeleted) => typeof(SearchParameterDeleted),
        nameof(StructureDefinitionActivated) => typeof(StructureDefinitionActivated),
        nameof(StructureDefinitionDeactivated) => typeof(StructureDefinitionDeactivated),
        _ => throw new InvalidOperationException(
            $"Unknown event type '{eventType}'. This may indicate database corruption or version mismatch. " +
            $"Valid types: PackageUploaded, PackageActivated, PackageDeactivated, SearchParameterActivated, " +
            $"SearchParameterReindexStarted, SearchParameterReindexCompleted, SearchParameterReindexFailed, " +
            $"SearchParameterDeactivated, SearchParameterDeleted, StructureDefinitionActivated, StructureDefinitionDeactivated")
    };
}
