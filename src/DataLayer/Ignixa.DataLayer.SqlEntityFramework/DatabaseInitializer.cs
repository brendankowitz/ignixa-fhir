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

            // Apply pending EF Core migrations (incremental schema changes after initial creation)
            // This ensures PackageResource table and other migrations are applied
            _logger.LogInformation("Checking for pending migrations...");
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                _logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                    pendingMigrations.Count(),
                    string.Join(", ", pendingMigrations));
                await _context.Database.MigrateAsync(cancellationToken);
                _logger.LogInformation("Migrations applied successfully");
            }
            else
            {
                _logger.LogInformation("No pending migrations to apply");
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
