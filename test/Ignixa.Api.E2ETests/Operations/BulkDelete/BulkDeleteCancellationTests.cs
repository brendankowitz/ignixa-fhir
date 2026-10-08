// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Covers bulk-delete cancellation (scenario 12) and, by recording every intermediate poll along the
/// way, polling (scenario 11): each poll before cancellation completes must be 202 (the job is a soft
/// delete over 40 resources with <c>BatchSize=1</c>, so it cannot finish before the cancel lands except
/// in a rare race, handled below), and the final poll must be 200 regardless of which path is taken.
/// Based on <see cref="BulkDeleteApiFixture"/> (SqlServer DurableTask backend) for consistency with the
/// rest of this suite, even though cancellation itself does not depend on the orchestration resuming
/// (the job repository is marked Cancelled directly) -- see that fixture's remarks.
/// </summary>
[Collection(BulkDeleteTestCollection.Name)]
public class BulkDeleteCancellationTests(BulkDeleteApiFixture fixture)
{
    [Fact]
    public async Task GivenASlowSoftDelete_WhenCancelledImmediately_ThenItStopsAndReportsJobCanceled()
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
        await BulkDeletePatientBatchFactory.PutPatientsAsync(client, tag, 40);

        using var kickoff = await bulkDelete.KickoffAsync($"/tenant/1/Patient/$bulk-delete?_tag={tag}");
        kickoff.StatusCode.ShouldBe(HttpStatusCode.Accepted, await kickoff.Content.ReadAsStringAsync());
        var location = kickoff.Content.Headers.GetValues("Content-Location").Single();

        using var firstCancel = await client.DeleteAsync(location);

        if (firstCancel.StatusCode == HttpStatusCode.Conflict)
        {
            // The job reached a terminal state before the cancel request landed -- rare with
            // BatchSize=1 across 40 resources, but not impossible. Confirm it actually completed and
            // stop here rather than asserting cancellation behavior that did not occur: that would
            // make the test flaky instead of robust to the race.
            var (statusCode, _, _) = await bulkDelete.PollToCompletionAsync(location);
            statusCode.ShouldBe(HttpStatusCode.OK);
            return;
        }

        firstCancel.StatusCode.ShouldBe(HttpStatusCode.Accepted, await firstCancel.Content.ReadAsStringAsync());

        var (finalStatus, finalBody, intermediatePolls) = await bulkDelete.PollToCompletionAsync(location);

        // Scenario 11 (polling): every poll while the job was still running must have been 202; the
        // loop above stops recording once the final, non-202 status arrives.
        intermediatePolls.ShouldAllBe(status => status == HttpStatusCode.Accepted);
        finalStatus.ShouldBe(HttpStatusCode.OK, finalBody.ToJsonString());
        BulkDeleteClient.GetIssueDiagnostics(finalBody).ShouldContain("Job Canceled");

        // Cancellation stops new batches after at most one in-flight page; some resources may remain.
        BulkDeleteClient.GetCounts(finalBody).GetValueOrDefault("Patient").ShouldBeLessThanOrEqualTo(40);

        using var secondCancel = await client.DeleteAsync(location);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.Conflict, await secondCancel.Content.ReadAsStringAsync());
    }
}
