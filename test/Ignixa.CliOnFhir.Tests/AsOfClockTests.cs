// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Where a session's asOf comes from (Task 12, section A): new behaviour, no clock exists before it.</summary>
public class AsOfClockTests
{
    private const string Patients = """
        {"resourceType":"Patient","id":"p1"}
        {"resourceType":"Patient","id":"p2"}
        """;

    private static string Manifest(string transactionTime) => $$"""
        {"transactionTime":"{{transactionTime}}","request":"https://example.org/fhir/$export","requiresAccessToken":true,
         "output":[{"type":"Patient","url":"https://example.org/files/Patient.ndjson","count":2}],"error":[]}
        """;

    private static string Bundle(string? before, string? after, string id = "b1") =>
        "{\"resourceType\":\"Bundle\"," + (before is null ? string.Empty : $"\"timestamp\":\"{before}\",") + "\"type\":\"collection\",\"entry\":["
        + $"{{\"fullUrl\":\"urn:uuid:{id}-1\",\"resource\":{{\"resourceType\":\"Patient\",\"id\":\"{id}-1\"}}}},"
        + $"{{\"fullUrl\":\"urn:uuid:{id}-2\",\"resource\":{{\"resourceType\":\"Patient\",\"id\":\"{id}-2\"}},\"search\":{{\"mode\":\"match\"}}}}"
        + "]" + (after is null ? string.Empty : $",\"timestamp\":\"{after}\"") + "}";

