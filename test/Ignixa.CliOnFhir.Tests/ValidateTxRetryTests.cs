// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Remote;
using Ignixa.CliOnFhir.Validation;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ValidationFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 9 fix round 2: the validator's remote binding answers. Each test is a regression, failing at 29aa44d: there a
/// transient server failure was cached as the answer for the session, and a waiter on another caller's question inherited
/// that caller's cancellation.
/// </summary>
public class ValidateTxRetryTests
{
    private const string ObservationStatus = "http://hl7.org/fhir/ValueSet/observation-status|4.0.1";

    private const string Observation = """{"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]}}""";

    [Fact]
    public async Task GivenATxServerFailingOnce_WhenABindingItDecidesIsValidated_ThenTheCallRetriesAndTheDefinitiveAnswerIsCached()
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", Observation);
        using var store = new TestFolder();
        int asked = 0;
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(_ => Interlocked.Increment(ref asked) == 1 ? StubHttpHandler.Status(HttpStatusCode.ServiceUnavailable) : Valid());
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx);

        JsonNode first = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));
        JsonNode second = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));

        ((int)first["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        ((int)second["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        tx.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenATxServerThatKeepsFailing_WhenValidatedTwice_ThenEachCallAsksOnceMoreAndTheBindingStaysNotChecked()
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", Observation);
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(_ => StubHttpHandler.Status(HttpStatusCode.BadGateway));
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx);

        JsonNode first = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));
        int afterFirst = tx.Requests.Count;
        JsonNode second = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));

        ((int)first["facts"]!["verdicts"]!["unknown"]!).ShouldBe(1);
        ((int)second["facts"]!["verdicts"]!["unknown"]!).ShouldBe(1);
        afterFirst.ShouldBe(2);
        tx.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GivenTwoCallersAskingTheSameQuestion_WhenTheFirstIsCancelled_ThenTheSecondAsksWithItsOwnTokenAndGetsTheAnswer()
    {
        using var folder = new TestFolder();
        folder.Write("patients.ndjson", """{"resourceType":"Patient","id":"p1","gender":"male"}""");
        using var store = new TestFolder();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(async (_, cancellationToken) =>
        {
            await release.Task.WaitAsync(cancellationToken);
            return Valid();
        });
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx);
        _ = await ResourceValidator.ValidateAsync(session, new ValidateRequest());
        var service = new StoreTerminologyService(session);
        using var cancelled = new CancellationTokenSource();

        Task<(string Result, string Reason)> first = service.CheckAsync(ObservationStatus, "http://hl7.org/fhir/observation-status", "final", null, cancelled.Token);
        Task<(string Result, string Reason)> second = service.CheckAsync(ObservationStatus, "http://hl7.org/fhir/observation-status", "final", null, CancellationToken.None);
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(first);
        release.SetResult();

        (await second).Result.ShouldBe("valid");
    }

    private static FolderSession WithTx(TestFolder folder, TestFolder store, StubHttpHandler tx) =>
#pragma warning disable CA2000 // The registry and tx clients own their stub handlers and live as long as the session.
        TerminologyFixture.Open(folder, TerminologyFixture.Online(TerminologyFixture.Registry([(PackageId, PackageVersion, TerminologyFixture.Archive(Files()), null)]), store, Package) with
        {
            TxServer = new TxServerClient(new Uri("https://tx.example.org/r4"), tx, TxServerClient.DefaultTimeout),
        });
#pragma warning restore CA2000

    private static HttpResponseMessage Valid() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"resourceType":"Parameters","parameter":[{"name":"result","valueBoolean":true}]}""", Encoding.UTF8, "application/fhir+json"),
    };

    private static JsonNode Json<TFacts>(Contracts.ToolEnvelope<TFacts> envelope) => TerminologyFixture.Json(envelope);
}
