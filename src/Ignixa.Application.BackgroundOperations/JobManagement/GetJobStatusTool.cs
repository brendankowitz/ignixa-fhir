// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Domain.Abstractions;
using Medino;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace Ignixa.Application.BackgroundOperations.JobManagement;

/// <summary>
/// MCP tool for getting the status of import/export jobs.
/// Polls DurableTask orchestration state and updates job metadata.
/// </summary>
[McpServerToolType]
public class GetJobStatusTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;

    public GetJobStatusTool(
        IHttpContextAccessor httpContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator)
        : base(httpContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [McpServerTool(Name = "get_job_status")]
    [Description(@"Get the current status of an import or export job.
Returns detailed progress, completion status, and results.
Example: jobId='abc123', jobType='Import'")]
    public async Task<JobStatusDto> GetJobStatusAsync(
        [Description("Job ID returned from start_import_job or start_export_job")]
        string jobId,

        [Description("Job type: 'Import' or 'Export'")]
        string jobType,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Query job status via handler
        var query = new GetJobStatusQuery
        {
            JobId = jobId,
            JobType = jobType,
            TenantId = resolvedTenantId
        };

        var jobStatus = await _mediator.SendAsync(query, cancellationToken);

        return new JobStatusDto
        {
            JobId = jobStatus.JobId,
            JobType = jobStatus.JobType,
            Status = jobStatus.Status,
            ProgressPercentage = jobStatus.ProgressPercentage,
            ProgressDescription = jobStatus.ProgressDescription,
            CreateDate = jobStatus.CreateDate,
            StartDate = jobStatus.StartDate,
            EndDate = jobStatus.EndDate,
            ErrorMessage = jobStatus.ErrorMessage,
            Definition = jobStatus.Definition,
            Result = jobStatus.Result
        };
    }
}
