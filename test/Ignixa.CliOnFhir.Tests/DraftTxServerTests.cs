// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Remote;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DraftFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A draft's codes checked by an opt-in terminology server, a stub here (never the network): codes only are sent.</summary>
public class DraftTxServerTests
{
    private static readonly Uri Server = new("https://tx.example.org/r4");

#pragma warning disable CA2000 // The client owns its stub handler and lives as long as its session.
    private static (FolderSession Session, StubHttpHandler Tx) Open(TestFolder folder, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var tx = new StubHttpHandler(respond);
        return (DraftFixture.Open(folder, TerminologyOptions.None with { TxServer = new TxServerClient(Server, tx, TxServerClient.DefaultTimeout) }), tx);
    }
#pragma warning restore CA2000

    private static HttpResponseMessage Valid(string display) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"resourceType":"Parameters","parameter":[{"name":"result","valueBoolean":true},{"name":"display","valueString":"{{display}}"}]}""", Encoding.UTF8, "application/fhir+json"),
    };

    [Fact]
    public async Task GivenACodeMissingFromAFragmentOfItsSystem_WhenDrafted_ThenTheServerAnswersOnlySystemAndCodeAreSentAndADisplayItDisagreesWithIsUnknown()
    {
        using TestFolder folder = Clinic();
        (FolderSession session, StubHttpHandler tx) = Open(folder, _ => Valid("Glucose [Mass/volume] in Serum or Plasma"));
        string input = """{"patient":"p1","code":{"system":"http://loinc.org","code":"2345-7","display":"Blood glucose SENTINEL-DISPLAY"},"note":"SENTINEL-NOTE"}""";

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(session, new DraftRequest("ServiceRequest", input));

        DraftCheck check = Check(Facts(envelope), "terminology", "ServiceRequest.code.coding[0]");
        check.Outcome.ShouldBe(DraftCheck.Unknown);
        Facts(envelope).Verdict.ShouldBe(DraftFacts.Unknown);
        envelope.Issues.Single(issue => issue.Code == "display-mismatch").Severity.ShouldBe("warning");
        var details = (TerminologyCheckDetails)check.Details;
        details.Source!.Kind.ShouldBe("tx-server");
        details.ExpectedDisplay.ShouldBe("Glucose [Mass/volume] in Serum or Plasma");
        Uri request = tx.Requests.ShouldHaveSingleItem();
        request.AbsoluteUri.ShouldBe("https://tx.example.org/r4/CodeSystem/$validate-code?url=http%3A%2F%2Floinc.org&code=2345-7");
        request.AbsoluteUri.ShouldNotContain("SENTINEL");
    }

    [Fact]
    public async Task GivenAServerThatFails_WhenDrafted_ThenTheCodeIsUnknownNotValid()
    {
        using TestFolder folder = Clinic();
        (FolderSession session, _) = Open(folder, _ => StubHttpHandler.Status(HttpStatusCode.InternalServerError));
        string input = """{"patient":"p1","code":{"system":"http://example.org/labs","code":"2345-7"}}""";

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(session, new DraftRequest("ServiceRequest", input));

        DraftFacts facts = Facts(envelope);
        Check(facts, "terminology", "ServiceRequest.code.coding[0]").Outcome.ShouldBe(DraftCheck.Unknown);
        facts.Verdict.ShouldBe(DraftFacts.Unknown);
        envelope.Issues.ShouldContain(issue => issue.Code == StoreTerminologyService.TxUnavailableReason);
    }

    [Fact]
    public async Task GivenACodeTheStoreHas_WhenDrafted_ThenTheServerIsNotAsked()
    {
        using TestFolder folder = Clinic();
        (FolderSession session, StubHttpHandler tx) = Open(folder, _ => Valid("x"));

        Facts(await RunAsync(session, new DraftRequest("ServiceRequest", MagnesiumOrder))).Verdict.ShouldBe(DraftFacts.Valid);

        tx.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAPrimaryDisplayOnlyTheServerCouldCompare_WhenDraftedWithOut_ThenItIsWrittenWithAWarningThatItsDisplayIsUnconfirmed()
    {
        using TestFolder root = ClinicRoot();
        using var tx = new StubHttpHandler(_ => Valid("Glucose [Mass/volume] in Serum or Plasma"));
        var session = new FolderSession(DeIdFixture.Data(root), new Ignixa.Specification.Generated.R4CoreSchemaProvider(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, terminology: TerminologyOptions.None with { TxServer = new TxServerClient(Server, tx, TxServerClient.DefaultTimeout) });
        string input = """{"patient":"p1","code":{"system":"http://loinc.org","code":"2345-7","display":"Blood glucose"}}""";

        ToolEnvelope<DraftFacts?> envelope = await RunAsync(session, new DraftRequest("ServiceRequest", input, Out: DeIdFixture.Out(root)));

        Facts(envelope).Verdict.ShouldBe(DraftFacts.Unknown);
        Facts(envelope).File.ShouldNotBeNull();
        envelope.Issues.Single(issue => issue.Code == "out-written-unknown").Diagnostics.ShouldContain("ServiceRequest.code.coding[0] (its display could not be confirmed)");
    }
}
