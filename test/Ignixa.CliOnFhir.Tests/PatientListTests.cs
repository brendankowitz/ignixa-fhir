// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>patient_list</c>. New capability: at 523e80d no patient tool existed, so every test here fails there (it does not
/// compile), except the one guard named so.
/// </summary>
public class PatientListTests
{
    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenPatients_WhenListedByName_ThenEachHasItsCompartmentSizeDateSpanAndDomains(string layout)
    {
        using TestFolder folder = Layout(layout);

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder));

        PatientListFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Patients.ShouldBe([
            new PatientSummary("p1", "Ann B Smith", "female", "1975-03-04", 32, "2010-01-01", "2027-01-01", ["demographics", "problems", "medications", "encounters", "labs"]),
            new PatientSummary("p2", "Bo Jones", "male", "1985-06-07", 3, "2021-01-01", "2021-02-02", ["demographics", "problems", "allergies"]),
            new PatientSummary("p3", null, "other", null, 1, null, null, ["demographics"]),
        ], ignoreOrder: false, comparer: new SummaryComparer());
        facts.Sort.ShouldBe("name");
        facts.DateRule.ShouldBeNull();
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Patient/p1", "Patient/p2", "Patient/p3"]);
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenPatients_WhenListedByLatestDate_ThenTheMostRecentComesFirstAndUndatedLast()
    {
        using TestFolder folder = Ndjson().Write("late.ndjson", """
            {"resourceType":"Patient","id":"p4"}
            {"resourceType":"Encounter","id":"e40","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p4"},"period":{"start":"2024-01-01"}}
            """);

        PatientListFacts facts = PatientLister.List(Open(folder), sort: "latest").Facts.ShouldNotBeNull();

        facts.Patients.Select(patient => $"{patient.Id} {patient.LatestDate}").ShouldBe(["p1 2027-01-01", "p4 2024-01-01", "p2 2021-02-02", "p3 "]);
    }

    [Fact]
    public void GivenMorePatientsThanAPage_WhenListed_ThenOmittedAndNextGiveTheNextOffset()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder), count: 2, sort: "latest");

        envelope.Facts.ShouldNotBeNull().Patients.Select(patient => patient.Id).ShouldBe(["p1", "p2"]);
        envelope.Omitted.ShouldBe([new OmittedItem("1 more patients (offset=2)", "limit")]);
        envelope.Next.Select(call => call.Tool).ShouldBe(["patient_list", "patient_summary"]);
        envelope.Next[0].Args.ShouldBe(new Dictionary<string, object> { ["offset"] = 2, ["sort"] = "latest" }, ignoreOrder: true);
    }

    [Fact]
    public void GivenAFolderWithoutPatients_WhenListed_ThenAbsentSaysTheCatalogWasChecked()
    {
        using TestFolder folder = new TestFolder().Write("obs.ndjson", """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"}}""");

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(0);
        envelope.Absent.ShouldHaveSingleItem().Checked.ShouldEndWith(": 0 Patient resources in 1 files");
    }

    [Theory]
    [InlineData("date", "'sort' must be 'name' or 'latest', not 'date'.")]
    [InlineData("Name", "'sort' must be 'name' or 'latest', not 'Name'.")]
    public void GivenAnUnknownSort_WhenListed_ThenTheCallFails(string sort, string diagnostics)
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder), sort: sort);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldBe(diagnostics);
    }

    // Guard: the one-pass membership patient_list uses gives each patient exactly what the per-patient enumeration gives.
    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenEveryPatient_WhenMembersAreFoundInOnePass_ThenEachCompartmentEqualsItsOwnEnumeration(string layout)
    {
        using TestFolder folder = Layout(layout).Write("linked.ndjson", """
            {"resourceType":"Patient","id":"p5","link":[{"other":{"reference":"Patient/p1"},"type":"seealso"}]}
            {"resourceType":"Observation","id":"o50","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p2"},"performer":[{"reference":"Patient/p1"}]}
            {"resourceType":"Observation","id":"o51","status":"final","code":{"text":"x"},"focus":[{"reference":"Patient/p3"}]}
            """);
        FolderSession session = Open(folder);
        var source = new FolderSearchSource(session);
        HashSet<string> ids = [.. source.Resources("Patient").Select(patient => patient.Key.Id)];

        (IReadOnlyDictionary<string, List<Indexing.IndexedResource>> grouped, IReadOnlyList<string> unevaluable) = source.CompartmentMembersOf("Patient", ids);

        unevaluable.ShouldBeEmpty();
        foreach (string id in ids)
        {
            grouped[id].Select(member => member.Key).ShouldBe(source.CompartmentMembers("Patient", id).Members.Select(member => member.Key), $"Patient/{id}");
        }

        grouped["p1"].Select(member => member.Key.ToString()).ShouldContain("Patient/p5");
        grouped["p1"].Select(member => member.Key.ToString()).ShouldContain("Observation/o50");
        grouped["p2"].Select(member => member.Key.ToString()).ShouldContain("Observation/o50");

        // focus is not a Patient compartment parameter, so a reference through it is a candidate the membership rejects.
        grouped["p3"].ShouldBeEmpty();
    }

    private sealed class SummaryComparer : IEqualityComparer<PatientSummary>
    {
        public bool Equals(PatientSummary? x, PatientSummary? y) =>
            x is not null && y is not null && x with { Domains = [] } == y with { Domains = [] } && (x.Domains ?? []).SequenceEqual(y.Domains ?? []);

        public int GetHashCode(PatientSummary obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
