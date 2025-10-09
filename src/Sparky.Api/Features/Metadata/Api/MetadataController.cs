// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Sparky.Api.Features.Metadata.Api;

[ApiController]
[Route("[controller]")]
public class MetadataController : ControllerBase
{
    private readonly ILogger<MetadataController> _logger;
    private static readonly Lazy<string> CapabilityStatement = new(() => LoadCapabilityStatement());

    public MetadataController(ILogger<MetadataController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// GET /metadata
    /// Returns the FHIR server's capability statement.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        _logger.LogInformation("GET /metadata");

        return Content(CapabilityStatement.Value, "application/fhir+json");
    }

    private static string LoadCapabilityStatement()
    {
        var assembly = typeof(MetadataController).Assembly;
        var resourceName = "Sparky.Api.Data.BaseCapabilities.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // Return a minimal capability statement if embedded resource not found
            return """
            {
              "resourceType": "CapabilityStatement",
              "status": "active",
              "date": "2025-10-09",
              "kind": "instance",
              "fhirVersion": "4.0.1",
              "format": ["application/fhir+json"],
              "rest": [{
                "mode": "server",
                "resource": [{
                  "type": "Patient",
                  "interaction": [
                    { "code": "read" },
                    { "code": "update" },
                    { "code": "create" }
                  ]
                }]
              }]
            }
            """;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
