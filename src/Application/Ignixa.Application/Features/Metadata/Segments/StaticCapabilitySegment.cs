// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Static capability segment that never changes after startup.
/// Provides software information, basic settings, and supported formats.
/// </summary>
public class StaticCapabilitySegment : ICapabilitySegment
{
    private readonly ILogger<StaticCapabilitySegment> _logger;

    // Software version - could be injected from ProductVersionInfo in future
    private const string SoftwareVersion = "0.1.0";
    private const string SoftwareName = "Ignixa FHIR Server";
    private const string ReleaseDate = "2025-10-16";

    public StaticCapabilitySegment(ILogger<StaticCapabilitySegment> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
        statement.SetFormats(new List<string> { "application/fhir+json" });
        statement.SetPatchFormats(new List<string> { "application/json-patch+json" });

        // Software component
        statement.Software = new SoftwareComponentJsonNode
        {
            Name = SoftwareName,
            Version = SoftwareVersion,
            ReleaseDate = ReleaseDate,
        };

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Static segment: hash is just the software version
        // Changes only when software version changes (deployment)
        return ValueTask.FromResult(SoftwareVersion);
    }
}
