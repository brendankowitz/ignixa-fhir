// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Extensions;

public class ServiceCollectionExtensionsValidateVectorSearchDataLayerTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void GivenVectorSearchDisabled_WhenValidating_ThenDoesNotThrowEvenWithNonSqlTenant()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "false",
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Storage:Type"] = "FileSystem",
        });

        Should.NotThrow(() => ServiceCollectionExtensions.ValidateVectorSearchDataLayer(configuration));
    }

    [Fact]
    public void GivenVectorSearchEnabledAndAllTenantsOnSql_WhenValidating_ThenDoesNotThrow()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
        });

        Should.NotThrow(() => ServiceCollectionExtensions.ValidateVectorSearchDataLayer(configuration));
    }

    [Fact]
    public void GivenVectorSearchEnabledAndAnActiveTenantOnFileSystem_WhenValidating_ThenThrows()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Storage:Type"] = "FileSystem",
        });

        var exception = Should.Throw<OptionsValidationException>(() =>
            ServiceCollectionExtensions.ValidateVectorSearchDataLayer(configuration));

        exception.Failures.ShouldContain("VectorSearch requires the SQL Server data layer.");
    }

    [Fact]
    public void GivenVectorSearchEnabledAndAnInactiveTenantOnFileSystem_WhenValidating_ThenDoesNotThrow()
    {
        var configuration = Configuration(new()
        {
            ["VectorSearch:Enabled"] = "true",
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
            ["Tenants:Configurations:1:TenantId"] = "2",
            ["Tenants:Configurations:1:DisplayName"] = "Retired",
            ["Tenants:Configurations:1:FhirVersion"] = "4.0",
            ["Tenants:Configurations:1:IsActive"] = "false",
            ["Tenants:Configurations:1:Storage:Type"] = "FileSystem",
        });

        Should.NotThrow(() => ServiceCollectionExtensions.ValidateVectorSearchDataLayer(configuration));
    }
}
