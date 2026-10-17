// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Views;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A view reads the session's asOf as %asOf (Task 12): at 25f44c1d a view naming %asOf is invalid-view (undefined constant).</summary>
public class SqlViewAsOfTests
{
    // Patients who were 40 or older at asOf: p1 (1975) and p2 (1985) on 2026-01-01, p1 alone on 2016-01-01.
    private const string FortyOrOlder = """
        {"resourceType":"ViewDefinition","resource":"Patient","where":[{"path":"birthDate <= (%asOf - 40 years)"}],
         "select":[{"column":[{"name":"id","path":"id"},{"name":"as_of","path":"%asOf.toString()"}]}]}
        """;

    [Theory]
    [InlineData("2026-01-01T00:00:00Z", new[] { "p1", "p2" })]
    [InlineData("2016-01-01T00:00:00Z", new[] { "p1" })]
    public async Task GivenAViewWhereOnAsOf_WhenRunAsOfAnInstant_ThenItsRowsAreThoseOfThatInstant(string asOf, string[] expected)
    {
        using TestFolder folder = Clinic();

        ViewFacts facts = Facts(await RunAsync(Open(folder), new ViewRequest(FortyOrOlder), asOf));

        Cells(facts).Select(row => row[0]).ShouldBe(expected);
        Cells(facts).Select(row => row[1]).Distinct().ShouldBe([asOf]);
        facts.AsOf.ShouldBe(new AsOfFacts(asOf, AsOfSources.SessionOption));
    }

    [Fact]
    public async Task GivenNoAsOfOption_WhenRun_ThenAsOfIsTheSessionClockAndTheFactsSaySo()
    {
        using TestFolder folder = Clinic();
        var clock = new ManualTimeProvider { Now = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: clock);

        ViewFacts facts = Facts(await RunAsync(session, new ViewRequest(FortyOrOlder)));

        Cells(facts).Select(row => row[0]).ShouldBe(["p1"]);
        facts.AsOf.ShouldBe(new AsOfFacts("2016-01-01T00:00:00Z", AsOfSources.Clock));
    }

