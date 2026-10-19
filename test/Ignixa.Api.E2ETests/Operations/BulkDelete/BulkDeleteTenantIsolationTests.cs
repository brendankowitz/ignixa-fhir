// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Bulk-delete tenant isolation (scenario 14), following the two-tenant pattern in
/// <see cref="VersionReadTenantIsolationTests"/>: a second, independently-databased tenant is spun up
/// via <see cref="IgnixaApiFixture.WithWebHostBuilder"/> so tenant 1's job and tenant 2's resources
/// cannot be confused with each other through a repository bug. Based on <see cref="BulkDeleteApiFixture"/>
/// (SqlServer DurableTask backend -- see its remarks) so the kicked-off job actually reaches a terminal
/// state.
/// </summary>
[Collection(BulkDeleteTestCollection.Name)]
public class BulkDeleteTenantIsolationTests(BulkDeleteApiFixture fixture)
{
    [Fact]
    public async Task GivenTwoTenantsWithTheSameTag_WhenBulkDeletingTenant1_ThenTenant2IsUntouchedAndCrossTenantAccessIsRejected()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Use the shared validation wrapper.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = $"IgnixaBulkDeleteTenant_{Guid.NewGuid():N}";
        builder.InitialCatalog = databaseName;
        var tenantConnectionString = builder.ConnectionString;
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
#pragma warning disable CA2100
        using (var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", master))
#pragma warning restore CA2100
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using var application = fixture.WithWebHostBuilder(webHost =>
                webHost.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Tenants:Configurations:2:TenantId"] = "2",
                        ["Tenants:Configurations:2:DisplayName"] = "Bulk-delete isolation tenant",
                        ["Tenants:Configurations:2:IsActive"] = "true",
                        ["Tenants:Configurations:2:FhirVersion"] = "4.0",
                        ["Tenants:Configurations:2:Storage:Type"] = "SqlServer",
                        ["Tenants:Configurations:2:Storage:ConnectionString"] = tenantConnectionString,
                        ["Tenants:Configurations:2:Packages:EnableAutoLoad"] = "false",
                        ["Tenants:Configurations:2:Storage:InheritConnectionStringFromTenant"] = null,
                    })));
            using var client = application.CreateClient();
            var bulkDelete = new BulkDeleteClient(client);
            var tag = Guid.NewGuid().ToString("N");
            var tenant1Id = await PutPatientAsync(client, tenantId: 1, tag);
            var tenant2Id = await PutPatientAsync(client, tenantId: 2, tag);

            using var kickoff = await bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}&_hardDelete=true");
            kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
            var location = kickoff.Content.Headers.GetValues("Content-Location").Single();
            location.ShouldContain("/tenant/1/_operations/bulk-delete/");
            var jobId = location.Split('/').Last();

            var (statusCode, body, _) = await bulkDelete.PollToCompletionAsync(location);
            statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
            BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(1);

            (await client.GetAsync($"/tenant/1/Patient/{tenant1Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            using var tenant2Read = await client.GetAsync($"/tenant/2/Patient/{tenant2Id}");
            tenant2Read.StatusCode.ShouldBe(HttpStatusCode.OK, await tenant2Read.Content.ReadAsStringAsync());

            // Tenant 1's job is invisible through tenant 2's routes.
            (await client.GetAsync($"/tenant/2/_operations/bulk-delete/{jobId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            using var crossTenantCancel = await client.DeleteAsync($"/tenant/2/_operations/bulk-delete/{jobId}");
            crossTenantCancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            // Tenant 0 is the reserved system partition.
            using var tenantZero = await bulkDelete.KickoffAsync($"/tenant/0/$bulk-delete?_tag={tag}");
            tenantZero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            // The agnostic route cannot auto-detect a tenant once more than one is configured.
            using var agnostic = await bulkDelete.KickoffAsync($"/$bulk-delete?_tag={tag}");
            agnostic.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            using var tenantConnection = new SqlConnection(tenantConnectionString);
            SqlConnection.ClearPool(tenantConnection);
#pragma warning disable CA2100
            using var drop = new SqlCommand(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", master);
#pragma warning restore CA2100
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> PutPatientAsync(HttpClient client, int tenantId, string tag)
    {
        var id = Guid.NewGuid().ToString("N");
        var resource = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = id,
            ["meta"] = new JsonObject { ["tag"] = new JsonArray(new JsonObject { ["code"] = tag }) },
        };
        using var content = new StringContent(resource.ToJsonString(), Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/{tenantId}/Patient/{id}", content);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return id;
    }
}
