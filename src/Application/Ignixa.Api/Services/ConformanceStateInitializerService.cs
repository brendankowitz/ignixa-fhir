// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;

namespace Ignixa.Api.Services;

/// <summary>
/// Background service that initializes ConformanceState by replaying events from the event store on startup.
/// Uses the activation lock to ensure thread-safe initialization.
/// </summary>
public class ConformanceStateInitializerService(
    ISourceEventStore eventStore,
    ConformanceState conformanceState,
    ILogger<ConformanceStateInitializerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ConformanceStateInitializerService starting - replaying events from event store...");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await conformanceState.InitializeFromEventsAsync(eventStore, stoppingToken);

            stopwatch.Stop();

            var spCount = conformanceState.AllSearchParameters.Count;
            var sdCount = conformanceState.StructureDefinitions.Count;
            var pkgCount = conformanceState.Packages.Count;
            var lastEventId = conformanceState.LastProcessedEventId;

            logger.LogInformation(
                "ConformanceStateInitializerService completed in {ElapsedMs:N0}ms. " +
                "Last EventId: {LastEventId}. State: {SpCount} SearchParameters, {SdCount} StructureDefinitions, {PkgCount} Packages",
                stopwatch.ElapsedMilliseconds,
                lastEventId,
                spCount,
                sdCount,
                pkgCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("ConformanceStateInitializerService was cancelled during startup");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "ConformanceStateInitializerService failed during startup - conformance state is not initialized");
            throw;
        }
    }
}
