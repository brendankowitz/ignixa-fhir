// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement;

/// <summary>
/// Factory for creating DbContext instances for package repository operations.
/// CRITICAL: Creates a NEW DbContext for EACH operation to avoid threading issues.
/// DbContext is not thread-safe - even with InstancePerDependency, concurrent
/// operations from multiple threads can cause "A second operation was started on this context
/// instance before a previous operation completed" errors.
/// This factory ensures true isolation by creating fresh DbContext per operation.
/// </summary>
public class PackageRepositoryDbContextFactory : IDbContextFactory<FhirDbContext>
{
    private readonly string _connectionString;
    private readonly ILoggerFactory _loggerFactory;

    public PackageRepositoryDbContextFactory(string connectionString, ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        _connectionString = connectionString;
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Creates a fresh DbContext instance for an operation.
    /// Must be used with 'using' to ensure proper disposal after operation.
    /// </summary>
    public FhirDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<FhirDbContext>();
        optionsBuilder.UseSqlServer(
            _connectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                sqlOptions.CommandTimeout(30);
            });

        return new FhirDbContext(optionsBuilder.Options);
    }
}
