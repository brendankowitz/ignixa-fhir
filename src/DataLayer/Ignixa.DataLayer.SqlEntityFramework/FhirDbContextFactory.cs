// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Design-time DbContext factory for Entity Framework Core migrations.
/// Used by dotnet ef migrations add/update commands.
/// </summary>
public class FhirDbContextFactory : IDesignTimeDbContextFactory<FhirDbContext>
{
    public FhirDbContext CreateDbContext(string[] args)
    {
        // Default connection string for local development
        // Override with FHIR_CONNECTION_STRING environment variable or via command-line
        var connectionString = Environment.GetEnvironmentVariable("FHIR_CONNECTION_STRING")
            ?? "server=(local);Initial Catalog=FHIR_R4;Integrated Security=true;TrustServerCertificate=true";

        var optionsBuilder = new DbContextOptionsBuilder<FhirDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new FhirDbContext(optionsBuilder.Options);
    }
}
