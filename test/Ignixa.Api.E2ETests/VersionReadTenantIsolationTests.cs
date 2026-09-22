using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Ignixa.Api.E2ETests;

[Collection(E2ETestCollection.Name)]
public class VersionReadTenantIsolationTests(IgnixaApiFixture fixture)
{
    private static readonly int[] Tenants = [1, 2];
    [Fact]
    public async Task GivenTwoTenantsWithTheSameResourceId_WhenVread_ThenHistoryIsIsolatedAndUnqualifiedRoutesAreRejected()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Use the shared validation wrapper.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = $"IgnixaVreadTenant_{Guid.NewGuid():N}";
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
                        ["Tenants:Configurations:2:DisplayName"] = "Version-read isolation tenant",
                        ["Tenants:Configurations:2:IsActive"] = "true",
                        ["Tenants:Configurations:2:FhirVersion"] = "4.0",
                        ["Tenants:Configurations:2:Storage:Type"] = "SqlServer",
                        ["Tenants:Configurations:2:Storage:ConnectionString"] = tenantConnectionString,
                        ["Tenants:Configurations:2:Packages:EnableAutoLoad"] = "false",
                        ["Tenants:Configurations:2:Storage:InheritConnectionStringFromTenant"] = null
                    })));
            using var client = application.CreateClient();
            var id = Guid.NewGuid().ToString();
            foreach (var tenant in Tenants)
            {
                using var first = await client.PutAsync($"/tenant/{tenant}/Patient/{id}", new StringContent(
                    $$"""{"resourceType":"Patient","id":"{{id}}","name":[{"family":"tenant-{{tenant}}"}]}""",
                    Encoding.UTF8, "application/fhir+json"));
                first.StatusCode.ShouldBe(HttpStatusCode.Created,
                    $"Tenant {tenant}: {await first.Content.ReadAsStringAsync()}");
                using var second = await client.PutAsync($"/tenant/{tenant}/Patient/{id}", new StringContent(
                    $$"""{"resourceType":"Patient","id":"{{id}}","name":[{"family":"updated"}]}""",
                    Encoding.UTF8, "application/fhir+json"));
                second.StatusCode.ShouldBe(HttpStatusCode.OK,
                    $"Tenant {tenant}: {await second.Content.ReadAsStringAsync()}");
            }
            foreach (var tenant in Tenants)
            {
                using var response = await client.GetAsync($"/tenant/{tenant}/Patient/{id}/_history/1");
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.Headers.ETag!.ToString().ShouldBe("W/\"1\"");
                JsonNode.Parse(await response.Content.ReadAsStringAsync())!["name"]![0]!["family"]!
                    .GetValue<string>().ShouldBe($"tenant-{tenant}");
            }
            (await client.GetAsync($"/Patient/{id}/_history/1")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await client.GetAsync($"/tenant/0/Patient/{id}/_history/1")).IsSuccessStatusCode.ShouldBeFalse();
        }
        finally
        {
            using var tenantConnection = new SqlConnection(tenantConnectionString);
            SqlConnection.ClearPool(tenantConnection);
#pragma warning disable CA2100
            using var drop = new SqlCommand($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]", master);
#pragma warning restore CA2100
            await drop.ExecuteNonQueryAsync();
        }
    }

}
