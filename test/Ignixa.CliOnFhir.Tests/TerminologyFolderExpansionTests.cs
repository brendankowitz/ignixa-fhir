// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Remote;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// terminology expand scope=folder: of a ValueSet's codes, those the folder's resources carry, with counts and the searches
/// that find them, from loaded content or the terminology server read page by page. New capability: at 365d1acd expand had no
/// scope, so an agent paged a whole hierarchy and text-searched the folder instead.
/// </summary>
public class TerminologyFolderExpansionTests
{
    private static readonly Uri Server = new("https://tx.example.org/r4");

    private static HttpResponseMessage Fhir(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/fhir+json") };

    private static string Condition(string id, string system, string code) =>
        $$$"""{"resourceType":"Condition","id":"{{{id}}}","code":{"coding":[{"system":"{{{system}}}","code":"{{{code}}}"}]},"subject":{"reference":"Patient/p1"}}""";

    /// <summary>A $expand page of the codes <c>c&lt;first&gt;</c> to <c>c&lt;first + count - 1&gt;</c> of <paramref name="total"/>.</summary>
    private static string Page(int total, int first, int count)
    {
        string contains = string.Join(',', Enumerable.Range(first, count).Select(index => $$"""{"system":"{{Snomed}}","code":"c{{index}}","display":"Code {{index}}"}"""));
        return $$"""{"resourceType":"ValueSet","url":"http://snomed.info/sct?fhir_vs=isa/404684003","expansion":{"total":{{total}},"contains":[""" + contains + "]}}";
    }

    private static int Offset(HttpRequestMessage request) => int.Parse(HttpUtility.ParseQueryString(request.RequestUri!.Query)["offset"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);

#pragma warning disable CA2000 // Each client owns its stub handler and lives as long as its session.
    private static (FolderSession Session, StubHttpHandler Tx) WithServer(TestFolder folder, TestFolder store, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var tx = new StubHttpHandler(respond);
        return (Open(folder, Online(Registry(Archive(Files())), store) with { TxServer = new TxServerClient(Server, tx, TxServerClient.DefaultTimeout) }), tx);
    }
#pragma warning restore CA2000

    private static async Task<JsonNode> Expand(FolderSession session, string valueSet, string? scope = TermExpandInFolder.FolderScope) =>
        Json(await new TerminologyTools(session).Terminology("expand", valueSet: valueSet, scope: scope));

    [Fact]
    public async Task GivenALoadedValueSet_WhenExpandedWithScopeFolder_ThenOnlyTheCodesTheFolderCarriesAreListedWithCountsAndTheirSearch()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        folder.Write("conditions.ndjson", string.Join('\n', Condition("c1", Body, "mi"), Condition("c2", Body, "mi"), Condition("c3", Body, "heart"), Condition("c4", Body, "device"), Condition("c5", Body, "old")));

        JsonNode result = await Expand(OpenWithPackage(folder, store), ValueSetBase + "current-disorders");

        JsonNode facts = result["facts"]!;
        ((string)facts["scope"]!).ShouldBe("folder");
        ((bool)facts["complete"]!).ShouldBeTrue();
        ((int)facts["total"]!).ShouldBe(2);
        facts["codes"]!.AsArray().Select(code => ((string)code!["code"]!, (int)code["resources"]!)).ShouldBe([("mi", 2), ("heart", 1)]);
        JsonNode search = result["next"]![0]!;
        ((string)search["tool"]!).ShouldBe("search_fhir_resources");
        ((string)search["args"]!["resourceType"]!).ShouldBe("Condition");
        ((string)search["args"]!["query"]!).ShouldBe($"code={Body}|mi,{Body}|heart");
    }

    [Fact]
    public async Task GivenAServerExpansionLargerThanOnePage_WhenExpandedWithScopeFolder_ThenEveryPageIsReadAndCodesFromEachAreFound()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        folder.Write("conditions.ndjson", string.Join('\n', Condition("c1", Snomed, "c5"), Condition("c2", Snomed, "c1200"), Condition("c3", Snomed, "c1200"), Condition("c4", Snomed, "other")));
        const int total = TermExpandInFolder.ServerPageSize + 300;
        (FolderSession session, StubHttpHandler tx) = WithServer(folder, store, request => Offset(request) == 0
            ? Fhir(Page(total, 0, TermExpandInFolder.ServerPageSize))
            : Fhir(Page(total, TermExpandInFolder.ServerPageSize, 300)));

        JsonNode result = await Expand(session, "isa/404684003");

        tx.Requests.Where(uri => uri.AbsolutePath.EndsWith("/ValueSet/$expand", StringComparison.Ordinal)).Select(uri => (HttpUtility.ParseQueryString(uri.Query)["url"], HttpUtility.ParseQueryString(uri.Query)["offset"]))
            .ShouldBe([("http://snomed.info/sct?fhir_vs=isa/404684003", "0"), ("http://snomed.info/sct?fhir_vs=isa/404684003", "1000")]);
        JsonNode facts = result["facts"]!;
        ((int)facts["expansionCodes"]!).ShouldBe(total);
        facts["codes"]!.AsArray().Select(code => ((string)code!["code"]!, (int)code["resources"]!)).ShouldBe([("c1200", 2), ("c5", 1)]);
        ((string)facts["source"]!["kind"]!).ShouldBe(AnswerSource.TxServerKind);
        result["omitted"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAServerExpansionBeyondTheCap_WhenExpandedWithScopeFolder_ThenReadingStopsAtTheCapAndSaysSoInOmitted()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        folder.Write("conditions.ndjson", Condition("c1", Snomed, "c3"));
        const int total = TermExpandInFolder.MaxExpansionCodes + 5_000;
        (FolderSession session, StubHttpHandler tx) = WithServer(folder, store, request => Fhir(Page(total, Offset(request), TermExpandInFolder.ServerPageSize)));

        JsonNode result = await Expand(session, "isa/404684003");

        tx.Requests.Count(uri => uri.AbsolutePath.EndsWith("/ValueSet/$expand", StringComparison.Ordinal)).ShouldBe(TermExpandInFolder.MaxExpansionCodes / TermExpandInFolder.ServerPageSize);
        ((int)result["facts"]!["expansionCodes"]!).ShouldBe(TermExpandInFolder.MaxExpansionCodes);
        JsonNode omitted = result["omitted"]!.AsArray().ShouldHaveSingleItem()!;
        ((string)omitted["what"]!).ShouldStartWith("5000 more codes of the expansion were not read");
        ((string)omitted["why"]!).ShouldBe("limit");
    }

    [Fact]
    public async Task GivenNoServerAndAValueSetNotLoaded_WhenExpandedWithScopeFolder_ThenTheOutcomeSaysSoAndNamesTheServerOption()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        folder.Write("conditions.ndjson", Condition("c1", Snomed, "44054006"));

        JsonNode result = await Expand(OpenWithPackage(folder, store), "isa/44054006");

        ((string)result["facts"]!["outcome"]!).ShouldBe(TermExpand.ValueSetNotLoaded);
        ((string)result["facts"]!["valueSet"]!).ShouldBe(TermExpand.SnomedIsAPrefix + "44054006");
        result["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldContain("tx-server-not-enabled");
    }

    [Fact]
    public async Task GivenAnUnknownScope_WhenExpanded_ThenTheCallFailsNamingTheScopes()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();

        JsonNode result = await Expand(OpenWithPackage(folder, store), ValueSetBase + "heart", scope: "patients");

        result["facts"].ShouldBeNull();
        ((string)result["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
        ((string)result["issues"]![0]!["diagnostics"]!).ShouldContain("'folder'");
    }

    [Fact]
    public void GivenTheIsAShorthand_WhenResolved_ThenItNamesSnomedsImplicitValueSetAndOtherUrlsAreKept()
    {
        TermExpand.Canonical("isa/44054006").ShouldBe("http://snomed.info/sct?fhir_vs=isa/44054006");
        TermExpand.Canonical(ValueSetBase + "heart").ShouldBe(ValueSetBase + "heart");
        TermExpand.Canonical("isa/not-a-code").ShouldBe("isa/not-a-code");
    }
}
