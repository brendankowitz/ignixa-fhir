// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Converts package resource JSON to IType for use in composite schema provider.
/// Parses FHIR StructureDefinition JSON using internal infrastructure (no Firely SDK).
/// </summary>
public class PackageResourceProvider : IPackageResourceProvider
{
    private readonly ILogger<PackageResourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageResourceProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PackageResourceProvider(ILogger<PackageResourceProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Converts a package resource JSON to an IType using
    /// <see cref="StructureDefinitionTypeAdapter"/>.
    /// </summary>
    /// <param name="resourceJson">The FHIR StructureDefinition resource as JSON string.</param>
    /// <param name="fhirVersion">The FHIR version (e.g., "4.0.1", "4.3.0", "5.0.0").</param>
    /// <returns>The type definition if parsing succeeds, null otherwise.</returns>
    public IType? ToTypeDefinition(string resourceJson, string fhirVersion)
    {
        if (string.IsNullOrEmpty(resourceJson) || string.IsNullOrEmpty(fhirVersion))
        {
            return null;
        }

        try
        {
            return new StructureDefinitionTypeAdapter().Adapt(resourceJson, fhirVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Failed to adapt StructureDefinition (fhirVersion={FhirVersion}) - returning null", fhirVersion);
            return null;
        }
    }

}
