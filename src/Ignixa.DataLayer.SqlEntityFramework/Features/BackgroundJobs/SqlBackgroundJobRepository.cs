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
/// Provides persistent storage for all background job types (import, export, validate, etc.).
/// </summary>
public class SqlBackgroundJobRepository<T> : IBackgroundJobRepository<T>
    where T : class
{
    private readonly FhirDbContext _dbContext;
    private readonly ILogger<SqlBackgroundJobRepository<T>> _logger;

    public SqlBackgroundJobRepository(FhirDbContext dbContext, ILogger<SqlBackgroundJobRepository<T>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BackgroundJob<T>?> GetAsync(int tenantId, string jobId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BackgroundJobs
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.JobId == jobId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        return MapEntityToModel(entity);
    }

    public async Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var entity = MapModelToEntity(job);
        _dbContext.BackgroundJobs.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Created background job {JobId} for tenant {TenantId}", job.JobId, job.TenantId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error creating background job {JobId} for tenant {TenantId}", job.JobId, job.TenantId);
            throw;
        }
    }

    public async Task UpdateAsync(BackgroundJob<T> job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var entity = await _dbContext.BackgroundJobs
            .FirstOrDefaultAsync(b => b.TenantId == job.TenantId && b.JobId == job.JobId, cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning("Background job {JobId} for tenant {TenantId} not found for update", job.JobId, job.TenantId);
            throw new InvalidOperationException($"Background job {job.JobId} not found");
        }

        // Update entity properties from the model
        UpdateEntityFromModel(entity, job);
        entity.HeartbeatDate = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Updated background job {JobId} for tenant {TenantId}", job.JobId, job.TenantId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error updating background job {JobId} for tenant {TenantId}", job.JobId, job.TenantId);
            throw;
        }
    }

    public async Task DeleteAsync(int tenantId, string jobId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.BackgroundJobs
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.JobId == jobId, cancellationToken);

        if (entity != null)
        {
            _dbContext.BackgroundJobs.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted background job {JobId} for tenant {TenantId}", jobId, tenantId);
        }
    }

    public async Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int tenantId, int? jobType = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BackgroundJobs
            .Where(b => b.TenantId == tenantId);

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
            TenantId = entity.TenantId,
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
            TenantId = model.TenantId,
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
}
