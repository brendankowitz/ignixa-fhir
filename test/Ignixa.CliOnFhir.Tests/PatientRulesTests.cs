// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.Specification.Generated;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The domain rules after the Task 6 review. Each test is a regression, failing at 1bfaad9, unless it is labelled a guard.
/// </summary>
public class PatientRulesTests
{
    // Regression (fix round 1): at 1bfaad9 an active Condition that was refuted or entered in error counted as a problem.
    [Fact]
    public void GivenActiveConditionsRefutedOrEnteredInError_WhenTheRootIsRead_ThenTheyAreExcluded()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);

        DomainStub problems = ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull().Problems;

        problems.Items.Select(item => item.Reference).ShouldBe(["Condition/c3"]);
        problems.Excluded.ShouldBe(2);
        problems.Rule.ShouldNotBeNull().ShouldContain("verificationStatus (condition-ver-status) not refuted or entered-in-error");
    }

    // Regression (fix round 1): at 1bfaad9 an order not to give a medication (doNotPerform) was listed as active.
    [Fact]
    public void GivenADoNotPerformRequest_WhenTheRootIsRead_ThenItIsNotAnActiveMedication()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);

        DomainStub medications = ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull().Medications;

        // ms1 (2023) is newer than m1 (2021); m2 is doNotPerform and ms2 entered in error.
        medications.Items.Select(item => item.Reference).ShouldBe(["MedicationStatement/ms1", "MedicationRequest/m1"]);
        medications.Excluded.ShouldBe(2);
        medications.Rule.ShouldNotBeNull().ShouldContain("not doNotPerform");
    }

    // Regression (fix round 1): at 1bfaad9 encounters sorted by period end, so the open 2020 period (e4) came first.
    [Fact]
    public void GivenAnOpenPeriodAndMixedPrecisionStarts_WhenTheRootIsRead_ThenEncountersAreNewestFirstByTheStartTheyShow()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);

        DomainStub encounters = ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull().Encounters;

        // e2 starts 2023-01-01T05:00Z, e3 at the first moment of 2023-01-01; e1 is cancelled.
        encounters.Items.Select(item => $"{item.Reference} {item.Date}").ShouldBe([
            "Encounter/e2 2023-01-01T10:00:00+05:00",
            "Encounter/e3 2023-01-01",
            "Encounter/e4 2020-01-01"]);
        encounters.Excluded.ShouldBe(1);
    }

    // Regression (fix round 1): at 1bfaad9 the latest glucose was the year-precision "2024" value, whose range ends last.
    [Fact]
    public void GivenAYearPrecisionValue_WhenTheLabSeriesIsRead_ThenTheLatestIsTheNewestByItsEarliestInstant()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);
        Session.FolderSession session = Open(folder);

        ContextItem glucose = ContextRoot.Get(session, "p1").Facts.ShouldNotBeNull().Labs.Items.Single(item => item.Code == "http://loinc.org|2339-0");
        NodeFacts series = ContextExpander.Expand(session, "p1", "labs/http://loinc.org|2339-0").Facts.ShouldNotBeNull();

        // 2024-01-01T08:00+05:00 is 03:00Z, after 02:00Z; "2024" counts from its first moment.
        glucose.Reference.ShouldBe("Observation/o2");
        glucose.Trend.ShouldBe("n/a");
        series.Items.Select(item => item.Reference).ShouldBe(["Observation/o2", "Observation/o3", "Observation/o4", "Observation/o1"]);
    }

    // Guard: patient_list's latestDate is read on the same basis (the main date's earliest instant).
    [Fact]
    public void GivenMixedPrecisionDates_WhenPatientsAreListed_ThenLatestDateIsTheNewestByTheSameBasis()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);

        PatientSummary patient = PatientLister.List(Open(folder)).Facts.ShouldNotBeNull().Patients.ShouldHaveSingleItem();

        patient.LatestDate.ShouldBe("2024-01-01T08:00:00+05:00");
        patient.EarliestDate.ShouldBe("2010-05-01");
    }

    // Regression (fix round 1): at 1bfaad9 a planned encounter that started before asOf counted as recent.
    [Fact]
    public void GivenAPlannedEncounter_WhenTheRootIsRead_ThenItIsNotRecent()
    {
        using TestFolder folder = new TestFolder().Write("chart.ndjson", """
            {"resourceType":"Patient","id":"p1"}
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"},"period":{"start":"2024-01-01"}}
            {"resourceType":"Encounter","id":"e2","status":"planned","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"},"period":{"start":"2025-01-01"}}
            """);

        DomainStub encounters = ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull().Encounters;

        encounters.Items.ShouldHaveSingleItem().Reference.ShouldBe("Encounter/e1");
        encounters.Excluded.ShouldBe(1);
        encounters.Rule.ShouldNotBeNull().ShouldContain("status not planned, cancelled or entered-in-error");
    }

    // Regression (fix round 1): at 1bfaad9 "<5" and "5" were compared as numbers.
    [Fact]
    public void GivenAComparatorAmongTheLastThreeValues_WhenTheTrendIsRead_ThenItIsNotApplicable()
    {
        using TestFolder folder = new TestFolder().Write("chart.ndjson", """
            {"resourceType":"Patient","id":"p1"}
            {"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"2339-0"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2023-01-01","valueQuantity":{"value":10,"unit":"mg/dL"}}
            {"resourceType":"Observation","id":"o2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"2339-0"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2024-01-01","valueQuantity":{"value":5,"comparator":"<","unit":"mg/dL"}}
            """);

        ContextItem lab = ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull().Labs.Items.ShouldHaveSingleItem();

        lab.Trend.ShouldBe("n/a");
        lab.Comparator.ShouldBe("<");
    }

    // Regression (fix round 1): at 1bfaad9 R4B, whose paths are R4's, got the unsupported-version warning and no rules.
    [Fact]
    public void GivenAFolderReadAsR4B_WhenTheRootIsRead_ThenTheR4RulesApplyWithoutAWarning()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder, schema: new R4BCoreSchemaProvider()), "p1");

        envelope.Issues.ShouldNotContain(issue => issue.Code == "domain-rule-unsupported-version");
        envelope.Facts.ShouldNotBeNull().Problems.Items.Select(item => item.Reference).ShouldBe(["Condition/c3"]);
    }

    // Regression (fix round 1): at 1bfaad9 demographics printed "deceased": null where every other record omits nulls.
    [Fact]
    public void GivenNoDeceasedValue_WhenTheRootIsRead_ThenDemographicsOmitIt()
    {
        using TestFolder folder = ReviewFixture.Folder(ReviewFixture.Chart);

        RootDigestFacts facts = ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull();

        JsonSerializer.Serialize(facts.Demographics, EnvelopeJson.Compact).ShouldNotContain("deceased");
    }
}
