// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Report and note text (V17): DiagnosticReport.presentedForm and DocumentReference.content.attachment hold the report as base64
/// 'data'. A shell agent decodes it; at V16 read_resources returned only the base64, so the tool knew less than the shell.
/// </summary>
public class ReportTextTests
{
    [Fact]
    public void GivenAPlainTextReport_WhenRead_ThenItsTextComesBackDecodedWithItsSize()
    {
        const string Report = "EXAM: Chest radiograph.\r\nIMPRESSION: Right lower lobe pneumonia.";
        using TestFolder folder = new TestFolder().Write("dr.ndjson", Report_("dr1", "text/plain; charset=utf-8", Report));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["DiagnosticReport/dr1"]);

        AttachmentText text = envelope.Facts!.Texts.ShouldNotBeNull().ShouldHaveSingleItem();
        text.ShouldBe(new AttachmentText("DiagnosticReport/dr1", "presentedForm[0]", "text/plain; charset=utf-8", Encoding.UTF8.GetByteCount(Report), "EXAM: Chest radiograph.\nIMPRESSION: Right lower lobe pneumonia."));
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenElementsThatLeaveTheAttachmentOut_WhenRead_ThenTheTextIsStillDecodedFromTheStoredResource()
    {
        using TestFolder folder = new TestFolder().Write("dr.ndjson", Report_("dr1", "text/plain", "Normal study."));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["DiagnosticReport/dr1"], elements: "status");

