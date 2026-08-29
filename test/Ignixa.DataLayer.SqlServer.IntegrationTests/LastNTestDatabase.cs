using System.Data;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

internal sealed class LastNTestDatabase : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly string _baseConnectionString;

    private LastNTestDatabase(string databaseName, string baseConnectionString, SqlConnection connection)
    {
        _databaseName = databaseName;
        _baseConnectionString = baseConnectionString;
        Connection = connection;
    }

    public SqlConnection Connection { get; }

    public static async Task<LastNTestDatabase> CreateAndDeployAsync(CancellationToken cancellationToken = default)
    {
        string baseConnectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new SkipException(
                "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
        string databaseName = $"LastNTest_{Guid.NewGuid():N}";
        string connectionString = BuildConnectionString(baseConnectionString, databaseName);

        await CreateEmptyDatabaseAsync(baseConnectionString, databaseName, cancellationToken);

        try
        {
            SchemaDeployer deployer = new(
                new SingleTenantStore(connectionString),
                new FakeHostEnvironment(),
                Options.Create(new SqlServerOptions
                {
                    AutomaticSchemaDeploymentEnabled = true,
                    AllowIncompatiblePlatform = true,
                }),
                new ThrowingSchemaVersionResolver(),
                NullLogger<SchemaDeployer>.Instance);
            await deployer.DeployIfEmptyAsync(1, cancellationToken);

            SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken);
            return new LastNTestDatabase(databaseName, baseConnectionString, connection);
        }
        catch
        {
            await DropDatabaseAsync(baseConnectionString, databaseName, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ReadStringsAsync(
        string commandText,
        CancellationToken cancellationToken = default)
    {
        await using SqlCommand command = Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<string> values = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public Task<IReadOnlyList<string>> ReadColumnNamesAsync(
        string tableName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableAsync(
            """
            SELECT columnDefinition.name
            FROM sys.columns AS columnDefinition
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = columnDefinition.object_id
            WHERE tableDefinition.name = @tableName
            ORDER BY columnDefinition.column_id;
            """,
            tableName,
            cancellationToken);

    public async Task<string?> ReadColumnCollationAsync(
        string tableName,
        string columnName,
        CancellationToken cancellationToken = default)
    {
        await using SqlCommand command = Connection.CreateCommand();
        command.CommandText = """
            SELECT columnDefinition.collation_name
            FROM sys.columns AS columnDefinition
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = columnDefinition.object_id
            WHERE tableDefinition.name = @tableName AND columnDefinition.name = @columnName;
            """;
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = tableName;
        command.Parameters.Add("@columnName", SqlDbType.NVarChar, 128).Value = columnName;

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (string)result;
    }

    public Task<IReadOnlyList<string>> ReadPrimaryKeyColumnsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableAsync(
            """
            SELECT columnDefinition.name
            FROM sys.indexes AS indexDefinition
            INNER JOIN sys.index_columns AS indexColumnDefinition
                ON indexColumnDefinition.object_id = indexDefinition.object_id
                AND indexColumnDefinition.index_id = indexDefinition.index_id
            INNER JOIN sys.columns AS columnDefinition
                ON columnDefinition.object_id = indexColumnDefinition.object_id
                AND columnDefinition.column_id = indexColumnDefinition.column_id
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = indexDefinition.object_id
            WHERE tableDefinition.name = @tableName
                AND indexDefinition.is_primary_key = 1
            ORDER BY indexColumnDefinition.key_ordinal;
            """,
            tableName,
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadIndexNamesAsync(
        string tableName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableAsync(
            """
            SELECT indexDefinition.name
            FROM sys.indexes AS indexDefinition
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = indexDefinition.object_id
            WHERE tableDefinition.name = @tableName
                AND indexDefinition.name IS NOT NULL
                AND indexDefinition.is_primary_key = 0
            ORDER BY indexDefinition.name;
            """,
            tableName,
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadIndexColumnsAsync(
        string tableName,
        string indexName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableAndIndexAsync(
            """
            SELECT CASE WHEN indexColumnDefinition.is_included_column = 1
                THEN CONCAT('INCLUDE:', columnDefinition.name)
                ELSE columnDefinition.name
            END
            FROM sys.indexes AS indexDefinition
            INNER JOIN sys.index_columns AS indexColumnDefinition
                ON indexColumnDefinition.object_id = indexDefinition.object_id
                AND indexColumnDefinition.index_id = indexDefinition.index_id
            INNER JOIN sys.columns AS columnDefinition
                ON columnDefinition.object_id = indexColumnDefinition.object_id
                AND columnDefinition.column_id = indexColumnDefinition.column_id
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = indexDefinition.object_id
            WHERE tableDefinition.name = @tableName AND indexDefinition.name = @indexName
            ORDER BY indexColumnDefinition.is_included_column, indexColumnDefinition.key_ordinal,
                indexColumnDefinition.index_column_id;
            """,
            tableName,
            indexName,
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadForeignKeyNamesAsync(
        string tableName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableAsync(
            """
            SELECT foreignKeyDefinition.name
            FROM sys.foreign_keys AS foreignKeyDefinition
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = foreignKeyDefinition.parent_object_id
            WHERE tableDefinition.name = @tableName
            ORDER BY foreignKeyDefinition.name;
            """,
            tableName,
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadCheckDefinitionsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableAsync(
            """
            SELECT REPLACE(REPLACE(checkDefinition.definition, ' ', ''), '(', '')
            FROM sys.check_constraints AS checkDefinition
            INNER JOIN sys.tables AS tableDefinition ON tableDefinition.object_id = checkDefinition.parent_object_id
            WHERE tableDefinition.name = @tableName
            ORDER BY checkDefinition.name;
            """,
            tableName,
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadTableTypeColumnsAsync(
        string typeName,
        CancellationToken cancellationToken = default)
        => ReadStringsForTableTypeAsync(
            """
            SELECT columnDefinition.name
            FROM sys.table_types AS typeDefinition
            INNER JOIN sys.columns AS columnDefinition ON columnDefinition.object_id = typeDefinition.type_table_object_id
            WHERE typeDefinition.name = @typeName
            ORDER BY columnDefinition.column_id;
            """,
            typeName,
            cancellationToken);

    public async Task SeedResourceAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        string resourceId,
        int version,
        bool isHistory,
        bool isDeleted,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.Resource
                (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RawResource)
            VALUES
                (@resourceTypeId, @resourceId, @version, @isHistory, @resourceSurrogateId, @isDeleted, @rawResource);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@resourceId", SqlDbType.VarChar, 64).Value = resourceId;
        command.Parameters.Add("@version", SqlDbType.Int).Value = version;
        command.Parameters.Add("@isHistory", SqlDbType.Bit).Value = isHistory;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@isDeleted", SqlDbType.Bit).Value = isDeleted;
        command.Parameters.Add("@rawResource", SqlDbType.VarBinary, 1).Value = new byte[] { 1 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SeedTokenSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        short searchParamId,
        int? systemId,
        string code,
        string? codeOverflow,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.TokenSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            VALUES
                (@resourceTypeId, @resourceSurrogateId, @searchParamId, @systemId, @code, @codeOverflow);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = searchParamId;
        command.Parameters.Add("@systemId", SqlDbType.Int).Value = systemId is int value ? value : DBNull.Value;
        command.Parameters.Add("@code", SqlDbType.VarChar, 256).Value = code;
        command.Parameters.Add("@codeOverflow", SqlDbType.VarChar, -1).Value = codeOverflow ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SeedTokenTextAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        short searchParamId,
        string text,
        bool isHistory,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
            VALUES (@resourceTypeId, @resourceSurrogateId, @searchParamId, @text, @isHistory);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = searchParamId;
        command.Parameters.Add("@text", SqlDbType.NVarChar, 400).Value = text;
        command.Parameters.Add("@isHistory", SqlDbType.Bit).Value = isHistory;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SeedDateTimeSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        short searchParamId,
        DateTime startDateTime,
        DateTime endDateTime,
        bool isLongerThanADay,
        bool isMin,
        bool isMax,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.DateTimeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
            VALUES
                (@resourceTypeId, @resourceSurrogateId, @searchParamId, @startDateTime, @endDateTime, @isLongerThanADay, @isMin, @isMax);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@resourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = searchParamId;
        command.Parameters.Add("@startDateTime", SqlDbType.DateTime2).Value = startDateTime;
        command.Parameters.Add("@endDateTime", SqlDbType.DateTime2).Value = endDateTime;
        command.Parameters.Add("@isLongerThanADay", SqlDbType.Bit).Value = isLongerThanADay;
        command.Parameters.Add("@isMin", SqlDbType.Bit).Value = isMin;
        command.Parameters.Add("@isMax", SqlDbType.Bit).Value = isMax;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ExecuteStoredProcedureAsync(
        string procedureName,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = procedureName;
#pragma warning restore CA2100
        command.CommandType = CommandType.StoredProcedure;
        foreach (SqlParameter parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        await DropDatabaseAsync(_baseConnectionString, _databaseName, CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> ReadStringsForTableAsync(
        string commandText,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = tableName;
        return await ReadStringsAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ReadStringsForTableAndIndexAsync(
        string commandText,
        string tableName,
        string indexName,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 128).Value = tableName;
        command.Parameters.Add("@indexName", SqlDbType.NVarChar, 128).Value = indexName;
        return await ReadStringsAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ReadStringsForTableTypeAsync(
        string commandText,
        string typeName,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        command.Parameters.Add("@typeName", SqlDbType.NVarChar, 128).Value = typeName;
        return await ReadStringsAsync(command, cancellationToken);
    }

    private static string BuildConnectionString(string baseConnectionString, string databaseName)
    {
        SqlConnectionStringBuilder builder = new(baseConnectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task CreateEmptyDatabaseAsync(
        string baseConnectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(BuildConnectionString(baseConnectionString, "master"));
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(BuildConnectionString(baseConnectionString, "master"));
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"""
            IF DB_ID('{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> values = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed class SingleTenantStore(string connectionString) : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new()
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
        };

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken cancellationToken = default)
            => new(tenantId == 1 ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<TenantConfiguration>)[_tenant]);

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ThrowingSchemaVersionResolver : ISchemaVersionResolver
    {
        public Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Not expected to be called by DeployIfEmptyAsync.");
    }
}
