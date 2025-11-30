// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.Application.BackgroundOperations.Import;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace Ignixa.Application.BackgroundOperations.JobManagement;

/// <summary>
/// MCP tool for starting FHIR bulk import jobs.
/// Supports NDJSON file import with configurable batch processing.
/// </summary>
[McpServerToolType]
public class StartImportJobTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public StartImportJobTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator,
        IConfiguration configuration)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    [McpServerTool(Name = "start_import_job")]
    [Description(@"Start a FHIR bulk import job from NDJSON files.
Supports both InitialLoad (clean slate) and IncrementalLoad (merge) modes.
Returns job ID for tracking progress via get_job_status tool.
Example: inputFiles=[{type='Patient', url='https://...'}], mode='IncrementalLoad'")]
    public async Task<JobSummaryDto> StartImportJobAsync(
        [Description("Input files to import. Array of {type, url, etag} objects. Type is the FHIR resource type.")]
        IReadOnlyList<InputFileInfo> inputFiles,

        [Description("Import mode: 'InitialLoad' (replace all data) or 'IncrementalLoad' (merge/update)")]
        string mode = "IncrementalLoad",

        [Description("Batch size for processing resources (default: 100)")]
        int? batchSize = null,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Read performance tuning settings from configuration
        var effectiveBatchSize = batchSize ?? _configuration.GetValue<int>("Import:BatchSize", 100);
        var channelCapacity = _configuration.GetValue<int>("Import:ChannelCapacity", 1000);

        // Create import job via handler
        var command = new CreateImportJobCommand
        {
            TenantId = resolvedTenantId,
            InputFiles = inputFiles,
            Mode = mode,
            BatchSize = effectiveBatchSize,
            ChannelCapacity = channelCapacity,
            StorageDetail = null // MCP tools don't support storage detail parameters
        };

        var result = await _mediator.SendAsync(command, cancellationToken);

        return new JobSummaryDto
        {
            JobId = result.JobId,
            JobType = "Import",
            Status = result.Status,
            ProgressPercentage = null,
            ProgressDescription = "Job queued and waiting to start",
            CreateDate = result.CreateDate,
            StartDate = null,
            EndDate = null,
            ErrorMessage = null
        };
    }
}
