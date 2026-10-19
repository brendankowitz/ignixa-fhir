// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Bulk-delete E2E fixture. Identical to <see cref="IgnixaApiFixture"/> (its own uniquely-named SQL
/// database, tenant 1) except it runs DurableTask on the SQL Server backend, with the matching SQL
/// Server background-job repository, instead of the fixture's default FileSystem/in-memory ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the DurableTask override exists (found while writing this suite's E2E tests):</b>
/// <see cref="Ignixa.DataLayer.FileSystem.DurableTask.InMemoryOrchestrationService.LockNextTaskOrchestrationWorkItemAsync"/>
/// hands every locked orchestration work item a brand-new, empty <c>OrchestrationRuntimeState</c>
/// instead of reconstructing it from the instance's persisted history. <c>FileBasedOrchestrationService</c>
/// (the wrapper actually selected by <c>DurableTask:Provider=FileSystem</c>, this fixture's and the base
/// fixture's default) delegates straight to it without correcting this. The result: a multi-step
/// orchestration's *first* resumption after its *first* scheduled activity desyncs DurableTask.Core's
/// replay from history and never progresses again -- every <c>$bulk-delete</c> job observed in this
/// suite stayed "Running" forever after exactly one <see cref="Ignixa.Application.BackgroundOperations.BulkDelete.Activities.BulkDeleteBatchActivity"/>
/// batch, confirmed even for a zero-match job that only needed a second work item to call
/// <see cref="Ignixa.Application.BackgroundOperations.BulkDelete.Activities.CompleteBulkDeleteJobActivity"/>.
/// This is a pre-existing defect in the FileSystem DurableTask provider itself, not in bulk-delete's own
/// orchestration logic -- <c>BulkImportCompositionRootTests</c> already routes around the exact same
/// problem the same way. See the Task 6 final report for the full repro and suspected fix location. It
/// is pre-existing and out of scope for this suite; do not fix it here.
/// </para>
/// <para>
/// <b>Why the <c>BackgroundJobs:Repository</c> override exists:</b> several tests (cancellation,
/// ContinueAsNew) call <see cref="IgnixaApiFixture.WithWebHostBuilder"/> to start a second host against
/// the same database and SQL task hub, so two independent <c>TaskHubWorker</c>s can dispatch work for
/// the same orchestration instance. Left at the <c>InMemory</c> default, each host has its own
/// in-process background-job dictionary: when an activity scheduled by one host's worker happens to run
/// on the *other* host's worker, that host's job repository has never seen the job and the lookup fails
/// with "job not found" -- observed intermittently (roughly 1 in 4 runs) in the 105-resource ContinueAsNew
/// scenario, where enough batches are scheduled to make the cross-host race likely. Setting
/// <c>BackgroundJobs:Repository=SqlServer</c> here (matching <c>BulkImportCompositionRootTests</c>) gives
/// every host the same persisted job row, which both <see cref="Ignixa.Application.BackgroundOperations.BulkDelete.CancelBulkDeleteHandler"/>
/// (running on whichever host is serving the cancel request) and every activity (running on whichever
/// host's worker picks it up) read and write.
/// </para>
/// </remarks>
public class BulkDeleteApiFixture : IgnixaApiFixture
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DurableTask:Provider"] = "SqlServer",
                ["DurableTask:SqlServer:TaskHubName"] = "bulkdelete",
                ["BackgroundJobs:Repository"] = "SqlServer",
            }));
    }
}

/// <summary>
/// Dedicated xunit collection so every bulk-delete E2E test class shares one
/// <see cref="BulkDeleteApiFixture"/> instance (one SQL database, one SqlServer-backed DurableTask
/// schema) instead of each test class paying its own fixture startup cost.
/// </summary>
// CA1711 suppressed: xUnit convention requires collection definitions to end with "Collection"
// for the ICollectionFixture<T> pattern to work correctly (see E2ETestCollection).
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public class BulkDeleteTestCollection : ICollectionFixture<BulkDeleteApiFixture>
#pragma warning restore CA1711
{
    public const string Name = "BulkDeleteApiFixture collection";
}
