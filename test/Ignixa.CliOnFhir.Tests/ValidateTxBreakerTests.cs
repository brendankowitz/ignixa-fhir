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
/// resources sharing a code cost 60 requests, and a hung server held each of them for two timeouts. Round 4 adds the
/// reviewer's <c>txb</c> shape (one code the server fails on, the rest healthy), failing at 406a06e: there the failing
/// question's retry counted as a second failure in a row, opening the breaker for the whole call.
/// </summary>
public class ValidateTxBreakerTests
{
    private const int Resources = 30;

    private const string ObsBind = "http://example.org/fhir/StructureDefinition/obsbind";

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

    // The first question fails and is asked once more; the second question's failure opens the breaker: three requests, not
    // one per distinct code (at 406a06e the first question's retry opened it after two).
    [Fact]
    public async Task GivenA503ForEveryCode_WhenManyDistinctCodesAreValidated_ThenTheServerIsNotAskedAfterTwoQuestionsFailInARow()
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
        ValidateCodeRequests(tx).ShouldBe(3);
    }

    // Regression (406a06e): the reviewer's txb shape. The first resource's code gets a 500 twice; the 49 others are distinct
    // healthy codes. At 406a06e the retry opened the breaker, so 2 requests were made and all 50 were unknown.
    [Fact]
    public async Task GivenOneCodeTheServerFailsOnFollowedByHealthyCodes_WhenValidated_ThenOnlyThatResourceIsUnknownAndEveryOtherCodeIsAsked()
    {
        using var folder = new TestFolder();
        WriteCodeBound(folder, index => index == 1 ? "BAD-1" : $"{1000 + index}-1");
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(request => IsMetadata(request) ? Metadata() : AskedCode(request).StartsWith("BAD", StringComparison.Ordinal) ? Outcome(HttpStatusCode.InternalServerError, false) : Valid());
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx, TxServerClient.DefaultTimeout, WithObservationStatus());

        JsonNode result = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(ResourceType: "Observation", Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(1);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(Resources - 1);
        ValidateCodeRequests(tx).ShouldBe(Resources + 1);
    }

    // Regression (406a06e): a success closes the breaker, so failing codes interleaved with healthy ones each get their retry
    // and never open it. At 406a06e the first failing code's retry opened it for the whole call.
    [Fact]
    public async Task GivenFailingCodesInterleavedWithHealthyOnes_WhenValidated_ThenEachFailingCodeIsRetriedAndEveryHealthyCodeIsAsked()
    {
        using var folder = new TestFolder();
        WriteCodeBound(folder, index => index % 2 == 1 ? $"BAD-{index}" : $"{1000 + index}-1");
        using var store = new TestFolder();
#pragma warning disable CA2000 // The client owns the stub handler and lives as long as the session.
        var tx = new StubHttpHandler(request => IsMetadata(request) ? Metadata() : AskedCode(request).StartsWith("BAD", StringComparison.Ordinal) ? Outcome(HttpStatusCode.InternalServerError, false) : Valid());
#pragma warning restore CA2000
        FolderSession session = WithTx(folder, store, tx, TxServerClient.DefaultTimeout, WithObservationStatus());

        JsonNode result = Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(ResourceType: "Observation", Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(Resources / 2);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(Resources / 2);
        ValidateCodeRequests(tx).ShouldBe(Resources / 2 * 3);
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

    /// <summary>Observations whose status is <paramref name="status"/>: the server is asked about each distinct status, observation-status being no package's.</summary>
    private static string Observations(Func<int, string> status) =>
        string.Join('\n', Enumerable.Range(1, Resources).Select(index => $$$"""{"resourceType":"Observation","id":"o{{{index}}}","status":"{{{status(index)}}}","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]}}"""));

    /// <summary>
    /// The reviewer's txb shape: a folder profile requiring Observation.code to be in observation-codes, and Observations
    /// claiming it whose code is <paramref name="code"/>, so the server is asked about each distinct code (with
    /// <see cref="WithObservationStatus"/>, about nothing else).
    /// </summary>
    private static void WriteCodeBound(TestFolder folder, Func<int, string> code)
    {
        folder.Write("sd-obsbind.json", $$$"""
            {"resourceType":"StructureDefinition","id":"obsbind","url":"{{{ObsBind}}}","version":"1.0.0","name":"ObsBind","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Observation","derivation":"constraint",
             "differential":{"element":[{"id":"Observation.code","path":"Observation.code","binding":{"strength":"required","valueSet":"http://hl7.org/fhir/ValueSet/observation-codes"}}]}}
            """);
        folder.Write("obs.ndjson", string.Join('\n', Enumerable.Range(1, Resources).Select(index => $$$"""{"resourceType":"Observation","id":"o{{{index}}}","meta":{"profile":["{{{ObsBind}}}"]},"status":"final","code":{"coding":[{"system":"http://loinc.org","code":"{{{code(index)}}}"}]}}""")));
    }

    /// <summary>The fixture package with R4's observation-status added, so a status is answered locally and never asked of the server.</summary>
    private static IReadOnlyList<(string Path, string Content)> WithObservationStatus() =>
    [
        .. Files(),
        ("package/cs-observation-status.json", TerminologyFixture.CodeSystem("http://hl7.org/fhir/observation-status", "4.0.1", string.Empty, "registered", "preliminary", "final", "amended", "corrected", "cancelled", "entered-in-error", "unknown")),
        ("package/vs-observation-status.json", """{"resourceType":"ValueSet","id":"observation-status","url":"http://hl7.org/fhir/ValueSet/observation-status","version":"4.0.1","compose":{"include":[{"system":"http://hl7.org/fhir/observation-status"}]}}"""),
    ];

    /// <summary>The <c>code</c> a $validate-code request asks about.</summary>
    private static string AskedCode(HttpRequestMessage request) =>
        request.RequestUri!.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).Where(pair => pair[0] == "code").Select(pair => Uri.UnescapeDataString(pair[1])).SingleOrDefault() ?? string.Empty;

    private static HttpResponseMessage Valid() => Fhir("""{"resourceType":"Parameters","parameter":[{"name":"result","valueBoolean":true}]}""", HttpStatusCode.OK);

    private static bool IsMetadata(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/metadata", StringComparison.Ordinal);

    private static int ValidateCodeRequests(StubHttpHandler tx) => tx.Requests.Count(uri => uri.AbsolutePath.EndsWith("/$validate-code", StringComparison.Ordinal));

    private static HttpResponseMessage Metadata() => Fhir("""{"resourceType":"CapabilityStatement","fhirVersion":"4.0.1"}""", HttpStatusCode.OK);

    private static HttpResponseMessage Outcome(HttpStatusCode status, bool outcomeBody) =>
        Fhir(outcomeBody ? """{"resourceType":"OperationOutcome","issue":[{"severity":"error","code":"processing"}]}""" : string.Empty, status);

    private static HttpResponseMessage Fhir(string body, HttpStatusCode status) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/fhir+json"),
    };

    private static FolderSession WithTx(TestFolder folder, TestFolder store, StubHttpHandler tx, TimeSpan timeout, IReadOnlyList<(string Path, string Content)>? files = null) =>
#pragma warning disable CA2000 // The registry and tx clients own their stub handlers and live as long as the session.
        TerminologyFixture.Open(folder, TerminologyFixture.Online(TerminologyFixture.Registry([(PackageId, PackageVersion, TerminologyFixture.Archive(files ?? Files()), null)]), store, Package) with
        {
            TxServer = new TxServerClient(new Uri("https://tx.example.org/r4"), tx, timeout),
        });
#pragma warning restore CA2000

    private static JsonNode Json<TFacts>(Contracts.ToolEnvelope<TFacts> envelope) => TerminologyFixture.Json(envelope);
}
