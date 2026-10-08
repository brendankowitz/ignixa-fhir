// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Creates many tagged Patients in one request (a FHIR batch <c>Bundle</c>) for bulk-delete tests that
/// need enough resources to exercise multiple <c>BulkDelete:BatchSize=1</c> batches (cancellation, and
/// the ContinueAsNew boundary), without the overhead of one HTTP round trip per resource.
/// </summary>
internal static class BulkDeletePatientBatchFactory
{
    public static async Task<List<string>> PutPatientsAsync(HttpClient client, string tag, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString("N")).ToList();
        var entries = new JsonArray();
        foreach (var id in ids)
        {
            entries.Add(new JsonObject
            {
                ["resource"] = new JsonObject
                {
                    ["resourceType"] = "Patient",
                    ["id"] = id,
                    ["meta"] = new JsonObject { ["tag"] = new JsonArray(new JsonObject { ["code"] = tag }) },
                },
                ["request"] = new JsonObject { ["method"] = "PUT", ["url"] = $"Patient/{id}" },
            });
        }

        var bundle = new JsonObject { ["resourceType"] = "Bundle", ["type"] = "batch", ["entry"] = entries };
        using var content = new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/fhir+json");
        using var response = await client.PostAsync("/tenant/1/", content);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var responseBundle = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        foreach (var entry in responseBundle["entry"]!.AsArray())
        {
            var status = entry!["response"]!["status"]!.GetValue<string>();
            status.StartsWith("201", StringComparison.Ordinal).ShouldBeTrue($"Batch entry failed: {entry}");
        }

        return ids;
    }
}
