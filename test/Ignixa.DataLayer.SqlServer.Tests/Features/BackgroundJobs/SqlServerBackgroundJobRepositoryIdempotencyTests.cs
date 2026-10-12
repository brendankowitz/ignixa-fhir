using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.BackgroundJobs;

/// <summary>
/// <c>CreateAsync</c> runs a bare <c>INSERT INTO dbo.BackgroundJobs</c> through
/// <c>ExecuteNonQueryAsync</c>. Unlike the four existing <c>INSERT ... OUTPUT INSERTED</c> sites this
/// class shares the pattern with, a retried duplicate here is loud rather than silent --
/// <c>dbo.BackgroundJobs</c>' primary key is <c>(TenantId, JobId)</c>, so a second attempt fails with a
/// duplicate-key error rather than creating a second row -- but it is still wrong to retry a write that
/// may have already committed, so the insert must still declare itself
/// <see cref="SqlCommandIdempotency.NonIdempotent"/>. This pins the call site, not the mechanism -- that
/// a <c>NonIdempotent</c> command really does bypass the retry pipeline is already covered by
/// <c>SqlExecutionServiceConnectionTests</c>. Nothing else would notice this argument being dropped.
/// </summary>
public class SqlServerBackgroundJobRepositoryIdempotencyTests
{
    [Fact]
    public async Task GivenANewJob_WhenCreated_ThenTheInsertDeclaresItselfNonIdempotent()
    {
        var sql = Substitute.For<ISqlExecutionService>();
        sql.ExecuteNonQueryAsync(Arg.Any<int>(), Arg.Any<SqlCommand>(), Arg.Any<CancellationToken>(), Arg.Any<SqlCommandIdempotency>())
            .Returns(1);

        var repository = new SqlServerBackgroundJobRepository<ExportJobDefinition>(
            sql,
            connectionTenantId: 1,
            Substitute.For<ITenantConfigurationStore>(),
            NullLogger<SqlServerBackgroundJobRepository<ExportJobDefinition>>.Instance);

        var job = new BackgroundJob<ExportJobDefinition>
        {
            JobId = Guid.NewGuid().ToString(),
            JobType = 1,
            Status = "Queued",
            CreateDate = DateTimeOffset.UtcNow,
            Definition = new ExportJobDefinition
            {
                TenantId = 7,
                ResourceTypes = ["Patient"],
                TypeFilters = new Dictionary<string, string>(),
                OutputFormat = "ndjson",
                OutputPath = "/exports/test",
            },
        };

        await repository.CreateAsync(job, CancellationToken.None);

        await sql.Received(1).ExecuteNonQueryAsync(
            1,
            Arg.Is<SqlCommand>(c => c.CommandText.Contains("INSERT INTO dbo.BackgroundJobs", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>(),
            SqlCommandIdempotency.NonIdempotent);
    }
}
