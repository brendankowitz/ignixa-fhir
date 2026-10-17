// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>context_find</c>. New capability: at 523e80d no patient tool existed, so every test here fails there (it does not
/// compile).
/// </summary>
public class ContextFindTests
{
    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenABareTokenThatIsACodeAndAText_WhenFound_ThenCodeMatchesComeBeforeTextMatches(string layout)
    {
        // e2's type has the code 'asthma'; c1's text is 'Asthma'. Conditions come before Encounters in compartment order.
        using TestFolder folder = Layout(layout);

        ToolEnvelope<FindFacts?> envelope = ContextFinder.Find(Open(folder), "p1", "asthma");

        FindFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Items.Select(item => $"{item.Reference} {item.MatchedBy}").ShouldBe(["Encounter/e2 code", "Condition/c1 text"]);
        facts.NodeKeys.ShouldBe(["encounter/Encounter-e2", "problem/Condition-c1"]);
        facts.Resources.Select(resource => (string)resource["id"]!).ShouldBe(["e2", "c1"]);
        facts.Rule.ShouldStartWith("code (any system), then text against each resource's main code");
    }

    [Fact]
    public void GivenSystemAndCode_WhenFound_ThenOnlyThatCodeMatchesAndNoTextIsSearched()
    {
        using TestFolder folder = Ndjson();

        FindFacts facts = ContextFinder.Find(Open(folder), "p1", "http://snomed.info/sct|59621000").Facts.ShouldNotBeNull();

        // m2 gives the code only as its reason, which is not its main code.
        facts.Items.ShouldHaveSingleItem().Reference.ShouldBe("Condition/c2");
        facts.Rule.ShouldStartWith("code (in that system)");
    }

    [Fact]
    public void GivenALabCode_WhenFound_ThenEachObservationIsAMatchAndTheSeriesKeyIsListedOnce()
    {
        using TestFolder folder = Ndjson();

        FindFacts facts = ContextFinder.Find(Open(folder), "p1", "8480-6").Facts.ShouldNotBeNull();

        facts.Items.Select(item => item.Reference).ShouldBe(["Observation/o4", "Observation/o5", "Observation/o6"]);
        facts.NodeKeys.ShouldBe(["labs/http://loinc.org|8480-6"]);
    }

    [Fact]
    public void GivenAnObservationInNoLabSeries_WhenFound_ThenItHasNoNodeKey()
    {
        // The survey is not a lab, so labs/...|44249-1 cannot be expanded and is not handed out.
        using TestFolder folder = Ndjson();

        FindFacts facts = ContextFinder.Find(Open(folder), "p1", "44249-1").Facts.ShouldNotBeNull();

        facts.Items.ShouldHaveSingleItem().NodeKey.ShouldBeNull();
        facts.NodeKeys.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoMatch_WhenFound_ThenAbsentSaysWhatWasSearched()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<FindFacts?> envelope = ContextFinder.Find(Open(folder), "p1", "migraine headache");

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(0);
        AbsentItem absent = envelope.Absent.ShouldHaveSingleItem();
        absent.What.ShouldBe("resources matching 'migraine headache'");
        absent.Checked.ShouldBe("main codes of the 31 Condition, Encounter, MedicationRequest, Observation resources in Patient/p1's compartment");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://loinc.org|")]
    public void GivenAnEmptyQuery_WhenFound_ThenTheCallFails(string query)
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<FindFacts?> envelope = ContextFinder.Find(Open(folder), "p1", query);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }
}
