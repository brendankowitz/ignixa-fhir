// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using Sparky.Domain.Models;
using Sparky.Application.Features.Patient;
using Sparky.SourceNodeSerialization;
using Sparky.Search.Models;
using Sparky.Search.Parsing;
using Sparky.Api.Infrastructure;

namespace Sparky.Api.Features.Patient.Api;

[ApiController]
[Route("[controller]")]
public class PatientController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientController> _logger;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly IQueryParameterParser _queryParameterParser;
    private readonly ISearchOptionsBuilder _searchOptionsBuilder;

    public PatientController(
        IMediator mediator,
        ILogger<PatientController> logger,
        RecyclableMemoryStreamManager memoryStreamManager,
        IQueryParameterParser queryParameterParser,
        ISearchOptionsBuilder searchOptionsBuilder)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));
        _queryParameterParser = queryParameterParser ?? throw new ArgumentNullException(nameof(queryParameterParser));
        _searchOptionsBuilder = searchOptionsBuilder ?? throw new ArgumentNullException(nameof(searchOptionsBuilder));
    }

    /// <summary>
    /// GET /Patient
    /// Searches for Patient resources based on query parameters.
    /// Uses streaming serialization for memory-efficient response generation.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GET /Patient?{QueryString}", Request.QueryString);

        // Parse query parameters
        var queryParameters = _queryParameterParser.Parse(Request.Query);

        // Build SearchOptions
        var searchOptions = _searchOptionsBuilder.Build("Patient", queryParameters);

        // Send search query
        var searchQuery = new SearchPatientQuery(searchOptions);
        SearchPatientResult result = await _mediator.SendAsync(searchQuery, cancellationToken);

        // Build self link
        string selfLink = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";

        // Set response headers
        Response.ContentType = "application/fhir+json; charset=utf-8";

        // Stream Bundle response directly to HTTP response body
        await BundleSerializer.SerializeAsync(
            outputStream: Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources, // BundleSerializer accepts IEnumerable<ResourceWrapper>
            selfLink: selfLink,
            nextLink: null, // TODO: Implement pagination continuation
            pretty: false,
            cancellationToken: cancellationToken);

        return new EmptyResult();
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

        // Add ETag and Last-Modified headers
        Response.Headers.Append("ETag", $"W/\"{result.VersionId}\"");
        Response.Headers.Append("Last-Modified", result.LastModified.ToString("R")); // RFC 7231 format

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

        // Read request body using RecyclableMemoryStream
        string json;
        using (var memoryStream = _memoryStreamManager.GetStream("request-body"))
        {
            await Request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream, Encoding.UTF8);
            json = await reader.ReadToEndAsync(cancellationToken);
        }

        // Parse JSON to ISourceNode using JsonSourceNodeFactory (System.Text.Json, fast)
        var sourceNode = JsonSourceNodeFactory.Parse(json);

        // Validate resource type matches
        if (!string.Equals(sourceNode.Name, "Patient", StringComparison.Ordinal))
        {
            return BadRequest(new { error = $"Resource type must be 'Patient', got '{sourceNode.Name}'" });
        }

        // Send command (pass raw JSON for fast storage)
        var command = new CreateOrUpdatePatientCommand(id, sourceNode, json);
        ResourceKey result = await _mediator.SendAsync(command, cancellationToken);

        // Add ETag header (Last-Modified would require fetching the resource, skip for now)
        Response.Headers.Append("ETag", $"W/\"{result.VersionId}\"");

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
