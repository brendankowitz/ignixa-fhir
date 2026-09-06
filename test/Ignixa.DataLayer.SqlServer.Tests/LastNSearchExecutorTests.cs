using System.Data;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class LastNSearchExecutorTests
{
    [Fact]
    public async Task GivenCompiledParameters_WhenExecuted_ThenPreservesSqlTypesValuesTenantAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        DateTime date = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTimeOffset instant = new(date);
        EmittedSqlParameter[] parameters =
        [
            new("@p0", (short)104),
            new("@p1", 3),
            new("@p2", 1234567890123L),
            new("@p3", 5.35m),
            new("@p4", "a'|b"),
            new("@p5", date),
            new("@p6", instant),
            new("@p7", new string('x', 5000)),
        ];
        SqlDbType[] types =
        [
            SqlDbType.SmallInt, SqlDbType.Int, SqlDbType.BigInt, SqlDbType.Decimal,
            SqlDbType.NVarChar, SqlDbType.DateTime2, SqlDbType.DateTimeOffset, SqlDbType.NVarChar,
        ];
        CompiledSearch compiled = Compile(parameters);
        bool executed = false;
        var execution = new InspectingLastNSqlExecutionService((tenantId, command, cancellationToken) =>
        {
            executed = true;
            tenantId.ShouldBe(42);
            cancellationToken.ShouldBe(cancellation.Token);
            command.CommandText.ShouldBe(compiled.Sql);
            command.Parameters.Count.ShouldBe(parameters.Length);
            for (int index = 0; index < parameters.Length; index++)
            {
                command.Parameters[index].ParameterName.ShouldBe(parameters[index].Name);
                command.Parameters[index].Value.ShouldBe(parameters[index].Value);
                command.Parameters[index].SqlDbType.ShouldBe(types[index]);
            }

            command.Parameters[3].Precision.ShouldBe((byte)36);
            command.Parameters[3].Scale.ShouldBe((byte)18);
            command.Parameters[4].Size.ShouldBe(4);
            command.Parameters[7].Size.ShouldBe(5000);
        });

        await new LastNSearchExecutor(execution).ExecuteAsync(
            42, compiled, reader => reader.GetInt64(1), cancellation.Token);

        executed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAnOrdinarySearch_WhenExecuted_ThenRejectsBeforeSendingSql()
    {
        var execution = new InspectingLastNSqlExecutionService((_, _, _) =>
            throw new InvalidOperationException("Ordinary searches must not reach the lastn executor."));

        await Should.ThrowAsync<ArgumentException>(() => new LastNSearchExecutor(execution).ExecuteAsync(
            1, Compile([], new ResultShape.Matches()), reader => reader.GetInt64(1), CancellationToken.None));
    }

    [Fact]
    public async Task GivenAnUnsupportedParameterType_WhenExecuted_ThenRejectsBeforeSendingSql()
    {
        var execution = new InspectingLastNSqlExecutionService((_, _, _) =>
            throw new InvalidOperationException("Unsupported parameter types must not reach SQL."));

        await Should.ThrowAsync<NotSupportedException>(() => new LastNSearchExecutor(execution).ExecuteAsync(
            1, Compile([new("@p0", Guid.NewGuid())]), reader => reader.GetInt64(1), CancellationToken.None));
    }

    [Fact]
    public async Task GivenCancelledExecution_WhenExecuted_ThenPropagatesCancellationInsteadOfEmptyResults()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var execution = new InspectingLastNSqlExecutionService((_, _, cancellationToken) =>
            cancellationToken.ThrowIfCancellationRequested());

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            new LastNSearchExecutor(execution).ExecuteAsync(
                1, Compile([]), reader => reader.GetInt64(1), cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
    }

    private static CompiledSearch Compile(
        IReadOnlyList<EmittedSqlParameter> parameters,
        ResultShape? shape = null)
    {
        var query = new QueryPlan(
            [new CteDefinition.ResourceSource(104)],
            new MatchPageSpec(new CteRef(0), Shape: shape ?? new ResultShape.LastN(new LastNSpec(104, 210, 211, 1))));
        return new CompiledSearch("SELECT @p0 AS T1, @p1 AS Sid1", parameters, query);
    }
}
