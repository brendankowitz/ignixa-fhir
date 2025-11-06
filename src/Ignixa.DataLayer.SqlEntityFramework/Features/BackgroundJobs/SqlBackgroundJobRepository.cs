// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.BackgroundJobs;

/// <summary>
/// Entity Framework Core implementation of IBackgroundJobRepository for SQL Server.
/// Provides persistent storage for all background job types (import, export, validate, etc.) in system partition (partition 0).
/// TenantId is stored in the Definition/payload, not as a schema column.
/// Enforces tenant isolation based on deployment mode (Isolated = validate, Distributed = skip).
/// T is constrained to IJobDefinition for compile-time tenant access (no reflection required).
/// </summary>
public class SqlBackgroundJobRepository<T> : IBackgroundJobRepository<T>
    where T : class, IJobDefinition
{
    private readonly FhirDbContext _dbContext;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ILogger<SqlBackgroundJobRepository<T>> _logger;

    public SqlBackgroundJobRepository(
        FhirDbContext dbContext,
        ITenantConfigurationStore tenantConfigStore,
        ILogger<SqlBackgroundJobRepository<T>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BackgroundJob<T>?> GetAsync(string jobId, int tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BackgroundJobs
            .FirstOrDefaultAsync(b => b.JobId == jobId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        var job = MapEntityToModel(entity);

        // Validate tenant ownership based on deployment mode
        // Isolated mode (multi-tenant): MUST validate since BackgroundJobs are in partition 0 (shared)
        // Distributed mode (single-customer sharding): Skip validation (all data belongs to same customer)
        if (ShouldValidateTenant() && !ValidateTenantOwnership(job, tenantId))
        {
            _logger.LogWarning("Job {JobId} access denied for tenant {TenantId}", jobId, tenantId);
            return null; // Hide job existence from unauthorized tenants
        }

        return job;
    }

    public async Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var entity = MapModelToEntity(job);
        _dbContext.BackgroundJobs.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Created background job {JobId}", job.JobId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error creating background job {JobId}", job.JobId);
            throw;
        }
    }

    public async Task UpdateAsync(BackgroundJob<T> job, int tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var entity = await _dbContext.BackgroundJobs
            .FirstOrDefaultAsync(b => b.JobId == job.JobId, cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning("Background job {JobId} not found for update", job.JobId);
            throw new InvalidOperationException($"Background job {job.JobId} not found");
        }

        // Validate tenant ownership based on deployment mode
        if (ShouldValidateTenant() && !ValidateTenantOwnership(job, tenantId))
        {
            _logger.LogWarning("Job {JobId} update denied for tenant {TenantId}", job.JobId, tenantId);
            throw new InvalidOperationException($"Not authorized to update job {job.JobId}");
        }

        // Update entity properties from the model
        UpdateEntityFromModel(entity, job);
        entity.HeartbeatDate = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Updated background job {JobId}", job.JobId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error updating background job {JobId}", job.JobId);
            throw;
        }
    }

    public async Task DeleteAsync(string jobId, int tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BackgroundJobs
            .FirstOrDefaultAsync(b => b.JobId == jobId, cancellationToken);

        if (entity != null)
        {
            var job = MapEntityToModel(entity);

            // Validate tenant ownership based on deployment mode
            if (ShouldValidateTenant() && !ValidateTenantOwnership(job, tenantId))
            {
                _logger.LogWarning("Job {JobId} delete denied for tenant {TenantId}", jobId, tenantId);
                throw new InvalidOperationException($"Not authorized to delete job {jobId}");
            }

            _dbContext.BackgroundJobs.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted background job {JobId}", jobId);
        }
    }

    public async Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int? jobType = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BackgroundJobs.AsQueryable();

        if (jobType.HasValue)
        {
            query = query.Where(b => b.JobType == jobType.Value);
        }

        var entities = await query
            .OrderByDescending(b => b.CreateDate)
            .ToListAsync(cancellationToken);

        return entities.Select(MapEntityToModel).ToList().AsReadOnly();
    }

    /// <summary>
    /// Maps database entity to domain model.
    /// </summary>
    private BackgroundJob<T> MapEntityToModel(BackgroundJobEntity entity)
    {
        var definition = JsonSerializer.Deserialize<T>(entity.Definition)
            ?? throw new InvalidOperationException($"Failed to deserialize Definition for job {entity.JobId}");

        var model = new BackgroundJob<T>
        {
            JobId = entity.JobId,
            JobType = entity.JobType,
            OrchestrationInstanceId = entity.OrchestrationInstanceId,
            Status = entity.Status,
            Definition = definition,
            Progress = entity.Progress != null ? JsonNode.Parse(entity.Progress) : null,
            Result = entity.Result != null ? JsonNode.Parse(entity.Result) : null,
            CreateDate = entity.CreateDate,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            HeartbeatDate = entity.HeartbeatDate,
            Worker = entity.Worker,
            ErrorMessage = entity.ErrorMessage,
            CancelRequested = entity.CancelRequested
        };

        return model;
    }

    /// <summary>
    /// Maps domain model to database entity.
    /// </summary>
    private BackgroundJobEntity MapModelToEntity(BackgroundJob<T> model)
    {
        var definitionJson = JsonSerializer.Serialize(model.Definition);

        var entity = new BackgroundJobEntity
        {
            JobId = model.JobId,
            JobType = model.JobType,
            OrchestrationInstanceId = model.OrchestrationInstanceId,
            Status = model.Status,
            Definition = definitionJson,
            Progress = model.Progress?.ToJsonString(),
            Result = model.Result?.ToJsonString(),
            CreateDate = model.CreateDate,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            HeartbeatDate = model.HeartbeatDate,
            Worker = model.Worker,
            ErrorMessage = model.ErrorMessage,
            CancelRequested = model.CancelRequested
        };

        return entity;
    }

    /// <summary>
    /// Updates entity properties from model without touching the database key.
    /// </summary>
    private void UpdateEntityFromModel(BackgroundJobEntity entity, BackgroundJob<T> model)
    {
        entity.OrchestrationInstanceId = model.OrchestrationInstanceId;
        entity.Status = model.Status;
        entity.Definition = JsonSerializer.Serialize(model.Definition);
        entity.Progress = model.Progress?.ToJsonString();
        entity.Result = model.Result?.ToJsonString();
        entity.StartDate = model.StartDate;
        entity.EndDate = model.EndDate;
        entity.Worker = model.Worker;
        entity.ErrorMessage = model.ErrorMessage;
        entity.CancelRequested = model.CancelRequested;
    }

    /// <summary>
    /// Checks if tenant validation should be enforced based on deployment mode.
    /// Isolated mode (multi-tenant) = validate (TRUE)
    /// Distributed mode (single-customer sharding) = skip validation (FALSE)
    /// </summary>
    private bool ShouldValidateTenant()
    {
        return _tenantConfigStore.Mode == TenantMode.Isolated;
    }

    /// <summary>
    /// Validates that the job belongs to the specified tenant by checking the TenantId in the Definition payload.
    /// Uses direct property access via IJobDefinition constraint (no reflection needed).
    /// </summary>
    private bool ValidateTenantOwnership(BackgroundJob<T> job, int tenantId)
    {
        // IJobDefinition constraint guarantees job.Definition.TenantId is always available
        return job.Definition.TenantId == tenantId;
    }
}
