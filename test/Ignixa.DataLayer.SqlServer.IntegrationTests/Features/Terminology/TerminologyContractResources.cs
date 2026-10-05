using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

internal static class TerminologyContractResources
{
    public static async Task<PackageResource> StoreAsync(
        TerminologyTestFixture database, string resourceType, string canonical, string? version, string json)
    {
        var repository = new SqlServerPackageResourceRepository(
            database.SqlExecutionService, 1, NullLogger<SqlServerPackageResourceRepository>.Instance);
        var packageId = $"terminology.contract.{Guid.NewGuid():N}";
        await repository.UpsertAsync(new PackageResource
        {
            PackageId = packageId, PackageVersion = "1.0.0", ResourceType = resourceType,
            ResourceId = Guid.NewGuid().ToString("N"), Canonical = canonical, Version = version,
            ResourceJson = json, FhirVersion = "4.0.1", IsActive = true,
        }, CancellationToken.None);
        return (await repository.GetFromPackageAsync(packageId, "1.0.0", canonical))!;
    }

    public static string CodeSystem(string canonical, string? version, string display = "Car", bool caseSensitive = true)
    {
        var resource = JsonNode.Parse(TerminologyTestFixture.HierarchicalCodeSystemJson(canonical))!;
        SetVersion(resource, version);
        resource["caseSensitive"] = caseSensitive;
        resource["concept"]![0]!["concept"]![0]!["display"] = display;
        return resource.ToJsonString();
    }

    public static string ValueSet(string canonical, string? version, string system, string code = "car", string display = "Car")
    {
        var resource = JsonNode.Parse(TerminologyTestFixture.ExpandedValueSetJson(canonical, system, code))!;
        SetVersion(resource, version);
        resource["expansion"]!["contains"]![0]!["display"] = display;
        return resource.ToJsonString();
    }

    public static string ConceptMap(string canonical, string? version, string targetCode)
    {
        var resource = JsonNode.Parse(TerminologyTestFixture.ConceptMapJson(
            canonical, "http://terminology-contract.example/source", "http://terminology-contract.example/target"))!;
        SetVersion(resource, version);
        resource["group"]![0]!["element"]![0]!["target"]![0]!["code"] = targetCode;
        return resource.ToJsonString();
    }

    private static void SetVersion(JsonNode resource, string? version)
    {
        if (version is null)
        {
            resource.AsObject().Remove("version");
        }
        else
        {
            resource["version"] = version;
        }
    }
}
