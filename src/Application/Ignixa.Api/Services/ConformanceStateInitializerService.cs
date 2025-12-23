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
        const int maxRetries = 3;
        var delays = new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) };

        logger.LogInformation("ConformanceStateInitializerService starting - replaying events from event store...");

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
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

                return; // Success - exit retry loop
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("ConformanceStateInitializerService was cancelled during startup");
                return;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                if (attempt < maxRetries)
                {
                    logger.LogWarning(
                        ex,
                        "ConformanceStateInitializerService initialization attempt {Attempt} of {MaxRetries} failed after {ElapsedMs:N0}ms. Retrying in {DelaySeconds}s...",
                        attempt + 1,
                        maxRetries + 1,
                        stopwatch.ElapsedMilliseconds,
                        delays[attempt].TotalSeconds);

                    try
                    {
                        await Task.Delay(delays[attempt], stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning("ConformanceStateInitializerService retry was cancelled");
                        return;
                    }
                }
                else
                {
                    logger.LogCritical(
                        ex,
                        "ConformanceStateInitializerService failed after {MaxRetries} attempts. Application will run in degraded mode without conformance state. " +
                        "ConformanceState.IsInitialized = false. Certain FHIR operations may not work correctly.",
                        maxRetries + 1);

                    // Don't throw - allow the application to continue in degraded mode
                    // ConformanceState.IsInitialized will remain false, which can be checked by dependent services
                }
            }
        }
    }
}