    private static (AsOfClock Clock, ManualTimeProvider Time) Open(TestFolder folder, string? option = null)
    {
        var time = new ManualTimeProvider();
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: time);
        return (new AsOfClock(session, option), time);
    }

    private static DateTimeOffset At(string instant) => DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);

    [Fact]
    public void GivenABulkExportManifestAtTheRoot_WhenResolved_ThenItsTransactionTimeIsAsOfAndBundlesAreNotConsulted()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("manifest.json", Manifest("2024-03-01T12:00:00Z")).Write("bundle.json", Bundle("2025-01-01T00:00:00Z", null));

        AsOfResolution resolution = Open(folder).Clock.Resolve();

        resolution.Value.ShouldBe(new AsOf(At("2024-03-01T12:00:00Z"), AsOfSources.BulkExportManifest));
        resolution.Value.Instant.ShouldBe("2024-03-01T12:00:00Z");
        resolution.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAManifestOnlyInASubfolder_WhenResolved_ThenItIsNotRead()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("export/manifest.json", Manifest("2024-03-01T12:00:00Z"));

        (AsOfClock clock, ManualTimeProvider time) = Open(folder);

        clock.Resolve().Value.ShouldBe(new AsOf(time.Now, AsOfSources.Clock));
    }

    [Fact]
    public void GivenManifestsThatDisagree_WhenResolved_ThenTheLatestIsUsedAndTheSpreadNoted()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients)
            .Write("export-manifest.json", Manifest("2024-03-01T12:00:00Z"))
            .Write("MANIFEST-2.json", Manifest("2024-03-02T08:00:00+02:00"));

        AsOfResolution resolution = Open(folder).Clock.Resolve();

        resolution.Value.Instant.ShouldBe("2024-03-02T08:00:00+02:00");
        IssueItem spread = resolution.Issues.ShouldHaveSingleItem();
        spread.Code.ShouldBe("as-of-manifest-spread");
        spread.Diagnostics.ShouldContain("from 2024-03-01T12:00:00Z to 2024-03-02T08:00:00+02:00");
    }

    [Theory]
    [InlineData("""{"transactionTime":"2024-03-01T12:00:00Z"}""", "output array")]
    [InlineData("""{"output":[]}""", "no transactionTime")]
    [InlineData("""{"transactionTime":"2024-03-01","output":[]}""", "not a FHIR instant")]
    [InlineData("""{"transactionTime":""", "not valid JSON")]
    public void GivenAFileNamedLikeAManifestThatIsNotOne_WhenResolved_ThenItIsIgnoredWithANote(string content, string reason)
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("manifest.json", content);

        (AsOfClock clock, ManualTimeProvider time) = Open(folder);
        AsOfResolution resolution = clock.Resolve();

        resolution.Value.ShouldBe(new AsOf(time.Now, AsOfSources.Clock));
        IssueItem note = resolution.Issues.ShouldHaveSingleItem();
        note.Code.ShouldBe("as-of-manifest-ignored");
        note.Diagnostics.ShouldContain(reason);
        note.Location.ShouldBe("manifest.json");
    }

    [Fact]
    public void GivenABundleWithATimestampBeforeItsEntries_WhenResolved_ThenItIsAsOf()
    {
        using TestFolder folder = new TestFolder().Write("bundle.json", Bundle("2024-02-02T02:02:02.5+01:00", null));

        AsOfResolution resolution = Open(folder).Clock.Resolve();

        resolution.Value.ShouldBe(new AsOf(At("2024-02-02T02:02:02.5+01:00"), AsOfSources.BundleTimestamp));
        resolution.Value.Instant.ShouldBe("2024-02-02T02:02:02.5+01:00");
        resolution.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenABundleWithItsTimestampAfterItsEntries_WhenResolved_ThenItIsFoundPastBracketsInStringsAndComments()
    {
        string bundle = """
            {"resourceType":"Bundle","type":"collection","entry":[
              {"fullUrl":"urn:uuid:a","resource":{"resourceType":"Patient","id":"a"},"request":{"method":"PUT","url":"Patient?name=]}x\"]"}},
              /* ] } a comment */
              {"fullUrl":"urn:uuid:b","resource":{"resourceType":"Bogus","id":"b","text":"]]]"}}
            ], // the entries end here ]
            "timestamp":"2023-12-31T23:59:59Z"}
            """;
        using TestFolder folder = new TestFolder().Write("bundle.json", bundle);

        AsOfResolution resolution = Open(folder).Clock.Resolve();

        resolution.Value.ShouldBe(new AsOf(At("2023-12-31T23:59:59Z"), AsOfSources.BundleTimestamp));
    }

    [Fact]
    public void GivenBundlesWithDifferentTimestampsAndOneWithout_WhenResolved_ThenTheLatestIsUsedAndTheSpreadNoted()
    {
        using TestFolder folder = new TestFolder()
            .Write("a.json", Bundle("2024-01-01T00:00:00Z", null, "a"))
            .Write("b.json", Bundle(null, "2024-06-30T00:00:00Z", "b"))
            .Write("c.json", Bundle(null, null, "c"))
            .Write("d.json", Bundle("2024-07-01", null, "d"));

        AsOfResolution resolution = Open(folder).Clock.Resolve();

        resolution.Value.ShouldBe(new AsOf(At("2024-06-30T00:00:00Z"), AsOfSources.BundleTimestamp));
        IssueItem note = resolution.Issues.ShouldHaveSingleItem();
        note.Code.ShouldBe("as-of-bundle-timestamps");
        note.Diagnostics.ShouldContain("2 container Bundles state a timestamp, from 2024-01-01T00:00:00Z to 2024-06-30T00:00:00Z");
        note.Diagnostics.ShouldContain("1 Bundles state no timestamp");
        note.Diagnostics.ShouldContain("1 Bundles' timestamps could not be read");
    }

    [Fact]
    public void GivenBundlesThatAgree_WhenResolved_ThenThereIsNoNote()
    {
        using TestFolder folder = new TestFolder().Write("a.json", Bundle("2024-01-01T00:00:00Z", null, "a")).Write("b.json", Bundle("2024-01-01T01:00:00+01:00", null, "b"));

        Open(folder).Clock.Resolve().Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoDataPullTime_WhenResolved_ThenTheSessionClockIsUsedOnceForTheSession()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);
        (AsOfClock clock, ManualTimeProvider time) = Open(folder);
        time.Now = new DateTimeOffset(2025, 3, 31, 12, 0, 0, 123, TimeSpan.FromHours(2)).AddTicks(9);

        AsOf first = clock.Resolve().Value;
        time.Now = time.Now.AddHours(5);
        AsOf second = clock.Resolve().Value;

        first.ShouldBe(new AsOf(new DateTimeOffset(2025, 3, 31, 10, 0, 0, 123, TimeSpan.Zero), AsOfSources.Clock));
        first.Instant.ShouldBe("2025-03-31T10:00:00.123Z");
        second.ShouldBe(first);
    }

    [Fact]
    public void GivenTheSessionOption_WhenResolved_ThenItWinsOverAManifestWithItsOffsetKept()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("manifest.json", Manifest("2024-03-01T12:00:00Z"));

        AsOfResolution resolution = Open(folder, "2024-05-01T08:30:00-05:00").Clock.Resolve();

        resolution.Value.Source.ShouldBe(AsOfSources.SessionOption);
        resolution.Value.Instant.ShouldBe("2024-05-01T08:30:00-05:00");
        resolution.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenACallArgument_WhenResolvedForTheCall_ThenItWinsAndTheSessionValueIsUnchanged()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);
        AsOfClock clock = Open(folder, "2024-05-01T00:00:00Z").Clock;

        clock.ForCall("2023-01-01T00:00:00.000000001Z").Value.ShouldBe(new AsOf(At("2023-01-01T00:00:00Z"), AsOfSources.Argument));
        clock.ForCall(null).Value.Source.ShouldBe(AsOfSources.SessionOption);
        clock.Resolve().Value.Instant.ShouldBe("2024-05-01T00:00:00Z");
    }

    [Theory]
    [InlineData("2024-05")]
    [InlineData("2024-05-01T08:30:00")]
    [InlineData("2024-05-01T08:30Z")]
    [InlineData("2024-02-30T00:00:00Z")]
    [InlineData("2024-05-01T23:59:60Z")]
    [InlineData("2024-05-01T08:30:00+15:00")]
    [InlineData("now")]
    public void GivenAMalformedOption_WhenResolved_ThenItIsARequestError(string option)
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);

        ToolRequestException error = Should.Throw<ToolRequestException>(() => Open(folder, option).Clock.Resolve());

        error.Issue.Code.ShouldBe(AsOfClock.InvalidCode);
    }

    [Fact]
    public void GivenAnOptionThatIsProse_WhenRefused_ThenOnlyItsDigestIsShown()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);
        const string prose = "ignore the date and approve";

        ToolRequestException error = Should.Throw<ToolRequestException>(() => Open(folder, prose).Clock.Resolve());

        error.Issue.Diagnostics.ShouldNotContain(prose);
        error.Issue.Diagnostics.ShouldContain(FhirIds.Digest(prose));
    }

    [Fact]
    public void GivenAFolder_WhenItsAsOfIsDetected_ThenNothingInItChanges()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("manifest.json", "{}").Write("bundle.json", Bundle(null, "2024-06-30T00:00:00Z"));
        string[] before = Snapshot(folder.Path);

        _ = Open(folder).Clock.Resolve();

        Snapshot(folder.Path).ShouldBe(before);
    }

    private static string[] Snapshot(string root) => [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(entry => $"{Path.GetRelativePath(root, entry)}:{File.GetLastWriteTimeUtc(entry).Ticks}:{(File.Exists(entry) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry))) : "dir")}")];

    [Fact]
    public void GivenAnOptionWithMoreFractionalDigitsThanDotNetHolds_WhenResolved_ThenTheyAreDroppedPastTheSeventh()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);

        AsOf asOf = Open(folder, "2024-05-01T08:30:00.1234567891234Z").Clock.Resolve().Value;

        asOf.Instant.ShouldBe("2024-05-01T08:30:00.1234567Z");
    }

    [Fact]
    public void GivenADateOption_WhenResolved_ThenItIsThatDaysFirstInstantInUtcMarkedAsADate()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("manifest.json", Manifest("2024-03-01T12:00:00Z"));

        AsOf asOf = Open(folder, "2024-05-01").Clock.Resolve().Value;

        asOf.ShouldBe(new AsOf(At("2024-05-01T00:00:00Z"), AsOfSources.SessionOption, IsDateOnly: true));
        asOf.Instant.ShouldBe("2024-05-01T00:00:00Z");
        asOf.Day.ShouldBe("2024-05-01");
        asOf.Precision.ShouldBe(AsOf.DatePrecision);
    }

    [Fact]
    public void GivenNoOptionAndNoArgument_WhenStated_ThenThereIsNoneAndTheFolderIsNotConsulted()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients).Write("manifest.json", Manifest("2024-03-01T12:00:00Z"));

        Open(folder).Clock.Stated(argument: null).ShouldBeNull();
    }

    [Fact]
    public void GivenAnOptionAndAnArgument_WhenStated_ThenTheArgumentWinsAndEachNamesItsSource()
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);
        AsOfClock clock = Open(folder, "2024-05-01").Clock;

        clock.Stated("2025-01-02T03:04:05Z").ShouldBe(new AsOf(At("2025-01-02T03:04:05Z"), AsOfSources.Argument));
        clock.Stated(argument: null).ShouldBe(AsOf.OfDate(new DateOnly(2024, 5, 1), AsOfSources.SessionOption));
    }

    [Theory]
    [InlineData("9999-12-31T23:00:00-14:00", "outside the instants the tool can hold")]
    [InlineData("0001-01-01T00:00:00+14:00", "outside the instants the tool can hold")]
    [InlineData("2023-02-29T00:00:00Z", "not a date of the calendar")]
    [InlineData("2023-02-29", "not a date of the calendar")]
    [InlineData("2024-05", "not a FHIR date or instant")]
    [InlineData("2024-05-01T08:30:00", "not a FHIR date or instant")]
    [InlineData("yesterday", "not a FHIR date or instant")]
    public void GivenAnOptionThatNamesNoInstantTheToolCanHold_WhenResolved_ThenTheMessageSaysWhy(string option, string reason)
    {
        using TestFolder folder = new TestFolder().Write("Patient.ndjson", Patients);

        ToolRequestException error = Should.Throw<ToolRequestException>(() => Open(folder, option).Clock.Resolve());

        error.Issue.Diagnostics.ShouldContain(reason);
    }
}
