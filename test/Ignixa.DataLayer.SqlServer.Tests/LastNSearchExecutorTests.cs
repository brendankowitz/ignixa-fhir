using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class LastNSearchExecutorTests
{
    [Fact]
    public void GivenANullExecutionService_WhenExecutorConstructed_ThenItThrows()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new LastNSearchExecutor(null!));
    }

    [Fact]
    public async Task GivenANonLastNCompiledSearch_WhenExecuted_ThenItIsRejectedBeforeSqlExecution()
    {
        // Arrange
        var execution = new RecordingSqlExecutionService([]);
        var executor = new LastNSearchExecutor(execution);
        CompiledSearch compiled = CreateCompiledSearch(
            [],
            new ResultShape.Matches());

        // Act
        ArgumentException exception = await Should.ThrowAsync<ArgumentException>(() => executor.ExecuteAsync(
            42,
            compiled,
            reader => reader.GetInt64(0),
            CancellationToken.None));

        // Assert
        exception.ParamName.ShouldBe("compiledSearch");
        execution.Command.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAllSupportedCompiledParameters_WhenExecuted_ThenItUsesExplicitSqlTypesAndPreservesValues()
    {
        // Arrange
        DateTime dateTime = new(2026, 8, 29, 12, 34, 56, DateTimeKind.Utc);
        DateTimeOffset dateTimeOffset = new(2026, 8, 29, 12, 34, 56, TimeSpan.FromHours(-7));
        EmittedSqlParameter[] emittedParameters =
        [
            new("@p0", (short)104),
            new("@p1", 3),
            new("@p2", 1234567890123L),
            new("@p3", "final"),
            new("@p4", dateTime),
            new("@p5", dateTimeOffset),
        ];
        var execution = new RecordingSqlExecutionService([7L, 11L]);
        var executor = new LastNSearchExecutor(execution);
        CompiledSearch compiled = CreateCompiledSearch(
            emittedParameters,
            new ResultShape.LastN(new LastNSpec(104, 210, 211, 3)));
        using var cancellationTokenSource = new CancellationTokenSource();
        Func<SqlDataReader, long> readRow = reader => reader.GetInt64(0);

        // Act
        IReadOnlyList<long> result = await executor.ExecuteAsync(
            42,
            compiled,
            readRow,
            cancellationTokenSource.Token);

        // Assert
        result.ShouldBe([7L, 11L]);
        execution.TenantId.ShouldBe(42);
        execution.ReadRow.ShouldBeSameAs(readRow);
        execution.CancellationToken.ShouldBe(cancellationTokenSource.Token);
        execution.Command!.CommandText.ShouldBe("SELECT T1, Sid1 FROM materialized_lastn");
        AssertParameter(execution.Command.Parameters[0], "@p0", SqlDbType.SmallInt, 0, (short)104);
        AssertParameter(execution.Command.Parameters[1], "@p1", SqlDbType.Int, 0, 3);
        AssertParameter(execution.Command.Parameters[2], "@p2", SqlDbType.BigInt, 0, 1234567890123L);
        AssertParameter(execution.Command.Parameters[3], "@p3", SqlDbType.NVarChar, 5, "final");
        AssertParameter(execution.Command.Parameters[4], "@p4", SqlDbType.DateTime2, 0, dateTime);
        AssertParameter(execution.Command.Parameters[5], "@p5", SqlDbType.DateTimeOffset, 0, dateTimeOffset);
    }

    [Fact]
    public async Task GivenAnUnavailableMaterializationSqlError_WhenExecuted_ThenItThrowsLastNUnavailable()
    {
        // Arrange
        SqlException sqlException = CreateSqlException(50403);
        var executor = new LastNSearchExecutor(new RecordingSqlExecutionService([], sqlException));

        // Act
        LastNUnavailableException exception = await Should.ThrowAsync<LastNUnavailableException>(() =>
            executor.ExecuteAsync(
                42,
                CreateLastNCompiledSearch(),
                reader => reader.GetInt64(0),
                CancellationToken.None));

        // Assert
        exception.Message.ShouldBe("$lastn is unavailable while Observation code groups are not ready.");
        exception.InnerException.ShouldBeSameAs(sqlException);
    }

    [Fact]
    public async Task GivenADifferentSqlError_WhenExecuted_ThenTheOriginalExceptionAndStackPropagate()
    {
        // Arrange
        SqlException sqlException = CreateSqlException(1205);
        var executor = new LastNSearchExecutor(new RecordingSqlExecutionService([], sqlException));

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(() => executor.ExecuteAsync(
            42,
            CreateLastNCompiledSearch(),
            reader => reader.GetInt64(0),
            CancellationToken.None));

        // Assert
        exception.ShouldBeSameAs(sqlException);
        exception.StackTrace.ShouldNotBeNull().ShouldContain(nameof(RecordingSqlExecutionService.ThrowSqlException));
    }

    [Fact]
    public void GivenSchemaDeploymentServices_WhenRegistered_ThenLastNExecutorHasOneSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        // Act
        services.AddIgnixaSqlServerSchemaDeployment(configuration);

        // Assert
        ServiceDescriptor[] descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(ILastNSearchExecutor))
            .ToArray();
        descriptors.ShouldHaveSingleItem();
        descriptors[0].ImplementationType.ShouldBe(typeof(LastNSearchExecutor));
        descriptors[0].Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    private static CompiledSearch CreateLastNCompiledSearch()
        => CreateCompiledSearch(
            [],
            new ResultShape.LastN(new LastNSpec(104, 210, 211, 1)));

    private static CompiledSearch CreateCompiledSearch(
        IReadOnlyList<EmittedSqlParameter> parameters,
        ResultShape shape)
    {
        var query = new QueryPlan(
            [new CteDefinition.ResourceSource(104)],
            new MatchPageSpec(new CteRef(0), Shape: shape));
        return new CompiledSearch("SELECT T1, Sid1 FROM materialized_lastn", parameters, query);
    }

    private static void AssertParameter(
        SqlParameter parameter,
        string name,
        SqlDbType type,
        int size,
        object value)
    {
        parameter.ParameterName.ShouldBe(name);
        parameter.SqlDbType.ShouldBe(type);
        parameter.Size.ShouldBe(size);
        parameter.Value.ShouldBe(value);
    }

    private static SqlException CreateSqlException(int number)
    {
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        ConstructorInfo errorConstructor = typeof(SqlError)
            .GetConstructors(instanceFlags)
            .Single(constructor => constructor.GetParameters() is { Length: 9 } parameters
                && parameters[^1].ParameterType == typeof(Exception));
        var error = (SqlError)errorConstructor.Invoke(
            [number, (byte)0, (byte)0, "server", "failure", "procedure", 1, 0, null]);
        typeof(SqlErrorCollection)
            .GetMethod("Add", instanceFlags)!
            .Invoke(errors, [error]);

        MethodInfo factory = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(SqlErrorCollection), typeof(string)],
            modifiers: null)!;
        return (SqlException)factory.Invoke(null, [errors, "16.0"])!;
    }

    private sealed class RecordingSqlExecutionService(
        IReadOnlyList<object> results,
        SqlException? exception = null) : ISqlExecutionService
    {
        public int TenantId { get; private set; }

        public SqlCommand? Command { get; private set; }

        public object? ReadRow { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken)
        {
            TenantId = tenantId;
            Command = command;
            ReadRow = readRow;
            CancellationToken = cancellationToken;
            if (exception is not null)
            {
                ThrowSqlException(exception);
            }

            return Task.FromResult<IReadOnlyList<TResult>>(results.Cast<TResult>().ToArray());
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            bool disableRetries = false)
        {
            throw new NotSupportedException();
        }

        [DoesNotReturn]
        public static void ThrowSqlException(SqlException exception) => throw exception;
    }
}
