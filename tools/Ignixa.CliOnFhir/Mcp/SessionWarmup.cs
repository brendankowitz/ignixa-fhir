// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// Starts the work every first call needs while the client is still connecting and its model is still reading the menu:
/// the folder's catalog, then the patient compartments (loaded from the cache, else worked out from the indexes). Both are
/// the session's own lazy values, so a call that arrives first simply waits for the same work instead of repeating it, and a
/// failure here is the failure that call would have met: it is logged and left for the call to report.
/// </summary>
internal sealed class SessionWarmup(FolderSession session, ILogger<SessionWarmup> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(
        () =>
        {
            try
            {
                if (EnvelopeAssembler.FolderProblem(session) is null && !stoppingToken.IsCancellationRequested)
                {
                    _ = session.PatientCompartments;
                }
            }
            // Every exception: one escaping a BackgroundService stops the host, which would take the server down before any
            // call. Nothing is lost by catching it: the session's lazy values keep the failure and rethrow it to the first call.
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Warming the folder session failed; the first tool call will report the problem.");
            }
        },
        stoppingToken);
}
