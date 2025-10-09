// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Sparky.Domain.Models;
using Sparky.Application.Features.Patient;

namespace Sparky.Api.Features.Patient.Api;

[ApiController]
[Route("[controller]")]
public class PatientController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientController> _logger;

    public PatientController(
        IMediator mediator,
        ILogger<PatientController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// GET /Patient/{id}
    /// Retrieves a Patient resource by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GET /Patient/{Id}", id);

        var query = new GetPatientQuery(id);
        ResourceWrapper? result = await _mediator.SendAsync(query, cancellationToken);

        if (result == null)
        {
            return NotFound();
        }

        // Return raw JSON (stored by FileBasedFhirRepository for prototype simplicity)
        string json = result.RawJson ?? throw new InvalidOperationException("RawJson not available");

        return Content(json, "application/fhir+json");
    }

    /// <summary>
    /// PUT /Patient/{id}
    /// Creates or updates a Patient resource.
    /// </summary>
    [HttpPut("{id}")]
    [Consumes("application/fhir+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PUT /Patient/{Id}", id);

        // Read request body
        using var reader = new StreamReader(Request.Body);
        string json = await reader.ReadToEndAsync(cancellationToken);

        // Parse JSON to ISourceNode
        var sourceNode = await FhirJsonNode.ParseAsync(json);

        // Validate resource type matches
        if (!string.Equals(sourceNode.Name, "Patient", StringComparison.Ordinal))
        {
            return BadRequest(new { error = $"Resource type must be 'Patient', got '{sourceNode.Name}'" });
        }

        // Send command
        var command = new CreateOrUpdatePatientCommand(id, sourceNode);
        ResourceKey result = await _mediator.SendAsync(command, cancellationToken);

        // Determine if created or updated (version 1 = created)
        bool isCreated = result.VersionId == "1";

        if (isCreated)
        {
            return Created(new Uri($"/Patient/{result.Id}", UriKind.Relative), new
            {
                resourceType = "Patient",
                id = result.Id,
                meta = new
                {
                    versionId = result.VersionId
                }
            });
        }

        return Ok(new
        {
            resourceType = "Patient",
            id = result.Id,
            meta = new
            {
                versionId = result.VersionId
            }
        });
    }
}
