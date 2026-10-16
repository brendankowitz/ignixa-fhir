// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Views;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

public class SqlViewTests
{
    [Fact]
    public async Task GivenASimplePatientView_WhenRun_ThenRowsFollowTheColumnsAndCiteTheirResources()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView));

        ViewFacts facts = Facts(envelope);
        Names(facts).ShouldBe(["id", "gender", "birth_date"]);
        Cells(facts).ShouldBe([["p1", "female", "1975-03-04"], ["p2", "male", "1985-06-07"], ["p3", "male", "1990-02-03"]]);
        facts.Total.ShouldBe(3);
        facts.Resources.ShouldBe(3);
        facts.View.ShouldBe(new ViewSourceFacts("inline", "Patient", Reference: null));
        facts.Provenance.EvidenceRows.ShouldBe(ViewResponse.EvidenceRows);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/p1", "Patient/p2", "Patient/p3"]);
        envelope.Evidence.ShouldAllBe(item => item.File == PatientsFile);
        envelope.Evidence.Select(item => item.Line).ShouldBe([1, 2, 3]);
        envelope.Omitted.ShouldBeEmpty();
        envelope.Next.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenForEachOverName_WhenRun_ThenEachNameIsARowAndCitesItsPatient()
    {
        using TestFolder folder = NamedPatients();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient",
             "select":[{"column":[{"name":"id","path":"id"}],
                        "select":[{"forEach":"name","column":[{"name":"use","path":"use"},{"name":"family","path":"family"},{"name":"given","path":"given.first()"}]}]}]}
            """;

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view));

        ViewFacts facts = Facts(envelope);
        Names(facts).ShouldBe(["id", "use", "family", "given"]);
        Cells(facts).ShouldBe([["p1", "official", "Smith", "Ann"], ["p1", "maiden", "Brown", "Ann"], ["p2", null, "Jones", "Bo"]]);
        facts.Total.ShouldBe(3);
        facts.Resources.ShouldBe(2);
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/p1", "Patient/p2"]);
    }

    [Fact]
    public async Task GivenAViewWithAWhere_WhenRun_ThenOnlyMatchingResourcesProduceRows()
    {
        using TestFolder folder = Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient","where":[{"path":"gender = 'male'"}],
             "select":[{"column":[{"name":"id","path":"id"}]}]}
            """;

        ViewFacts facts = Facts(await RunAsync(folder, new ViewRequest(view)));

        Cells(facts).ShouldBe([["p2"], ["p3"]]);
        facts.Resources.ShouldBe(3);
        facts.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnExtraWhere_WhenRun_ThenItIsAndedWithTheViewsOwn()
    {
        using TestFolder folder = Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient","where":[{"path":"gender = 'male'"}],
             "select":[{"column":[{"name":"id","path":"id"}]}]}
            """;
        string filter = "birthDate > @1987-01-01";

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view, Where: filter));

        ViewFacts facts = Facts(envelope);
        Cells(facts).ShouldBe([["p3"]]);
        facts.Where.ShouldBe(filter);
    }

    [Fact]
    public async Task GivenAnExtraWhereThatIsNotBoolean_WhenRun_ThenItIsRefusedAsAnInvalidParameter()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Where: "birthDate.("));

        Error(envelope).Code.ShouldBe("invalid-parameter");
    }

    [Theory]
    [InlineData("""{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"x","path":"gender.("}]}]}""", "Syntax")]
    [InlineData("""{"resourceType":"Patient","id":"p1"}""", "not a ViewDefinition resource")]
    [InlineData("""{"resourceType":"ViewDefinition","select":[{"column":[{"name":"x","path":"id"}]}]}""", "'resource'")]
    [InlineData("""{"resourceType":"ViewDefinition","resource":"Nonsense","select":[{"column":[{"name":"x","path":"id"}]}]}""", "not a R4 resource type")]
    [InlineData("""{"resourceType":"ViewDefinition","resource":"Patient","select":[]}""", "no columns")]
    [InlineData("""{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"x","path":"%missing"}]}]}""", "undefined constant")]
    public async Task GivenAnInvalidViewDefinition_WhenRun_ThenAnInvalidViewErrorNamesTheProblem(string view, string expected)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("invalid-view");
        error.Diagnostics.ShouldContain(expected);
    }

    [Fact]
    public async Task GivenInlineTextThatIsNotJson_WhenRun_ThenInvalidViewIsReported()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest("{\"resourceType\":"));

        Error(envelope).Code.ShouldBe("invalid-view");
    }

    [Fact]
    public async Task GivenMoreRowsThanCount_WhenRun_ThenTheRestIsOmittedWithTheTotalAndTheNextPage()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Count: 2));

        ViewFacts facts = Facts(envelope);
        facts.Total.ShouldBe(5);
        facts.Returned.ShouldBe(2);
        Cells(facts).Select(row => row[0]).ShouldBe(["o1", "o2"]);
        envelope.Omitted[0].ShouldBe(new OmittedItem("3 more rows (offset=2)", "limit"));
        // No call is listed: the tool a call would name does not exist yet, and omitted says how to go on instead.
        envelope.Next.ShouldBeEmpty();
        envelope.Omitted.Count.ShouldBe(2);
        envelope.Omitted[1].What.ShouldStartWith("next: no call is listed");
        envelope.Omitted[1].What.ShouldContain("offset 2");
    }

    [Fact]
    public async Task GivenTheOffsetFromANextCall_WhenRun_ThenTheFollowingRowsAreReturned()
    {
        using TestFolder folder = Clinic();

        ViewFacts facts = Facts(await RunAsync(folder, new ViewRequest(ObservationView, Count: 2, Offset: 4)));

        Cells(facts).Select(row => row[0]).ShouldBe(["o5"]);
        facts.Offset.ShouldBe(4);
    }

    [Fact]
    public async Task GivenAnOffsetPastTheLastRow_WhenRun_ThenNoRowsAreReturnedWithAWarning()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Offset: 10));

        Facts(envelope).Rows.ShouldBeEmpty();
        envelope.Issues.ShouldContain(issue => issue.Code == "offset-beyond-rows");
        envelope.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCountZero_WhenRun_ThenOnlyTheTotalAndColumnsAreReturned()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Count: 0));

        ViewFacts facts = Facts(envelope);
        facts.Total.ShouldBe(5);
        facts.Rows.ShouldBeEmpty();
        envelope.Evidence.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCountAboveTheMaximum_WhenRun_ThenItIsClampedWithAWarningAndTheRestIsOmitted()
    {
        using TestFolder folder = new TestFolder().Write(PatientsFile, string.Join('\n', Enumerable.Range(1, 1100).Select(i => $$"""{"resourceType":"Patient","id":"p{{i}}","gender":"male"}""")));

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Count: 5000, MaxBytes: 50 * 1024));

        ViewFacts facts = Facts(envelope);
        facts.Total.ShouldBe(1100);
        facts.Returned.ShouldBe(ViewPaging.MaxCount);
        envelope.Issues.ShouldContain(issue => issue.Code == "count-clamped");
        envelope.Omitted[0].What.ShouldBe("100 more rows (offset=1000)");
    }

    [Fact]
    public async Task GivenRowsLargerThanTheBudget_WhenRun_ThenThePageIsCutToFitWithTheRestOmittedAsSizeBudget()
    {
        string family = new('x', 300);
        using TestFolder folder = new TestFolder().Write(PatientsFile, string.Join('\n', Enumerable.Range(1, 60).Select(i => $$"""{"resourceType":"Patient","id":"p{{i}}","name":[{"family":"{{family}}"}]}""")));
        const string view = """{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"id","path":"id"},{"name":"family","path":"name.first().family"}]}]}""";

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view, MaxBytes: 4096));

        ViewFacts facts = Facts(envelope);
        facts.Total.ShouldBe(60);
        facts.Returned.ShouldNotBeNull().ShouldBeGreaterThan(0);
        facts.Returned.Value.ShouldBeLessThan(60);
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(4096);
        OmittedItem cut = envelope.Omitted[0];
        cut.Why.ShouldBe("size-budget");
        cut.What.ShouldBe($"{60 - facts.Returned} more rows (offset={facts.Returned})");
        envelope.Omitted[1].What.ShouldContain($"offset {facts.Returned}");
    }

    [Fact]
    public async Task GivenACodedObservationView_WhenRun_ThenCellsHoldPrimitivesOnly()
    {
        using TestFolder folder = Clinic();

        ViewFacts facts = Facts(await RunAsync(folder, new ViewRequest(ObservationView, Count: 1)));

        facts.Rows.ShouldNotBeNull()[0].ShouldBe(["o1", "Patient/p1", "8867-4", 60m]);
    }

    [Fact]
    public async Task GivenBundleReferences_WhenRun_ThenCellsHoldTheTypeAndIdTheyResolveToAndResolveIsWarnedAbout()
    {
        using TestFolder folder = ProjectionFixture.Synthea();
        const string view = """{"resourceType":"ViewDefinition","resource":"MedicationRequest","select":[{"column":[{"name":"id","path":"id"},{"name":"patient","path":"subject.reference"},{"name":"prescriber","path":"requester.reference"},{"name":"family","path":"requester.resolve().name.first().family"}]}]}""";

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view, Where: "id = 'm1'"));

        // resolve() reaches nothing in a view run, and the response says so rather than leaving a null to be read as data.
        Cells(Facts(envelope)).ShouldBe([["m1", "Patient/pa", "Practitioner/pr1", null]]);
        envelope.Issues.Single(issue => issue.Code == "resolve-unsupported").Severity.ShouldBe("warning");
    }

    [Fact]
    public async Task GivenAColumnNullInEveryRow_WhenRun_ThenAColumnEmptyWarningNamesIt()
    {
        using TestFolder folder = Clinic();
        const string view = """{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"id","path":"id"},{"name":"surname","path":"name"},{"name":"nothing","path":"deceased.ofType(boolean)"}]}]}""";

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view));

        IssueItem warning = envelope.Issues.Single(issue => issue.Code == "column-empty");
        warning.Severity.ShouldBe("warning");
        warning.Diagnostics.ShouldContain("surname, nothing");
        warning.Diagnostics.ShouldContain("3 rows");
    }

    [Fact]
    public async Task GivenNoResourcesOfTheViewsType_WhenRun_ThenThereAreNoRowsButAnInvalidViewStillFails()
    {
        using TestFolder folder = new TestFolder().Write(PatientsFile, """{"resourceType":"Patient","id":"p1"}""");

        ViewFacts empty = Facts(await RunAsync(folder, new ViewRequest(ObservationView)));
        ToolEnvelope<ViewFacts?> unknownFunction = await RunAsync(folder, new ViewRequest("""{"resourceType":"ViewDefinition","resource":"Observation","select":[{"column":[{"name":"x","path":"id.nosuchfunction()"}]}]}"""));

        empty.Total.ShouldBe(0);
        empty.Resources.ShouldBe(0);
        empty.Rows.ShouldBeEmpty();
        IssueItem error = Error(unknownFunction);
        error.Code.ShouldBe("view-evaluation-failed");
        error.Diagnostics.ShouldContain("nosuchfunction");
    }

    [Fact]
    public async Task GivenATopLevelUnionAll_WhenRun_ThenRowsAreOrderedAcrossResourcesAndProvenanceIsSaidToBeUnavailable()
    {
        using TestFolder folder = Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient",
             "select":[{"unionAll":[{"column":[{"name":"id","path":"id"},{"name":"kind","path":"'gender'"}]},{"column":[{"name":"id","path":"id"},{"name":"kind","path":"'born'"}]}]}]}
            """;

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view));

        ViewFacts facts = Facts(envelope);
        Cells(facts).Select(row => $"{row[0]}/{row[1]}").ShouldBe(["p1/gender", "p2/gender", "p3/gender", "p1/born", "p2/born", "p3/born"]);
        facts.Provenance.ShouldBe(new ViewProvenance(EvidenceRows: null, Unavailable: ViewResponse.UnionAllOrder));
        envelope.Evidence.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAVariableOverridingAConstant_WhenRun_ThenItsValueIsUsed()
    {
        using TestFolder folder = Clinic();
        const string view = """
            {"resourceType":"ViewDefinition","resource":"Patient",
             "constant":[{"name":"cutoff","valueDate":"1980-01-01"}],
             "where":[{"path":"birthDate < %cutoff"}],
             "select":[{"column":[{"name":"id","path":"id"}]}]}
            """;

        ViewFacts declared = Facts(await RunAsync(folder, new ViewRequest(view)));
        ToolEnvelope<ViewFacts?> overridden = await RunAsync(folder, new ViewRequest(view, Variables: ["cutoff=1988-01-01"]));

        Cells(declared).ShouldBe([["p1"]]);
        Cells(Facts(overridden)).ShouldBe([["p1"], ["p2"]]);
        Facts(overridden).Variables.ShouldBe(new Dictionary<string, string> { ["cutoff"] = "1988-01-01" });
    }

    [Fact]
    public async Task GivenAVariableNamingNoConstant_WhenRun_ThenAWarningSaysItChangedNothing()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Variables: ["cutoff=1988-01-01"]));

        Facts(envelope).Total.ShouldBe(3);
        envelope.Issues.Single(issue => issue.Code == "variable-not-declared").Diagnostics.ShouldContain("'cutoff'");
    }

    [Theory]
    [InlineData("rowIndex=1")]
    [InlineData("noequals")]
    [InlineData("=value")]
    public async Task GivenAVariableTheEngineOrTheSyntaxRefuses_WhenRun_ThenInvalidParameterIsReported(string variable)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Variables: [variable]));

        Error(envelope).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenACompartment_WhenRun_ThenOnlyThatPatientsResourcesAreRead()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Compartment: "Patient/p1"));

        ViewFacts facts = Facts(envelope);
        Cells(facts).Select(row => row[0]).ShouldBe(["o1", "o2", "o5"]);
        facts.Resources.ShouldBe(3);
        facts.Compartment.ShouldBe("Patient/p1");
    }

    [Fact]
    public async Task GivenAPatientViewInACompartment_WhenRun_ThenTheCompartmentsPatientIsTheOnlyRow()
    {
        using TestFolder folder = Clinic();

        ViewFacts facts = Facts(await RunAsync(folder, new ViewRequest(PatientView, Compartment: "Patient/p2")));

        Cells(facts).ShouldBe([["p2", "male", "1985-06-07"]]);
    }

    [Theory]
    [InlineData("Patient/nobody", "unknown-patient")]
    [InlineData("Encounter/e1", "invalid-parameter")]
    [InlineData("p1", "invalid-parameter")]
    public async Task GivenACompartmentTheFolderCannotResolve_WhenRun_ThenItIsAnError(string compartment, string code)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Compartment: compartment));

        Error(envelope).Code.ShouldBe(code);
    }

    [Fact]
    public async Task GivenACohortQuestion_WhenAViewOfConditionsIsCountedByDistinctPatient_ThenOneCallAnswersIt()
    {
        using TestFolder folder = ClinicWithConditions();
        const string filter = "code.coding.where(system = 'http://snomed.info/sct' and code = '44054006').exists()";

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ConditionView, Where: filter, Count: 0, Distinct: "patient"));

        ViewFacts facts = Facts(envelope);
        facts.Total.ShouldBe(3);
        facts.Distinct.ShouldBe(new DistinctFacts("patient", Values: 2, Nulls: 0));
        facts.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenADistinctColumnTheViewDoesNotHave_WhenRun_ThenTheColumnsAreNamed()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Distinct: "nope"));

        IssueItem error = Error(envelope);
        error.Code.ShouldBe("invalid-parameter");
        error.Diagnostics.ShouldContain("id, gender, birth_date");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public async Task GivenANegativeCountOrOffset_WhenRun_ThenInvalidParameterIsReported(int count, int offset)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Count: count, Offset: offset));

        Error(envelope).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenAFolderThatDoesNotExist_WhenRun_ThenFolderUnavailableIsReported()
    {
        string missing = Path.Combine(Path.GetTempPath(), "cli-on-fhir-tests", Guid.NewGuid().ToString("N"));
        var session = new Ignixa.CliOnFhir.Session.FolderSession(missing, new Ignixa.Specification.Generated.R4CoreSchemaProvider(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        ToolEnvelope<ViewFacts?> envelope = await ViewRunner.RunAsync(session, new ViewRequest(PatientView), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.Single().Code.ShouldBe("folder-unavailable");
    }
}
