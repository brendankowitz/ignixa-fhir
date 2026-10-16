// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Views;
using Microsoft.Extensions.Logging;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>That a run is bounded in time and in the resources it reads, and that a failure of the tool is an error, not a crash.</summary>
public class SqlViewLimitsTests
{
    private static TestFolder ManyPatients(int count) =>
        new TestFolder().Write(PatientsFile, string.Join('\n', Enumerable.Range(1, count).Select(i => $$"""{"resourceType":"Patient","id":"p{{i}}","gender":"male","birthDate":"1980-01-01"}""")));

    [Fact]
    public async Task GivenMoreResourcesThanTheLimit_WhenRun_ThenItFailsBeforeReadingAnyAndSuggestsACompartment()
    {
        using TestFolder folder = Clinic();

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(ObservationView, MaxResources: 4)));

        error.Code.ShouldBe("too-many-resources");
        error.Diagnostics.ShouldContain("5 Observation resources");
        error.Diagnostics.ShouldContain("limit of 4");
        error.Diagnostics.ShouldContain("compartment Patient/<id>");
    }

    [Fact]
    public async Task GivenACompartmentUnderTheLimit_WhenRun_ThenTheLimitCountsOnlyItsResources()
    {
        using TestFolder folder = Clinic();

        ViewFacts facts = Facts(await RunAsync(folder, new ViewRequest(ObservationView, Compartment: "Patient/p2", MaxResources: 2)));

        facts.Resources.ShouldBe(2);
    }

    [Fact]
    public async Task GivenNoLimitGiven_WhenRun_ThenTheDefaultAllowsFiftyThousandResources()
    {
        ViewRunner.DefaultMaxResources.ShouldBe(50_000);
        ViewRunner.DefaultTimeoutSeconds.ShouldBe(30);
        using TestFolder folder = Clinic();

        Facts(await RunAsync(folder, new ViewRequest(PatientView))).Total.ShouldBe(3);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(null, 3601)]
    public async Task GivenALimitThatIsNotPositive_WhenRun_ThenInvalidParameterIsReported(int? maxResources, int? timeout)
    {
        using TestFolder folder = Clinic();

        Error(await RunAsync(folder, new ViewRequest(PatientView, MaxResources: maxResources, TimeoutSeconds: timeout))).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenARunThatOutlastsTheTimeout_WhenRunWithOut_ThenItIsStoppedWithAnErrorAndLeavesNothing()
    {
        using TestFolder folder = ManyPatients(3000);
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "made", "rows.csv");

        ToolEnvelope<ViewFacts?> envelope = await RunInAsync(folder.Path, new ViewRequest(PatientView, Out: path), timeout: TimeSpan.FromMilliseconds(1));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("view-timeout");
        error.Diagnostics.ShouldContain("compartment Patient/<id>");
        Directory.GetFileSystemEntries(elsewhere.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenACallerWhoCancels_WhenRun_ThenTheCancellationIsNotReportedAsATimeout()
    {
        using TestFolder folder = ManyPatients(50);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Ignixa.CliOnFhir.Views.ViewRunner.RunAsync(Open(folder), new ViewRequest(PatientView), cancelled.Token));
    }

    [Fact]
    public async Task GivenAnUnexpectedFailureOfTheTool_WhenRun_ThenItIsAnErrorEnvelopeAndLoggedInFull()
    {
        using TestFolder folder = Clinic();
        using var provider = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        // A null variable is a defect of the caller of the engine service, which no check in front of it expects.
        ToolEnvelope<ViewFacts?> envelope = await RunInAsync(folder.Path, new ViewRequest(PatientView, Variables: [null!]), factory.CreateLogger("view"));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("view-internal-error");
        error.Diagnostics.ShouldContain("NullReferenceException");
        provider.Messages.ShouldContain(message => message.Contains("failed unexpectedly", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenAnElementReaderThatDoesNotRetain_WhenAResourceIsReadTwice_ThenItIsReadAgainNotKept()
    {
        using TestFolder folder = Clinic();
        var session = Open(folder);
        var entry = session.Catalog.Entries[0];
        var retaining = new ResourceElements(session);
        var streaming = new ResourceElements(session, retainElements: false);

        retaining.Get(entry).ShouldBeSameAs(retaining.Get(entry));
        streaming.Get(entry).ShouldNotBeSameAs(streaming.Get(entry));
    }

    [Fact]
    public async Task GivenABudgetAboveTheCeiling_WhenRun_ThenTheClampWarningSuggestsViewAlternatives()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, MaxBytes: 10_000_000));

        string hint = envelope.Issues.Single(issue => issue.Code == "max-bytes-clamped").Diagnostics;
        hint.ShouldContain("count 0");
        hint.ShouldContain("distinct");
        hint.ShouldContain("out");
        hint.ShouldNotContain("fhirpath");
        hint.ShouldNotContain("perPatient");
    }

    [Fact]
    public async Task GivenAViewAcrossManyFiles_WhenRun_ThenRowsFollowCatalogOrderAndEveryFileIsRead()
    {
        using TestFolder folder = new TestFolder()
            .Write("a.ndjson", """{"resourceType":"Patient","id":"a1","gender":"male"}""" + "\n" + """{"resourceType":"Patient","id":"a2","gender":"male"}""")
            .Write("b.ndjson", """{"resourceType":"Patient","id":"b1","gender":"male"}""")
            .Write("c.json", """{"resourceType":"Patient","id":"c1","gender":"male"}""");

        ViewFacts facts = Facts(await RunAsync(folder, new ViewRequest(PatientView)));

        Cells(facts).Select(row => row[0]).ShouldBe(["a1", "a2", "b1", "c1"]);
    }
}
