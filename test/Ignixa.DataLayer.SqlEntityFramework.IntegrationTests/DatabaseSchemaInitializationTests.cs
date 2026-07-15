// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Confirms the compartment-search step 0 test database can be provisioned via <see cref="DatabaseInitializer"/>
/// (the same mechanism production code uses, see <c>SqlEntityFrameworkRepositoryFactory.cs</c>) rather than
/// hand-written DDL, and that the resulting schema is queryable and empty.
/// </summary>
public class DatabaseSchemaInitializationTests
{
    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING must be set to run this test (see docker-compose.test.yml).");
        }

        return connectionString;
    }

    [Fact]
    public async Task GivenTestDatabase_WhenInitialized_ThenSchemaAppliesAndReferenceSearchParamTableIsEmpty()
    {
        // Arrange
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new FhirDbContext(options);
        var initializer = new DatabaseInitializer(context, NullLogger<DatabaseInitializer>.Instance, "Development");

        // Act
        await initializer.InitializeAsync();
        var referenceSearchParamCount = await context.ReferenceSearchParams.CountAsync();

        // Assert
        referenceSearchParamCount.ShouldBe(0);
    }
}
