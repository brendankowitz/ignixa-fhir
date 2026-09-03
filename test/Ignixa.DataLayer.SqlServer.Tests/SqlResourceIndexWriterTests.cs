using System.Data;
using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SqlResourceIndexWriterTests
{
    [Fact]
    public void GivenANullExecutionService_WhenWriterConstructed_ThenItThrows()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SqlResourceIndexWriter(null!));
    }

    [Fact]
    public async Task GivenANullResourceId_WhenHardDeleteExecutes_ThenItThrows()
    {
        // Arrange
        var writer = new SqlResourceIndexWriter(new RecordingSqlExecutionService(outputValue: 0));

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() => writer.HardDeleteAsync(
            42,
            7,
            null!,
            keepCurrentVersion: false,
            isResourceChangeCaptureEnabled: true,
            CancellationToken.None));
    }

    [Fact]
    public async Task GivenAMergeBatch_WhenWriterExecutes_ThenItUsesTheAtomicWrapperAndExistingTvpNames()
    {
        // Arrange
        var execution = new RecordingSqlExecutionService(outputValue: 7);
        var writer = new SqlResourceIndexWriter(execution);

        // Act
        int affected = await writer.MergeAsync(
            42,
            new SqlResourceMergeRequest(
                RaiseExceptionOnConflict: true,
                IsResourceChangeCaptureEnabled: false,
                TransactionId: 9001,
                SingleTransaction: true,
                Batch: CreateCompleteBatch()),
            CancellationToken.None);

        // Assert
        affected.ShouldBe(7);
        execution.TenantId.ShouldBe(42);
        execution.DisableRetries.ShouldBeTrue();
        execution.CancellationToken.ShouldBe(CancellationToken.None);
        AssertCommand(
            execution.Command!,
            "dbo.MergeResourcesAndMaintainLastNGroups",
            [
                ("@AffectedRows", SqlDbType.Int, ParameterDirection.Output, null),
                ("@RaiseExceptionOnConflict", SqlDbType.Bit, ParameterDirection.Input, null),
                ("@IsResourceChangeCaptureEnabled", SqlDbType.Bit, ParameterDirection.Input, null),
                ("@TransactionId", SqlDbType.BigInt, ParameterDirection.Input, null),
                ("@SingleTransaction", SqlDbType.Bit, ParameterDirection.Input, null),
                ("@Resources", SqlDbType.Structured, ParameterDirection.Input, "dbo.ResourceList"),
                ("@ResourceWriteClaims", SqlDbType.Structured, ParameterDirection.Input, "dbo.ResourceWriteClaimList"),
                ("@ReferenceSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.ReferenceSearchParamList"),
                ("@TokenSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenSearchParamList"),
                ("@TokenTexts", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenTextList"),
                ("@StringSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.StringSearchParamList"),
                ("@UriSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.UriSearchParamList"),
                ("@NumberSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.NumberSearchParamList"),
                ("@QuantitySearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.QuantitySearchParamList"),
                ("@DateTimeSearchParms", SqlDbType.Structured, ParameterDirection.Input, "dbo.DateTimeSearchParamList"),
                ("@ReferenceTokenCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.ReferenceTokenCompositeSearchParamList"),
                ("@TokenTokenCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenTokenCompositeSearchParamList"),
                ("@TokenDateTimeCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenDateTimeCompositeSearchParamList"),
                ("@TokenQuantityCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenQuantityCompositeSearchParamList"),
                ("@TokenStringCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenStringCompositeSearchParamList"),
                ("@TokenNumberNumberCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenNumberNumberCompositeSearchParamList"),
            ]);
    }

    [Fact]
    public async Task GivenAReindexBatch_WhenWriterExecutes_ThenItUsesTheAtomicWrapperAndExistingTvpNames()
    {
        // Arrange
        var execution = new RecordingSqlExecutionService(outputValue: 3);
        var writer = new SqlResourceIndexWriter(execution);

        // Act
        int failed = await writer.ReindexAsync(
            42,
            new SqlResourceReindexRequest(CreateCompleteBatch()),
            CancellationToken.None);

        // Assert
        failed.ShouldBe(3);
        execution.DisableRetries.ShouldBeTrue();
        AssertCommand(
            execution.Command!,
            "dbo.UpdateResourceSearchParamsAndMaintainLastNGroups",
            [
                ("@FailedResources", SqlDbType.Int, ParameterDirection.Output, null),
                ("@Resources", SqlDbType.Structured, ParameterDirection.Input, "dbo.ResourceList"),
                ("@ResourceWriteClaims", SqlDbType.Structured, ParameterDirection.Input, "dbo.ResourceWriteClaimList"),
                ("@ReferenceSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.ReferenceSearchParamList"),
                ("@TokenSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenSearchParamList"),
                ("@TokenTexts", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenTextList"),
                ("@StringSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.StringSearchParamList"),
                ("@UriSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.UriSearchParamList"),
                ("@NumberSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.NumberSearchParamList"),
                ("@QuantitySearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.QuantitySearchParamList"),
                ("@DateTimeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.DateTimeSearchParamList"),
                ("@ReferenceTokenCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.ReferenceTokenCompositeSearchParamList"),
                ("@TokenTokenCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenTokenCompositeSearchParamList"),
                ("@TokenDateTimeCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenDateTimeCompositeSearchParamList"),
                ("@TokenQuantityCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenQuantityCompositeSearchParamList"),
                ("@TokenStringCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenStringCompositeSearchParamList"),
                ("@TokenNumberNumberCompositeSearchParams", SqlDbType.Structured, ParameterDirection.Input, "dbo.TokenNumberNumberCompositeSearchParamList"),
            ]);
    }

    [Fact]
    public async Task GivenAnEmptyBatch_WhenWriterExecutes_ThenAbsentStructuredParametersAreNull()
    {
        // Arrange
        var execution = new RecordingSqlExecutionService(outputValue: 0);
        var writer = new SqlResourceIndexWriter(execution);

        // Act
        await writer.ReindexAsync(
            42,
            new SqlResourceReindexRequest(new SqlResourceIndexBatch()),
            CancellationToken.None);

        // Assert
        foreach (SqlParameter parameter in execution.Command!.Parameters
                     .Cast<SqlParameter>()
                     .Where(parameter => parameter.SqlDbType == SqlDbType.Structured))
        {
            parameter.Value.ShouldBeNull();
        }
    }

    [Fact]
    public async Task GivenAHardDelete_WhenWriterExecutes_ThenItUsesTheAtomicWrapper()
    {
        // Arrange
        var execution = new RecordingSqlExecutionService(outputValue: 0);
        var writer = new SqlResourceIndexWriter(execution);
        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        await writer.HardDeleteAsync(
            42,
            7,
            "observation-1",
            keepCurrentVersion: false,
            isResourceChangeCaptureEnabled: true,
            cancellationTokenSource.Token);

        // Assert
        execution.DisableRetries.ShouldBeTrue();
        execution.CancellationToken.ShouldBe(cancellationTokenSource.Token);
        AssertCommand(
            execution.Command!,
            "dbo.HardDeleteResourceAndMaintainLastNGroups",
            [
                ("@ResourceTypeId", SqlDbType.SmallInt, ParameterDirection.Input, null),
                ("@ResourceId", SqlDbType.VarChar, ParameterDirection.Input, null),
                ("@KeepCurrentVersion", SqlDbType.Bit, ParameterDirection.Input, null),
                ("@IsResourceChangeCaptureEnabled", SqlDbType.Bit, ParameterDirection.Input, null),
            ]);
    }

    [Fact]
    public void GivenSchemaDeploymentServices_WhenRegistered_ThenExecutionAndWriterContractsHaveOneSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        // Act
        services.AddIgnixaSqlServerSchemaDeployment(configuration);

        // Assert
        AssertSingleSingleton<ISqlExecutionService, SqlExecutionService>(services);
        AssertSingleSingleton<ISqlResourceIndexWriter, SqlResourceIndexWriter>(services);
        AssertSingleSingleton<ILastNCodeGroupBackfillService, LastNCodeGroupBackfillService>(services);
    }

    private static void AssertCommand(
        SqlCommand command,
        string commandText,
        (string Name, SqlDbType Type, ParameterDirection Direction, string? TypeName)[] expectedParameters)
    {
        command.CommandText.ShouldBe(commandText);
        command.CommandType.ShouldBe(CommandType.StoredProcedure);

        SqlParameter[] actualParameters = command.Parameters.Cast<SqlParameter>().ToArray();
        actualParameters.Length.ShouldBe(expectedParameters.Length);
        for (int index = 0; index < expectedParameters.Length; index++)
        {
            SqlParameter actual = actualParameters[index];
            var expected = expectedParameters[index];
            actual.ParameterName.ShouldBe(expected.Name);
            actual.SqlDbType.ShouldBe(expected.Type);
            actual.Direction.ShouldBe(expected.Direction);
            actual.TypeName.ShouldBe(expected.TypeName ?? string.Empty);
        }
    }

    private static void AssertSingleSingleton<TService, TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor[] descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(TService)).ToArray();
        descriptors.Length.ShouldBe(1);
        descriptors[0].ImplementationType.ShouldBe(typeof(TImplementation));
        descriptors[0].Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    private static SqlResourceIndexBatch CreateCompleteBatch()
    {
        IReadOnlyList<SqlDataRecord> rows = [new SqlDataRecord(new SqlMetaData("Value", SqlDbType.Int))];
        return new SqlResourceIndexBatch(
            Resources: rows,
            ResourceWriteClaims: rows,
            ReferenceSearchParams: rows,
            TokenSearchParams: rows,
            TokenTexts: rows,
            StringSearchParams: rows,
            UriSearchParams: rows,
            NumberSearchParams: rows,
            QuantitySearchParams: rows,
            DateTimeSearchParams: rows,
            ReferenceTokenCompositeSearchParams: rows,
            TokenTokenCompositeSearchParams: rows,
            TokenDateTimeCompositeSearchParams: rows,
            TokenQuantityCompositeSearchParams: rows,
            TokenStringCompositeSearchParams: rows,
            TokenNumberNumberCompositeSearchParams: rows);
    }

    private sealed class RecordingSqlExecutionService(int outputValue) : ISqlExecutionService
    {
        public SqlCommand? Command { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public bool DisableRetries { get; private set; }

        public int TenantId { get; private set; }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            bool disableRetries = false)
        {
            TenantId = tenantId;
            Command = command;
            CancellationToken = cancellationToken;
            DisableRetries = disableRetries;
            SqlParameter? output = command.Parameters.Cast<SqlParameter>()
                .SingleOrDefault(parameter => parameter.Direction == ParameterDirection.Output);
            if (output is not null)
            {
                output.Value = outputValue;
            }

            return Task.FromResult(-1);
        }
    }
}
