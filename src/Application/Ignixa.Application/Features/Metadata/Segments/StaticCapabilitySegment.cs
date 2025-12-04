// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Static capability segment that never changes after startup.
/// Provides software information, basic settings, and supported formats.
/// </summary>
public class StaticCapabilitySegment(
    IApplicationVersionInfo versionInfo,
    ILogger<StaticCapabilitySegment> logger) : ICapabilitySegment
{
    private readonly IApplicationVersionInfo _versionInfo = versionInfo ?? throw new ArgumentNullException(nameof(versionInfo));
    private readonly ILogger<StaticCapabilitySegment> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string SegmentKey => "static";

    public int Priority => 10; // Execute first

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying static capability segment");

        // Basic metadata
        statement.Status = CapabilityStatementJsonNode.PublicationStatus.Active;
        statement.Experimental = false;
        statement.Publisher = "Ignixa Contributors";
        statement.Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance;

        // Supported formats
        statement.Format.Clear();
        statement.Format.Add("application/fhir+json");
        statement.PatchFormat.Clear();
        statement.PatchFormat.Add("application/json-patch+json");

        // Software component
        statement.Software = new SoftwareComponentJsonNode
        {
            Name = _versionInfo.Name,
            Version = _versionInfo.Version,
            ReleaseDate = _versionInfo.ReleaseDate,
        };

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Static segment: hash is just the software version
        // Changes only when software version changes (deployment)
        return ValueTask.FromResult(_versionInfo.Version);
    }
}
