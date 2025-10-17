// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Profile capability segment.
/// Populates supportedProfile for each resource type when Implementation Guides are loaded.
/// Changes when profiles are loaded/unloaded via $load-ig (Phase 11).
/// </summary>
/// <remarks>
/// Phase 3: Returns empty profiles (IG loading not yet implemented).
/// Phase 11: Will populate from IImplementationGuideProvider.
/// </remarks>
public class ProfileCapabilitySegment : ICapabilitySegment
{
    private readonly ILogger<ProfileCapabilitySegment> _logger;

    public ProfileCapabilitySegment(ILogger<ProfileCapabilitySegment> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SegmentKey => "profiles";

    public int Priority => 40; // Execute after search parameters

    public ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying profile capability segment for {FhirVersion}", context.FhirVersion);

        if (statement.Rest == null || statement.Rest.Count == 0)
        {
            _logger.LogWarning("No REST component found in capability statement - profiles will not be added");
            return ValueTask.CompletedTask;
        }

        var restComponent = statement.Rest[0];
        if (restComponent.Resource == null)
        {
            _logger.LogWarning("No resources found in REST component - profiles will not be added");
            return ValueTask.CompletedTask;
        }

        // Phase 3: No profiles loaded yet
        // Phase 11: Will query IImplementationGuideProvider for loaded profiles
        foreach (var resource in restComponent.Resource)
        {
            // Initialize empty list for future Phase 11 population
            resource.SupportedProfile = new List<ReferenceOrCanonicalJsonNode>();
        }

        _logger.LogDebug("Profile capability segment applied (Phase 3: no profiles loaded)");

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Phase 3: No profiles loaded, hash is constant
        // Phase 11: Hash will be SHA256 of all loaded profile canonical URLs
        const string emptyProfilesHash = "phase3-no-profiles";

        _logger.LogTrace(
            "Computed profile version hash for {FhirVersion}: {Hash}",
            context.FhirVersion,
            emptyProfilesHash);

        return ValueTask.FromResult(emptyProfilesHash);
    }
}
