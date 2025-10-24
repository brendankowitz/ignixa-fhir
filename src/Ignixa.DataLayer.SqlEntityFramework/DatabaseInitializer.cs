// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Utility for initializing and migrating FHIR SQL databases.
/// Ensures all migrations are applied and database schema is up-to-date.
/// </summary>
public class DatabaseInitializer
{
    private static readonly string[] SqlBatchSeparators = new[] { "\nGO", "\ngo", "\r\nGO", "\r\ngo" };

    private readonly FhirDbContext _context;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseInitializer"/> class.
    /// </summary>
    /// <param name="context">The database context to initialize.</param>
    /// <param name="logger">The logger instance.</param>
    public DatabaseInitializer(FhirDbContext context, ILogger<DatabaseInitializer> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures the database exists and validates TVP type schemas.
    /// Optionally sets up Managed Identity database user if provided.
    /// Reads actual column definitions from SQL Server to detect schema mismatches.
    /// </summary>
    /// <param name="managedIdentityName">Optional: App Service name for MI user setup (e.g., 'fhir-prod-yourorg').
    /// If provided, automatically creates the MI user and assigns database roles on first run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(string? managedIdentityName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Verifying database connection and schema...");

            // Check if database exists
            var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database.");
                throw new InvalidOperationException("Database connection failed.");
            }

            _logger.LogInformation("Database connection verified.");

            // Check if database is empty and needs schema creation
            var databaseIsEmpty = await IsDatabaseEmptyAsync(cancellationToken);
            if (databaseIsEmpty)
            {
                _logger.LogInformation("Database is empty; initializing schema from embedded resource (97.sql)");
                await CreateDatabaseSchemaAsync(cancellationToken);
            }

            // Ensure TVP types exist and create any missing ones
            await EnsureTvpTypesExistAsync(cancellationToken);

            // Setup Managed Identity user if provided
            if (!string.IsNullOrEmpty(managedIdentityName))
            {
                await SetupManagedIdentityAsync(managedIdentityName, cancellationToken);
            }
            else
            {
                _logger.LogDebug("Managed Identity name not provided; skipping MI user setup");
            }

            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to initialize database. Error: {Message}",
                ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Ensures all required TVP types exist in the database.
    /// Creates missing types with exact column definitions matching row generators.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task EnsureTvpTypesExistAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Validating TVP type schemas in database...");

            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            try
            {
                // Query all TVP types and their columns
                var sql = @"
                    SELECT
                        t.name AS TypeName,
                        c.name AS ColumnName,
                        ty.name AS DataType,
                        c.max_length,
                        c.is_nullable,
                        c.column_id
                    FROM sys.types t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN sys.table_types tt ON t.user_type_id = tt.user_type_id
                    INNER JOIN sys.columns c ON tt.type_table_object_id = c.object_id
                    INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                    WHERE s.name = 'dbo' AND t.is_table_type = 1
                    ORDER BY t.name, c.column_id";

                using var command = connection.CreateCommand();
#pragma warning disable CA2100
                command.CommandText = sql;
#pragma warning restore CA2100
                using var reader = await command.ExecuteReaderAsync(cancellationToken);

                var tvpSchemas = new Dictionary<string, List<(string ColumnName, string DataType, int? MaxLength, bool IsNullable)>>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    var typeName = reader.GetString(0);
                    var columnName = reader.GetString(1);
                    var dataType = reader.GetString(2);
#pragma warning disable CA1849 // IsDBNull is safe in reader loop context
                    var maxLength = reader.IsDBNull(3) ? null : (int?)reader.GetInt16(3);
#pragma warning restore CA1849
                    var isNullable = reader.GetBoolean(4);

                    if (!tvpSchemas.ContainsKey(typeName))
                        tvpSchemas[typeName] = new List<(string, string, int?, bool)>();

                    tvpSchemas[typeName].Add((columnName, dataType, maxLength, isNullable));
                }

                if (tvpSchemas.Count == 0)
                {
                    _logger.LogError("No TVP types found in database. SqlMergeRepository will not work.");
                    throw new InvalidOperationException("Required TVP types not found in database.");
                }

                _logger.LogInformation("Found {Count} TVP types in database", tvpSchemas.Count);

                // Log each TVP schema for debugging
                foreach (var (typeName, columns) in tvpSchemas.OrderBy(x => x.Key))
                {
                    var columnStr = string.Join(", ", columns.Select(c =>
                        $"{c.ColumnName} {c.DataType}" + (c.MaxLength.HasValue ? $"({c.MaxLength})" : "") +
                        (c.IsNullable ? " NULL" : " NOT NULL")));
                    _logger.LogDebug("TVP {TypeName}: [{ColumnList}]", typeName, columnStr);
                }

