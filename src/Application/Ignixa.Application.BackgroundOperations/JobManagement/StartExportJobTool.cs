// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Medino;
using ModelContextProtocol.Server;

namespace Ignixa.Application.BackgroundOperations.JobManagement;

/// <summary>
/// MCP tool for starting FHIR bulk export jobs.
/// Exports resources to NDJSON files in blob storage.
/// </summary>
[McpServerToolType]
public class StartExportJobTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;

    public StartExportJobTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [McpServerTool(Name = "start_export_job")]
    [Description(@"Start a FHIR bulk export job to NDJSON files.
Exports all data or filtered by resource types and date range.
Returns job ID for tracking progress via get_job_status tool.
Example: resourceTypes=['Patient', 'Observation'], since='2024-01-01T00:00:00Z'")]
    public async Task<JobSummaryDto> StartExportJobAsync(
        [Description("Resource types to export (e.g., ['Patient', 'Observation']). Leave empty to export all types.")]
        IReadOnlyList<string>? resourceTypes = null,

        [Description("Export only resources modified since this date (ISO 8601 format). Leave empty for all resources.")]
        DateTimeOffset? since = null,

        [Description("Type-specific filters as dictionary (e.g., {'Observation': 'code=http://loinc.org|85354-9'}). Advanced use only.")]
        Dictionary<string, string>? typeFilters = null,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Normalize inputs
        var types = resourceTypes?.ToArray() ?? Array.Empty<string>();
        var filters = typeFilters ?? new Dictionary<string, string>();

        // Create export job via handler
        var command = new CreateExportJobCommand
        {
            TenantId = resolvedTenantId,
            ResourceTypes = types,
            Since = since,
            TypeFilters = filters,
            OutputFormat = ExportConstants.MediaTypeNdjson
        };

        var result = await _mediator.SendAsync(command, cancellationToken);

        return new JobSummaryDto
        {
            JobId = result.JobId,
            JobType = "Export",
            Status = result.Status,
            ProgressPercentage = null,
            ProgressDescription = types.Any()
                ? $"Exporting {string.Join(", ", types)} resources"
                : "Exporting all resource types",
            CreateDate = result.CreateDate,
            StartDate = null,
            EndDate = null,
            ErrorMessage = null
        };
    }
}
