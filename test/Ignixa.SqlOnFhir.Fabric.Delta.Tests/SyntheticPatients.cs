// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>Invented Patient resources for <see cref="WriterContracts.PatientSchema"/>.</summary>
internal static class SyntheticPatients
{
    /// <summary>A Patient with <c>meta.lastUpdated</c> (omitted when null) and a family name, customized by <paramref name="customize"/>.</summary>
    public static ResourceJsonNode Patient(string id, string? lastUpdated, string family = "Doe", Action<JsonObject>? customize = null)
    {
        var resource = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = id,
            ["name"] = new JsonArray(new JsonObject { ["family"] = family, ["given"] = new JsonArray("Jo") }),
        };

        if (lastUpdated is not null)
        {
            resource["meta"] = new JsonObject { ["versionId"] = "1", ["lastUpdated"] = lastUpdated };
        }

        customize?.Invoke(resource);
        return ResourceJsonNode.Parse(resource.ToJsonString());
    }

    /// <summary>Creates <paramref name="table"/> and commits <paramref name="resources"/> as version 1 from the first file.</summary>
    public static async Task<FabricSilverContract> SeedAsync(string table, params ResourceJsonNode[] resources)
    {
        var contract = await WriterContracts.PatientAsync();
        var seeded = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, resources)], WriterContracts.Ingestion(), CancellationToken.None);
        seeded.Tables[0].CommittedVersion.ShouldBe(1);
        return contract;
    }

    /// <summary>The single row of <paramref name="rows"/> holding the Patient with local id <paramref name="id"/>.</summary>
    public static JsonObject RowOf(IEnumerable<JsonObject> rows, string id, string sourceNamespace = WriterContracts.Namespace)
    {
        var silverId = SilverId("Patient", id, sourceNamespace);
        return rows.Single(row => (string?)row["id"] == silverId);
    }

    /// <summary>The family name of a Patient row's first name.</summary>
    public static string? FamilyOf(JsonObject row) => (string?)row["name"]![0]!["family"];

    /// <summary>The Silver id rule, written independently of the library: SHA-256 hex of namespace/type/id.</summary>
    public static string SilverId(string resourceType, string id, string sourceNamespace = WriterContracts.Namespace) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{new Uri(sourceNamespace).Host}{new Uri(sourceNamespace).AbsolutePath}/{resourceType}/{id}")));
}