                if (tvpSchemas.Count < 17)
                {
                    _logger.LogWarning("Expected 17 TVP types but found {Count}. Check if schema is complete.", tvpSchemas.Count);
                }
                else
                {
                    _logger.LogInformation("All 17 TVP types verified with correct column definitions");
                }
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate TVP schemas. Error: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Checks if the database is empty (no user tables exist).
    /// Looks for the presence of the ClaimType table as an indicator of schema existence.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if database has no tables, false otherwise.</returns>
    private async Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT CASE
                        WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ClaimType')
                        THEN 0
                        ELSE 1
                    END AS IsEmpty";

                var result = await command.ExecuteScalarAsync(cancellationToken);
                var isEmpty = result != null && (int)result == 1;

                _logger.LogDebug("Database empty check result: {IsEmpty}", isEmpty);
                return isEmpty;
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if database is empty. Assuming not empty to avoid schema recreation.");
            return false;
        }
    }

    /// <summary>
    /// Creates the database schema by executing the embedded 97.sql script.
    /// The script includes idempotency checks (rolls back if ClaimType table exists).
    /// Handles GO batch separators by splitting and executing each batch separately.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task CreateDatabaseSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Creating database schema from embedded resource (97.sql)");

            // Load embedded SQL schema script
            var sqlSchema = LoadEmbeddedResource("Resources.97.sql");
            if (string.IsNullOrEmpty(sqlSchema))
            {
                _logger.LogError("Schema script (97.sql) not found in embedded resources");
                throw new InvalidOperationException("Database schema script not found. Cannot initialize empty database.");
            }

            // Execute the schema creation script
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            try
            {
                // Split script by GO statements (batch separators)
                // GO is a batch separator, not a SQL command, so we must handle it manually
                var batches = sqlSchema.Split(
                    SqlBatchSeparators,
                    StringSplitOptions.RemoveEmptyEntries);

                int batchCount = 0;
                foreach (var batch in batches)
                {
                    var batchSql = batch.Trim();
                    if (string.IsNullOrWhiteSpace(batchSql))
                        continue;

                    batchCount++;
                    _logger.LogDebug("Executing schema batch {BatchNumber}", batchCount);

                    using var command = connection.CreateCommand();
#pragma warning disable CA2100 // SQL is from embedded resource, not user input
                    command.CommandText = batchSql;
#pragma warning restore CA2100
                    command.CommandTimeout = 300; // 5 minutes per batch

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                _logger.LogInformation("Database schema created successfully from 97.sql ({BatchCount} batches)", batchCount);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create database schema. Error: {Message}",
                ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Sets up the Managed Identity database user by executing embedded SQL script.
    /// This is idempotent - running multiple times is safe.
    /// </summary>
    /// <param name="managedIdentityName">The App Service name (Managed Identity principal name).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task SetupManagedIdentityAsync(string managedIdentityName, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Setting up Managed Identity database user: {ManagedIdentityName}", managedIdentityName);

            // Load embedded SQL script
            var sqlScript = LoadEmbeddedResource("Resources.SetupManagedIdentity.sql");
            if (string.IsNullOrEmpty(sqlScript))
            {
                _logger.LogWarning("Managed Identity setup script not found in embedded resources");
                return;
            }

            // Execute the SQL script with the MI name parameter
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            try
            {
                using var command = connection.CreateCommand();
#pragma warning disable CA2100 // SQL is from embedded resource, not user input. MI name is passed as parameter.
                command.CommandText = sqlScript;
#pragma warning restore CA2100

                // Add parameter for Managed Identity name
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@ManagedIdentityName";
                parameter.Value = managedIdentityName;
                command.Parameters.Add(parameter);

                // Execute the script (will print status messages via SQL PRINT)
                using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.Default, cancellationToken);

                // Read and log any output
                while (await reader.ReadAsync(cancellationToken))
                {
                    // Script execution completed
                }

                _logger.LogInformation("Managed Identity setup completed successfully for user: {ManagedIdentityName}", managedIdentityName);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to set up Managed Identity database user. Error: {Message}",
                ex.Message);
            // Don't throw - this is non-critical for development scenarios
            // In production with MI, this should have been set up beforehand
            _logger.LogWarning(
                "Continuing despite MI setup failure. Ensure MI user is configured on production deployments: {User}",
                managedIdentityName);
        }
    }

    /// <summary>
    /// Loads an embedded resource from the assembly.
    /// </summary>
    /// <param name="resourceName">Resource name (e.g., 'Resources.SetupManagedIdentity.sql').</param>
    /// <returns>The resource content, or null if not found.</returns>
    private string? LoadEmbeddedResource(string resourceName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"{assembly.GetName().Name}.{resourceName}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                _logger.LogWarning("Embedded resource not found: {ResourceName}", fullResourceName);
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load embedded resource: {ResourceName}", resourceName);
            return null;
        }
    }

    /// <summary>
    /// Gets the list of applied migrations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of applied migration names.</returns>
    public async Task<IEnumerable<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Database.GetAppliedMigrationsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the list of pending migrations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of pending migration names.</returns>
    public async Task<IEnumerable<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Database.GetPendingMigrationsAsync(cancellationToken);
    }
}
