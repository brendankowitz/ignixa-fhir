// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Confirms a bulk-delete job survives a Durable Task ContinueAsNew: with <c>BulkDelete:BatchSize=1</c>,
/// deleting 105 tagged Patients takes 105 batches, crossing
/// <see cref="Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations.BulkDeleteOrchestration.BatchesPerExecution"/>
/// (100) exactly once. The orchestration's instance ID is unchanged across the continuation, so a single
/// status poll loop against the original job ID must still observe completion with the full count. Based
/// on <see cref="BulkDeleteApiFixture"/> (SqlServer DurableTask backend): this scenario needs the
/// orchestration to actually resume across 105 scheduled activities, which the shared fixture's default
/// FileSystem provider cannot do at all -- see that fixture's remarks for the underlying defect this
/// works around.
/// </summary>
[Collection(BulkDeleteTestCollection.Name)]
public class BulkDeleteContinueAsNewTests(BulkDeleteApiFixture fixture, ITestOutputHelper output)
{
    private const int PatientCount = 105;

    [Fact]
    public async Task GivenMoreResourcesThanOneOrchestrationExecutionsBatchLimit_WhenHardDeleting_ThenTheJobCompletesWithTheFullCount()
    {
        await using var application = fixture.WithWebHostBuilder(webHost =>
            webHost.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BulkDelete:BatchSize"] = "1",
                })));
        using var client = application.CreateClient();
        var bulkDelete = new BulkDeleteClient(client);

        var tag = Guid.NewGuid().ToString("N");
        var ids = await BulkDeletePatientBatchFactory.PutPatientsAsync(client, tag, PatientCount);

        using var kickoff = await bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}&_hardDelete=true");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = kickoff.Content.Headers.GetValues("Content-Location").Single();

        var stopwatch = Stopwatch.StartNew();
        var (statusCode, body, _) = await bulkDelete.PollToCompletionAsync(location, timeout: TimeSpan.FromMinutes(5));
        stopwatch.Stop();
        output.WriteLine($"ContinueAsNew bulk-delete of {PatientCount} resources (BatchSize=1, 105 batches, " +
            "crossing the 100-batch ContinueAsNew boundary once) completed in " +
            $"{stopwatch.Elapsed.TotalSeconds:F1}s.");
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(60))
        {
            output.WriteLine(
                $"NOTE: exceeded the 60s guideline ({stopwatch.Elapsed.TotalSeconds:F1}s) -- reported per task brief, test kept.");
        }

        statusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        BulkDeleteClient.GetCounts(body)["Patient"].ShouldBe(PatientCount);

        foreach (var id in ids)
        {
            (await client.GetAsync($"/tenant/1/Patient/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}
