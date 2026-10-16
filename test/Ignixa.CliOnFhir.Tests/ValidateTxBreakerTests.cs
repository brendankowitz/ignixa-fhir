// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Remote;
using Ignixa.CliOnFhir.Validation;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ValidationFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 9 fix round 3, the validator's terminology server questions on the reviewer's <c>txf</c> shapes. Each test is a
/// regression, failing at a5cace5: there every failure, definitive or not, was asked again for every resource, so 30
/// resources sharing a code cost 60 requests, and a hung server held each of them for two timeouts.
/// </summary>
public class ValidateTxBreakerTests
{
    private const int Resources = 30;

    private static readonly string[] Statuses = ["final", "amended", "preliminary", "registered", "corrected", "cancelled"];

    [Theory]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.OK, true)]
    public async Task GivenADefinitiveFailure_WhenManyResourcesShareTheCodeAcrossTwoCalls_ThenTheServerIsAskedOnce(HttpStatusCode status, bool outcomeBody)
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", Observations(_ => "final"));
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(request => IsMetadata(request) ? Metadata() : Outcome(status, outcomeBody));
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx, TxServerClient.DefaultTimeout);

        JsonNode first = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));
        JsonNode second = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));

        ((int)first["facts"]!["verdicts"]!["unknown"]!).ShouldBe(Resources);
        ((int)second["facts"]!["verdicts"]!["unknown"]!).ShouldBe(Resources);
        ValidateCodeRequests(tx).ShouldBe(1);
    }

    [Fact]
    public async Task GivenA503_WhenManyResourcesShareTheCode_ThenEachCallAsksTwiceNotTwicePerResource()
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", Observations(_ => "final"));
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(request => IsMetadata(request) ? Metadata() : Outcome(HttpStatusCode.ServiceUnavailable, true));
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx, TxServerClient.DefaultTimeout);

        JsonNode first = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));
        int afterFirst = ValidateCodeRequests(tx);
        _ = await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full"));

        ((int)first["facts"]!["verdicts"]!["unknown"]!).ShouldBe(Resources);
        afterFirst.ShouldBe(2);
        ValidateCodeRequests(tx).ShouldBe(4);
    }

    [Fact]
    public async Task GivenA503ForEveryCode_WhenManyDistinctCodesAreValidated_ThenTheServerIsNotAskedAfterTwoFailuresInARow()
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", Observations(index => Statuses[index % Statuses.Length]));
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(request => IsMetadata(request) ? Metadata() : Outcome(HttpStatusCode.ServiceUnavailable, true));
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx, TxServerClient.DefaultTimeout);

        JsonNode result = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(Resources);
        ValidateCodeRequests(tx).ShouldBe(2);
    }

    [Fact]
    public async Task GivenAHungServer_WhenManyResourcesAreValidated_ThenOneTimeoutIsWaitedPerCall()
    {
        using var folder = new TestFolder();
        folder.Write("obs.ndjson", Observations(index => Statuses[index % Statuses.Length]));
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Metadata();
        });
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx, TimeSpan.FromMilliseconds(250));
        _ = session.Catalog;

        var clock = Stopwatch.StartNew();
        JsonNode result = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(Depth: "full")));
        clock.Stop();

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(Resources);
        ValidateCodeRequests(tx).ShouldBe(1);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6));
    }

    private static string Observations(Func<int, string> status) =>
        string.Join('\n', Enumerable.Range(1, Resources).Select(index => $$$"""{"resourceType":"Observation","id":"o{{{index}}}","status":"{{{status(index)}}}","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]}}"""));

    private static bool IsMetadata(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/metadata", StringComparison.Ordinal);

    private static int ValidateCodeRequests(StubHttpHandler tx) => tx.Requests.Count(uri => uri.AbsolutePath.EndsWith("/$validate-code", StringComparison.Ordinal));

    private static HttpResponseMessage Metadata() => Fhir("""{"resourceType":"CapabilityStatement","fhirVersion":"4.0.1"}""", HttpStatusCode.OK);

    private static HttpResponseMessage Outcome(HttpStatusCode status, bool outcomeBody) =>
        Fhir(outcomeBody ? """{"resourceType":"OperationOutcome","issue":[{"severity":"error","code":"processing"}]}""" : string.Empty, status);

    private static HttpResponseMessage Fhir(string body, HttpStatusCode status) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/fhir+json"),
    };

    private static FolderSession WithTx(TestFolder folder, TestFolder store, StubHttpHandler tx, TimeSpan timeout) =>
#pragma warning disable CA2000 // The registry and tx clients own their stub handlers and live as long as the session.
        TerminologyFixture.Open(folder, TerminologyFixture.Online(TerminologyFixture.Registry([(PackageId, PackageVersion, TerminologyFixture.Archive(Files()), null)]), store, Package) with
        {
            TxServer = new TxServerClient(new Uri("https://tx.example.org/r4"), tx, timeout),
        });
#pragma warning restore CA2000

    private static JsonNode Json<TFacts>(Contracts.ToolEnvelope<TFacts> envelope) => TerminologyFixture.Json(envelope);
}