        envelope.Facts!.Resources.ShouldHaveSingleItem().AsObject().ContainsKey("presentedForm").ShouldBeFalse();
        envelope.Facts.Texts.ShouldNotBeNull().ShouldHaveSingleItem().Text.ShouldBe("Normal study.");
    }

    [Fact]
    public void GivenAnHtmlNote_WhenDecoded_ThenTagsScriptsAndEntitiesAreReducedToItsText()
    {
        const string Html = "<html><head><style>p{color:red}</style><script>alert('x')</script></head><body><h1>Progress&nbsp;note</h1><p>BP 120/80 &amp; HR 72.<br/>Plan:   continue</p><!-- hidden --><ul><li>aspirin</li><li>statin</li></ul></body></html>";

        string text = AttachmentText.StripHtml(Html);

        text.ShouldBe("Progress note\n\nBP 120/80 & HR 72.\nPlan: continue\n\naspirin\n\nstatin");
    }

    [Fact]
    public void GivenAnHtmlDocumentReference_WhenRead_ThenTheAttachmentTextIsTheStrippedHtml()
    {
        using TestFolder folder = new TestFolder().Write("doc.ndjson", Document("d1", "current", "final", "2150-01-01T10:00:00Z", relatesTo: null, "text/html", "<p>Discharge <b>home</b></p>"));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["DocumentReference/d1"]);

        AttachmentText text = envelope.Facts!.Texts.ShouldNotBeNull().ShouldHaveSingleItem();
        text.Path.ShouldBe("content[0].attachment");
        text.Text.ShouldBe("Discharge home");
        envelope.Facts.Versions.ShouldBeNull();
    }

    [Fact]
    public void GivenAReportLongerThanTheCap_WhenDecoded_ThenItIsCutAtTheCapWithAMarkerGivingBothSizes()
    {
        string report = string.Concat(Enumerable.Repeat("Findings ünïcode line. ", 400));
        using TestFolder folder = new TestFolder().Write("dr.ndjson", Report_("dr1", "text/plain", report));

        // The stored base64 alone is over the default budget, so the resource is trimmed: the text is decoded all the same.
        AttachmentText text = ResourceFetcher.Read(Open(folder), ["DiagnosticReport/dr1"], elements: "status").Facts!.Texts.ShouldNotBeNull().ShouldHaveSingleItem();

        int total = Encoding.UTF8.GetByteCount(report.TrimEnd());
        text.Text.ShouldNotBeNull().ShouldEndWith($" of {total} bytes of text are shown]");
        string shown = text.Text[..text.Text.LastIndexOf("\n[truncated: the first ", StringComparison.Ordinal)];
        Encoding.UTF8.GetByteCount(shown).ShouldBeLessThanOrEqualTo(AttachmentText.MaxBytes);
        Encoding.UTF8.GetByteCount(shown).ShouldBeGreaterThan(AttachmentText.MaxBytes - 4);
        text.Text.ShouldContain($"the first {Encoding.UTF8.GetByteCount(shown)} of {total} bytes");
    }

    [Theory]
    [InlineData("text/plain", "not base64!", "not valid base64")]
    [InlineData("text/plain; charset=ebcdic", null, "charset 'ebcdic'")]
    public void GivenDataThatCannotBeDecoded_WhenRead_ThenTheAttachmentSaysWhyInsteadOfText(string contentType, string? data, string problem)
    {
        string encoded = data ?? Convert.ToBase64String(Encoding.UTF8.GetBytes("x"));
        using TestFolder folder = new TestFolder().Write("dr.ndjson", $$"""{"resourceType":"DiagnosticReport","id":"dr1","status":"final","code":{"text":"x"},"presentedForm":[{"contentType":"{{contentType}}","data":"{{encoded}}"}]}""");

        AttachmentText text = ResourceFetcher.Read(Open(folder), ["DiagnosticReport/dr1"]).Facts!.Texts.ShouldNotBeNull().ShouldHaveSingleItem();

        text.Text.ShouldBeNull();
        text.Problem.ShouldNotBeNull().ShouldContain(problem);
    }

    [Fact]
    public void GivenAttachmentsThatAreNotInlineText_WhenRead_ThenNoTextIsListed()
    {
        string pdf = Convert.ToBase64String("%PDF-1.4"u8.ToArray());
        using TestFolder folder = new TestFolder().Write("dr.ndjson", $$"""{"resourceType":"DiagnosticReport","id":"dr1","status":"final","code":{"text":"x"},"presentedForm":[{"contentType":"application/pdf","data":"{{pdf}}"},{"contentType":"text/plain","url":"http://example.org/report.txt"},{"data":"{{pdf}}"},{"contentType":5}]}""");

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["DiagnosticReport/dr1"]);

        envelope.Facts!.Texts.ShouldBeNull();
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenADocumentInAReplacementChain_WhenOneVersionIsRead_ThenTheWholeChainComesBackInDateOrder()
    {
        using TestFolder folder = new TestFolder().Write("doc.ndjson", string.Join('\n',
            Document("final", "superseded", "final", "2189-06-11T11:45:00-04:00", "replaces DocumentReference/prelim", "text/plain", "FINAL"),
            Document("amended", "current", "amended", "2189-06-12T09:05:00-04:00", "replaces DocumentReference/final", "text/plain", "AMENDED"),
            Document("prelim", "superseded", "preliminary", "2189-06-11T08:20:00-04:00", relatesTo: null, "text/plain", "PRELIMINARY"),
            Document("addendum", "current", "final", "2189-06-13T08:00:00-04:00", "appends DocumentReference/amended", "text/plain", "ADDENDUM"),
            Document("other", "current", "final", "2189-06-14T08:00:00-04:00", relatesTo: null, "text/plain", "UNRELATED")));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["DocumentReference/final", "DocumentReference/other"]);

        IReadOnlyList<DocumentVersion> chain = envelope.Facts!.Versions.ShouldNotBeNull().ShouldHaveSingleItem();
        chain.Select(version => (version.Reference, version.Status, version.DocStatus)).ShouldBe([
            ("DocumentReference/prelim", "superseded", "preliminary"),
            ("DocumentReference/final", "superseded", "final"),
            ("DocumentReference/amended", "current", "amended"),
            ("DocumentReference/addendum", "current", "final")]);
        chain[2].RelatesTo.ShouldBe(["replaces DocumentReference/final"]);
        chain[0].Date.ShouldBe("2189-06-11T08:20:00-04:00");
        envelope.Facts.Texts!.Select(text => text.Text).ShouldBe(["FINAL", "UNRELATED"]);
    }

    [Fact]
    public void GivenMoreAttachmentsThanAreDecoded_WhenRead_ThenTheRestAreListedWithAProblem()
    {
        string form = string.Join(',', Enumerable.Range(1, 4).Select(index => $$"""{"contentType":"text/plain","data":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes($"part {index}"))}}"}"""));
        using TestFolder folder = new TestFolder().Write("dr.ndjson", $$"""{"resourceType":"DiagnosticReport","id":"dr1","status":"final","code":{"text":"x"},"presentedForm":[{{form}}]}""");

        IReadOnlyList<AttachmentText> texts = ResourceFetcher.Read(Open(folder), ["DiagnosticReport/dr1"]).Facts!.Texts.ShouldNotBeNull();

        texts.Select(text => text.Text).ShouldBe(["part 1", "part 2", null, null]);
        texts[3].Problem.ShouldNotBeNull().ShouldContain($"at most {AttachmentText.MaxAttachments}");
    }

    [Fact]
    public void GivenMarkupThatNeverCloses_WhenStripped_ThenItTakesLinearTime()
    {
        string html = string.Concat(Enumerable.Repeat("<script><", 40_000));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        string text = AttachmentText.StripHtml(html);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        text.ShouldNotBeNull();
    }

    [Fact]
    public void GivenAGroupLongerThanAChainLists_WhenTwoFarApartDocumentsAreRead_ThenTheGroupIsListedOnce()
    {
        int count = DocumentVersions.MaxVersions + 5;
        using TestFolder folder = new TestFolder().Write("doc.ndjson", string.Join('\n', Enumerable.Range(0, count).Select(index =>
            Document($"d{index}", index == count - 1 ? "current" : "superseded", "final", $"2150-01-{index + 1:00}T00:00:00Z", index == 0 ? null : $"replaces DocumentReference/d{index - 1}", "text/plain", $"v{index}"))));

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["DocumentReference/d0", $"DocumentReference/d{count - 1}"]);

        IReadOnlyList<DocumentVersion> chain = envelope.Facts!.Versions.ShouldNotBeNull().ShouldHaveSingleItem();
        chain.Count.ShouldBe(DocumentVersions.MaxVersions);
        chain[0].Reference.ShouldBe("DocumentReference/d0");
    }

    [Fact]
    public void GivenManyLongReports_WhenRead_ThenTheResponseStaysWithinTheMcpCapAndTheRestIsOffered()
    {
        string report = string.Concat(Enumerable.Repeat("Long findings text. ", 200));
        using TestFolder folder = new TestFolder().Write("dr.ndjson", string.Join('\n', Enumerable.Range(1, 12).Select(index => Report_($"dr{index}", "text/plain", report))));
        var session = new FolderSession(folder.Path, new Ignixa.Specification.Generated.R4CoreSchemaProvider(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, maxResponseBytes: EnvelopeJson.McpMaxBytes);

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(session, [.. Enumerable.Range(1, 12).Select(index => $"DiagnosticReport/dr{index}")]);

        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.McpMaxBytes);
        int returned = envelope.Facts!.Resources.Count;
        returned.ShouldBeInRange(1, 11);
        envelope.Facts.Texts.ShouldNotBeNull().Count.ShouldBe(returned);
        envelope.Next.ShouldHaveSingleItem().Tool.ShouldBe("read_resources");
    }

    private static string Report_(string id, string contentType, string text) =>
        new JsonObject
        {
            ["resourceType"] = "DiagnosticReport",
            ["id"] = id,
            ["status"] = "final",
            ["code"] = new JsonObject { ["text"] = "XR Chest" },
            ["presentedForm"] = new JsonArray(new JsonObject { ["contentType"] = contentType, ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) }),
        }.ToJsonString();

    private static string Document(string id, string status, string docStatus, string date, string? relatesTo, string contentType, string text)
    {
        var document = new JsonObject
        {
            ["resourceType"] = "DocumentReference",
            ["id"] = id,
            ["status"] = status,
            ["docStatus"] = docStatus,
            ["date"] = date,
            ["content"] = new JsonArray(new JsonObject { ["attachment"] = new JsonObject { ["contentType"] = contentType, ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) } }),
        };
        if (relatesTo is not null)
        {
            string[] parts = relatesTo.Split(' ');
            document["relatesTo"] = new JsonArray(new JsonObject { ["code"] = parts[0], ["target"] = new JsonObject { ["reference"] = parts[1] } });
        }

        return document.ToJsonString();
    }
}