    [Fact]
    public async Task GivenAVarNamedAsOf_WhenRun_ThenItWinsWithAWarning()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(FortyOrOlder, Variables: ["asOf=2026-01-01T00:00:00Z"]), "2016-01-01T00:00:00Z");

        Cells(Facts(envelope)).Select(row => row[0]).ShouldBe(["p1", "p2"]);
        Facts(envelope).AsOf.ShouldBe(new AsOfFacts("2026-01-01T00:00:00Z", AsOfSources.Variable));
        envelope.Issues.ShouldContain(issue => issue.Code == "as-of-overridden" && issue.Severity == "warning");
        envelope.Issues.ShouldNotContain(issue => issue.Code == "variable-not-declared");
    }

    [Fact]
    public async Task GivenAViewDeclaringItsOwnAsOf_WhenRun_ThenItsConstantIsUsedWithAWarning()
    {
        using TestFolder folder = Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient","constant":[{"name":"asOf","valueDateTime":"2016-01-01T00:00:00Z"}],
             "where":[{"path":"birthDate <= (%asOf - 40 years)"}],"select":[{"column":[{"name":"id","path":"id"}]}]}
            """;

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(view), "2026-01-01T00:00:00Z");

        Cells(Facts(envelope)).Select(row => row[0]).ShouldBe(["p1"]);
        Facts(envelope).AsOf.ShouldBe(new AsOfFacts("2016-01-01T00:00:00Z", AsOfSources.ViewConstant));
        envelope.Issues.ShouldContain(issue => issue.Code == "as-of-shadowed" && issue.Severity == "warning");
    }

    [Fact]
    public async Task GivenAViewThatDoesNotUseAsOf_WhenRun_ThenNothingChangesButTheFactsStillStateAsOf()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(PatientView), "2026-01-01T00:00:00Z");

        Facts(envelope).Total.ShouldBe(3);
        Facts(envelope).AsOf.Instant.ShouldBe("2026-01-01T00:00:00Z");
        envelope.Issues.ShouldNotContain(issue => issue.Code.StartsWith("as-of", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAMalformedAsOf_WhenRun_ThenItIsARequestError()
    {
        using TestFolder folder = Clinic();

        Error(await RunAsync(Open(folder), new ViewRequest(PatientView), "2026-01-01")).Code.ShouldBe(AsOfClock.InvalidCode);
    }

    [Theory]
    [InlineData("asOf=2026-01-01")]
    [InlineData("asOf=yesterday")]
    [InlineData("asOfDate=2026-01-01T00:00:00Z")]
    [InlineData("asOfDate=2026-02-30")]
    public async Task GivenAVarOfTheClockThatIsNotOfItsType_WhenRun_ThenItIsARequestError(string variable)
    {
        using TestFolder folder = Clinic();

        Error(await RunAsync(Open(folder), new ViewRequest(FortyOrOlder, Variables: [variable]), "2026-01-01T00:00:00Z")).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenAViewOnAsOfDate_WhenRun_ThenItIsTheAsOfDayInTheAsOfOffset()
    {
        using TestFolder folder = Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient","where":[{"path":"birthDate <= (%asOfDate - 40 years)"}],
             "select":[{"column":[{"name":"id","path":"id"},{"name":"day","path":"%asOfDate.toString()"}]}]}
            """;

        // In UTC this instant is still 2015-12-31; in its own offset it is 2016-01-01.
        ViewFacts facts = Facts(await RunAsync(Open(folder), new ViewRequest(view), "2016-01-01T00:30:00+05:00"));

        Cells(facts).Select(row => row[0]).ShouldBe(["p1"]);
        Cells(facts).Select(row => row[1]).Distinct().ShouldBe(["2016-01-01"]);
    }

    [Fact]
    public async Task GivenDateTimesWithoutAZone_WhenAViewComparesThemWithAsOf_ThenAWarningCountsThemAndAsOfDateFindsThem()
    {
        using TestFolder folder = new TestFolder().Write("encounters.ndjson", """
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"period":{"start":"2025-03-01T08:00:00.0000000","end":"2025-03-01T09:00:00"}}
            {"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"period":{"start":"2025-04-01T08:00:00Z"}}
            {"resourceType":"Encounter","id":"e3","status":"finished","class":{"code":"AMB"},"period":{"start":"2025-05-01"}}
            """);
        const string onAsOf = """{"resourceType":"ViewDefinition","resource":"Encounter","where":[{"path":"period.start <= %asOf"}],"select":[{"column":[{"name":"id","path":"id"}]}]}""";
        const string onDay = """{"resourceType":"ViewDefinition","resource":"Encounter","where":[{"path":"period.start <= %asOfDate"}],"select":[{"column":[{"name":"id","path":"id"}]}]}""";

        ToolEnvelope<ViewFacts?> asOf = await RunAsync(Open(folder), new ViewRequest(onAsOf), "2026-01-01T00:00:00Z");
        ToolEnvelope<ViewFacts?> day = await RunAsync(Open(folder), new ViewRequest(onDay), "2026-01-01T00:00:00Z");

        Cells(Facts(asOf)).Select(row => row[0]).ShouldBe(["e2", "e3"]);
        IssueItem warning = asOf.Issues.Single(issue => issue.Code == "as-of-zone-missing");
        warning.Severity.ShouldBe("warning");
        warning.Diagnostics.ShouldStartWith("2 dateTime values in 1 of the 3 resources in scope");
        Cells(Facts(day)).Select(row => row[0]).ShouldBe(["e1", "e2", "e3"]);
        day.Issues.ShouldNotContain(issue => issue.Code == "as-of-zone-missing");
    }

    [Fact]
    public async Task GivenDateTimesWithAZone_WhenAViewComparesThemWithAsOf_ThenThereIsNoWarning()
    {
        using TestFolder folder = new TestFolder().Write("encounters.ndjson", """
            {"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"period":{"start":"2025-04-01T08:00:00Z"}}
            """);

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest("""{"resourceType":"ViewDefinition","resource":"Encounter","where":[{"path":"period.start <= %`asOf`"}],"select":[{"column":[{"name":"id","path":"id"}]}]}"""), "2026-01-01T00:00:00Z");

        Cells(Facts(envelope)).Select(row => row[0]).ShouldBe(["e2"]);
        envelope.Issues.ShouldNotContain(issue => issue.Code == "as-of-zone-missing");
    }
}
