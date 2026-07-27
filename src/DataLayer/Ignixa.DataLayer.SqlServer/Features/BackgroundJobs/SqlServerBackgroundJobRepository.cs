using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Sql.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;

/// <summary>
/// SQL-backed <see cref="IBackgroundJobRepository{T}"/> over <see cref="ISqlExecutionService"/>.
/// <para>
/// This is a rewrite rather than a port. The EF implementation it replaces modelled
/// <c>dbo.BackgroundJobs</c> with <c>JobId</c> as the whole primary key and no <c>TenantId</c> property at
/// all, while the deployed table declares <c>PRIMARY KEY (TenantId, JobId)</c> with <c>TenantId INT NOT
/// NULL</c> and no default. An insert through that model omits the column and fails with SQL error 515, so
/// it could never have run against this schema -- consistent with it being registered but never resolved.
/// The behavioural rules below (tenant-validation mode, ordering, and the differing error shapes) are taken
/// from that implementation; the storage model is taken from the DDL.
/// </para>
/// <para>
/// Rows carry their owning tenant in the <c>TenantId</c> column, sourced from
/// <see cref="IJobDefinition.TenantId"/>. Reads still locate a job by <c>JobId</c> alone so that Distributed
/// mode continues to see jobs across tenants; the tenant check stays in code, exactly where it was.
/// </para>
/// </summary>
public sealed class SqlServerBackgroundJobRepository<T>(
    ISqlExecutionService sqlExecutionService,
    int connectionTenantId,
    ITenantConfigurationStore tenantConfigStore,
    ILogger<SqlServerBackgroundJobRepository<T>> logger) : IBackgroundJobRepository<T>
    where T : class, IJobDefinition
{
    private static readonly TableDescriptor Jobs = SqlCatalog.Default.Table("BackgroundJobs");

    private static readonly string AllColumns = string.Join(", ",
        "TenantId", "JobId", "JobType", "OrchestrationInstanceId", "Status", "Definition", "Progress",
        "Result", "CreateDate", "StartDate", "EndDate", "HeartbeatDate", "Worker", "ErrorMessage",
        "CancelRequested")
        .Split(", ")
        .Select(c => Jobs.Column(c).Name)
        .Aggregate((a, b) => $"{a}, {b}");

    private static readonly string QualifiedTable = $"{Jobs.SchemaName}.{Jobs.TableName}";

    public async Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        using var command = CreateCommand(
            $"INSERT INTO {QualifiedTable} ({AllColumns}) VALUES " +
            "(@tenantId, @jobId, @jobType, @orchestrationInstanceId, @status, @definition, @progress, " +
            "@result, @createDate, @startDate, @endDate, @heartbeatDate, @worker, @errorMessage, @cancelRequested)");

        BindAll(command, job);

        await sqlExecutionService.ExecuteNonQueryAsync(connectionTenantId, command, cancellationToken);
        logger.LogInformation("Created background job {JobId}", job.JobId);
    }

    public async Task<BackgroundJob<T>?> GetAsync(string jobId, int tenantId, CancellationToken cancellationToken = default)
    {
        var job = await FindByJobIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (ShouldValidateTenant() && job.Definition.TenantId != tenantId)
        {
            // Returning null rather than throwing hides the job's existence from an unauthorised tenant.
            // Update and Delete deliberately throw instead -- the caller there already named a specific job.
            logger.LogWarning("Job {JobId} access denied for tenant {TenantId}", jobId, tenantId);
            return null;
        }

        return job;
    }

    public async Task UpdateAsync(BackgroundJob<T> job, int tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var existing = await FindByJobIdAsync(job.JobId, cancellationToken)
            ?? throw NotFound(job.JobId);

        if (ShouldValidateTenant() && job.Definition.TenantId != tenantId)
        {
            logger.LogWarning("Job {JobId} update denied for tenant {TenantId}", job.JobId, tenantId);
            throw new InvalidOperationException($"Not authorized to update job {job.JobId}");
        }

        // CreateDate, JobId and JobType are deliberately not updated. HeartbeatDate is stamped here rather
        // than taken from the model, so an update always refreshes liveness.
        using var command = CreateCommand(
            $"UPDATE {QualifiedTable} SET " +
            $"{Jobs.Column("OrchestrationInstanceId").Name} = @orchestrationInstanceId, " +
            $"{Jobs.Column("Status").Name} = @status, " +
            $"{Jobs.Column("Definition").Name} = @definition, " +
            $"{Jobs.Column("Progress").Name} = @progress, " +
            $"{Jobs.Column("Result").Name} = @result, " +
            $"{Jobs.Column("StartDate").Name} = @startDate, " +
            $"{Jobs.Column("EndDate").Name} = @endDate, " +
            $"{Jobs.Column("HeartbeatDate").Name} = @heartbeatDate, " +
            $"{Jobs.Column("Worker").Name} = @worker, " +
            $"{Jobs.Column("ErrorMessage").Name} = @errorMessage, " +
            $"{Jobs.Column("CancelRequested").Name} = @cancelRequested " +
            $"WHERE {Jobs.Column("TenantId").Name} = @rowTenantId AND {Jobs.Column("JobId").Name} = @jobId");

        command.Parameters.AddWithValue("@rowTenantId", existing.Definition.TenantId);
        command.Parameters.AddWithValue("@jobId", job.JobId);
        command.Parameters.AddWithValue("@orchestrationInstanceId", (object?)job.OrchestrationInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", job.Status);
        command.Parameters.AddWithValue("@definition", JsonSerializer.Serialize(job.Definition));
        command.Parameters.AddWithValue("@progress", (object?)job.Progress?.ToJsonString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@result", (object?)job.Result?.ToJsonString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@startDate", (object?)job.StartDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@endDate", (object?)job.EndDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@heartbeatDate", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@worker", (object?)job.Worker ?? DBNull.Value);
        command.Parameters.AddWithValue("@errorMessage", (object?)job.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@cancelRequested", job.CancelRequested);

        await sqlExecutionService.ExecuteNonQueryAsync(connectionTenantId, command, cancellationToken);
        logger.LogDebug("Updated background job {JobId}", job.JobId);
    }

    public async Task DeleteAsync(string jobId, int tenantId, CancellationToken cancellationToken = default)
    {
        var existing = await FindByJobIdAsync(jobId, cancellationToken);
        if (existing is null)
        {
            // Absent is not an error here, unlike Update.
            return;
        }

        if (ShouldValidateTenant() && existing.Definition.TenantId != tenantId)
        {
            logger.LogWarning("Job {JobId} delete denied for tenant {TenantId}", jobId, tenantId);
            throw new InvalidOperationException($"Not authorized to delete job {jobId}");
        }

        using var command = CreateCommand(
            $"DELETE FROM {QualifiedTable} WHERE {Jobs.Column("TenantId").Name} = @tenantId " +
            $"AND {Jobs.Column("JobId").Name} = @jobId");
        command.Parameters.AddWithValue("@tenantId", existing.Definition.TenantId);
        command.Parameters.AddWithValue("@jobId", jobId);

        await sqlExecutionService.ExecuteNonQueryAsync(connectionTenantId, command, cancellationToken);
        logger.LogInformation("Deleted background job {JobId}", jobId);
    }

    public async Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int? jobType = null, CancellationToken cancellationToken = default)
    {
        // Deliberately not tenant-filtered: the interface takes no tenant, and the implementation this
        // replaces listed across all tenants too. Callers that need scoping filter on Definition.TenantId.
        var where = jobType.HasValue ? $" WHERE {Jobs.Column("JobType").Name} = @jobType" : string.Empty;

        using var command = CreateCommand(
            $"SELECT {AllColumns} FROM {QualifiedTable}{where} " +
            $"ORDER BY {Jobs.Column("CreateDate").Name} DESC");

        if (jobType.HasValue)
        {
            command.Parameters.AddWithValue("@jobType", jobType.Value);
        }

        return await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadJob, cancellationToken);
    }

    private async Task<BackgroundJob<T>?> FindByJobIdAsync(string jobId, CancellationToken cancellationToken)
    {
        using var command = CreateCommand(
            $"SELECT {AllColumns} FROM {QualifiedTable} WHERE {Jobs.Column("JobId").Name} = @jobId");
        command.Parameters.AddWithValue("@jobId", jobId);

        var rows = await sqlExecutionService.ExecuteReaderAsync(connectionTenantId, command, ReadJob, cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    private void BindAll(SqlCommand command, BackgroundJob<T> job)
    {
        command.Parameters.AddWithValue("@tenantId", job.Definition.TenantId);
        command.Parameters.AddWithValue("@jobId", job.JobId);
        command.Parameters.AddWithValue("@jobType", job.JobType);
        command.Parameters.AddWithValue("@orchestrationInstanceId", (object?)job.OrchestrationInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", job.Status);
        command.Parameters.AddWithValue("@definition", JsonSerializer.Serialize(job.Definition));
        command.Parameters.AddWithValue("@progress", (object?)job.Progress?.ToJsonString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@result", (object?)job.Result?.ToJsonString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@createDate", job.CreateDate);
        command.Parameters.AddWithValue("@startDate", (object?)job.StartDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@endDate", (object?)job.EndDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@heartbeatDate", job.HeartbeatDate);
        command.Parameters.AddWithValue("@worker", (object?)job.Worker ?? DBNull.Value);
        command.Parameters.AddWithValue("@errorMessage", (object?)job.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@cancelRequested", job.CancelRequested);
    }

    private static BackgroundJob<T> ReadJob(SqlDataReader reader)
    {
        var jobId = reader.GetString(1);
        var definitionJson = reader.GetString(5);

        var definition = JsonSerializer.Deserialize<T>(definitionJson)
            ?? throw new InvalidOperationException($"Failed to deserialize Definition for job {jobId}");

        return new BackgroundJob<T>
        {
            JobId = jobId,
            JobType = reader.GetInt32(2),
            OrchestrationInstanceId = reader.IsDBNull(3) ? null : reader.GetString(3),
            Status = reader.GetString(4),
            Definition = definition,
            Progress = reader.IsDBNull(6) ? null : JsonNode.Parse(reader.GetString(6)),
            Result = reader.IsDBNull(7) ? null : JsonNode.Parse(reader.GetString(7)),
            CreateDate = reader.GetDateTimeOffset(8),
            StartDate = reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9),
            EndDate = reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10),
            HeartbeatDate = reader.GetDateTimeOffset(11),
            Worker = reader.IsDBNull(12) ? null : reader.GetString(12),
            ErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13),
            CancelRequested = reader.GetBoolean(14),
        };
    }

    // Isolated mode is multi-tenant, so ownership must be checked. Distributed mode shards one customer
    // across databases, where every job already belongs to that customer.
    private bool ShouldValidateTenant() => tenantConfigStore.Mode == TenantMode.Isolated;

    // Every SQL string in this type is assembled from SqlCatalog-sourced identifiers and fixed literals;
    // all caller data flows through parameters. Stating the CA2100 justification once here rather than at
    // each of the five call sites.
    private static SqlCommand CreateCommand(string sql)
    {
#pragma warning disable CA2100
        return new SqlCommand(sql);
#pragma warning restore CA2100
    }

    private static InvalidOperationException NotFound(string jobId)
        => new($"Background job {jobId} not found");
}
