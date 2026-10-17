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
        facts.AsOf.ShouldBe(new AsOf(DateTimeOffset.Parse(asOf, System.Globalization.CultureInfo.InvariantCulture), AsOfSources.SessionOption));
    }

    [Fact]
    public async Task GivenNoAsOfOption_WhenRun_ThenAsOfIsTheSessionClockAndTheFactsSaySo()
    {
        using TestFolder folder = Clinic();
        var clock = new ManualTimeProvider { Now = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: clock);

        ViewFacts facts = Facts(await RunAsync(session, new ViewRequest(FortyOrOlder)));

        Cells(facts).Select(row => row[0]).ShouldBe(["p1"]);
        facts.AsOf.ShouldBe(new AsOf(clock.Now, AsOfSources.Clock));
    }

    [Fact]
    public async Task GivenAVarNamedAsOf_WhenRun_ThenItWinsWithAWarning()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(Open(folder), new ViewRequest(FortyOrOlder, Variables: ["asOf=2026-01-01T00:00:00Z"]), "2016-01-01T00:00:00Z");

        Cells(Facts(envelope)).Select(row => row[0]).ShouldBe(["p1", "p2"]);
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
}
