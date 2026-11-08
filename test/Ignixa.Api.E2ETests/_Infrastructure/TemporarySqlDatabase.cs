using System.Data;
using Microsoft.Data.SqlClient;

namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>
/// An empty database on the <c>TEST_SQL_CONNECTION_STRING</c> server, dropped on dispose. Dispose it after every
/// host that uses it, so no pooled connection keeps it open.
/// </summary>
internal sealed class TemporarySqlDatabase : IAsyncDisposable
{
    private readonly string _master;
    private readonly string _quotedName;

    private TemporarySqlDatabase(string connectionString, string master, string quotedName)
    {
        ConnectionString = connectionString;
        _master = master;
        _quotedName = quotedName;
    }

    public string ConnectionString { get; }

    public static async Task<TemporarySqlDatabase> CreateAsync(string namePrefix)
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var name = $"{namePrefix}_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = name }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedName = names.QuoteIdentifier(name);
        await ExecuteAsync(master, $"CREATE DATABASE {quotedName}");
        return new TemporarySqlDatabase(connectionString, master, quotedName);
    }

    public async ValueTask DisposeAsync()
    {
        using var pool = new SqlConnection(ConnectionString);
        SqlConnection.ClearPool(pool);
        await ExecuteAsync(_master, $"DROP DATABASE {_quotedName}");
    }

    private static async Task ExecuteAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }
}
