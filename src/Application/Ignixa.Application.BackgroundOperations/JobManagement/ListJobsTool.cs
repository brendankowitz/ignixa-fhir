// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using Ignixa.Application.Features.Experimental.Mcp.Authorization;
using Ignixa.Application.Features.Experimental.Mcp.Dtos;
using Ignixa.Application.Features.Experimental.Mcp.Tools;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using ModelContextProtocol.Server;

namespace Ignixa.Application.BackgroundOperations.JobManagement;

/// <summary>
/// MCP tool for listing import/export jobs.
/// Returns summary information for all jobs belonging to a tenant.
/// Requires Mcp or Contributor role with read permission.
/// </summary>
[McpServerToolType]
public class ListJobsTool : TenantAwareMcpTool
{
    private readonly IBackgroundJobRepository<ImportJobDefinition> _importRepository;
    private readonly IBackgroundJobRepository<ExportJobDefinition> _exportRepository;

    public ListJobsTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IBackgroundJobRepository<ImportJobDefinition> importRepository,
        IBackgroundJobRepository<ExportJobDefinition> exportRepository,
        IMcpAuthorizationService? mcpAuthorizationService = null)
        : base(fhirRequestContextAccessor, tenantStore, mcpAuthorizationService)
    {
        _importRepository = importRepository ?? throw new ArgumentNullException(nameof(importRepository));
        _exportRepository = exportRepository ?? throw new ArgumentNullException(nameof(exportRepository));
    }

    [McpServerTool(Name = "list_jobs")]
    [Description(@"List all import and export jobs for a tenant.
Returns summary information including status, progress, and timestamps.
Optionally filter by job type (Import/Export) or status.
Requires Mcp or Contributor role with read permission.
Example: jobType='Import', status='Running'")]
    public async Task<List<JobSummaryDto>> ListJobsAsync(
        [Description("Filter by job type: 'Import', 'Export', or leave empty for all")]
        string? jobType = null,

        [Description("Filter by status: 'Queued', 'Running', 'Completed', 'Failed', 'Cancelled', or leave empty for all")]
        string? status = null,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate MCP access and read permission
        await EnsureOperationAuthorizedAsync(McpOperationType.Read, resourceType: null, cancellationToken);

        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Validate job type filter
        if (jobType != null && jobType != "Import" && jobType != "Export")
        {
            throw new ArgumentException($"Invalid jobType '{jobType}'. Must be 'Import', 'Export', or null.", nameof(jobType));
        }

        var results = new List<JobSummaryDto>();

        // Get import jobs if requested
        if (jobType == null || jobType == "Import")
        {
            var importJobs = await _importRepository.ListAsync(
                jobType: (int)BackgroundJobType.Import,
                cancellationToken: cancellationToken);

            // Filter by tenant and status
            var filteredImports = importJobs
                .Where(j => j.Definition.TenantId == resolvedTenantId)
                .Where(j => status == null || j.Status == status);

            foreach (var job in filteredImports)
            {
                results.Add(MapToSummaryDto(job, "Import"));
            }
        }

        // Get export jobs if requested
        if (jobType == null || jobType == "Export")
        {
            var exportJobs = await _exportRepository.ListAsync(
                jobType: (int)BackgroundJobType.Export,
                cancellationToken: cancellationToken);

            // Filter by tenant and status
            var filteredExports = exportJobs
                .Where(j => j.Definition.TenantId == resolvedTenantId)
                .Where(j => status == null || j.Status == status);

            foreach (var job in filteredExports)
            {
                results.Add(MapToSummaryDto(job, "Export"));
            }
        }

        // Sort by create date descending (most recent first)
        return results.OrderByDescending(j => j.CreateDate).ToList();
    }

    private static JobSummaryDto MapToSummaryDto<T>(BackgroundJob<T> job, string jobType) where T : class, IJobDefinition
    {
        // Extract progress if available
        double? progressPercentage = null;
        string? progressDescription = null;

        if (job.Progress != null)
        {
            var progressJson = job.Progress.ToJsonString();

            if (jobType == "Import")
            {
                var progress = JsonSerializer.Deserialize<ImportJobProgress>(progressJson);
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ProcessedFiles} files, {progress.ProcessedResources} resources)"
                    : null;
            }
            else
            {
                var progress = JsonSerializer.Deserialize<ExportJobProgress>(progressJson);
                progressPercentage = progress?.ProgressPercentage;
                progressDescription = progress != null
                    ? $"{progress.ProgressPercentage:F2}% ({progress.ResourcesExported} resources)"
                    : null;
            }
        }

        return new JobSummaryDto
        {
            JobId = job.JobId,
            JobType = jobType,
            Status = job.Status,
            ProgressPercentage = progressPercentage,
            ProgressDescription = progressDescription ?? job.Status,
            CreateDate = job.CreateDate,
            StartDate = job.StartDate,
            EndDate = job.EndDate,
            ErrorMessage = job.ErrorMessage
        };
    }
}
