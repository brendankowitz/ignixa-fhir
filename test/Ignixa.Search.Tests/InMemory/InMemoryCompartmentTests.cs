// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// Compartment membership (<see cref="InMemoryCompartmentMembership"/>) and compartment search through
/// <see cref="InMemorySearchEngine"/>, over R4 resources indexed with Ignixa's own indexer. New capability: at 2e56e3f
/// <c>VisitCompartment</c> threw for every compartment search and the membership type did not exist.
/// </summary>
public class InMemoryCompartmentTests
{
    private static readonly InMemoryCompartmentMembership R4 = Membership(FhirVersion.R4);

    private static IndexedTestSource Clinic() => new(
        """{"resourceType":"Patient","id":"pat-1","gender":"male","link":[{"other":{"reference":"Patient/pat-2"},"type":"seealso"}]}""",
        """{"resourceType":"Patient","id":"pat-2","gender":"female"}""",
        """{"resourceType":"Organization","id":"org-1","name":"Acme"}""",
        """{"resourceType":"Observation","id":"obs-subject","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/pat-1"}}""",
        """{"resourceType":"Observation","id":"obs-performer","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/pat-2"},"performer":[{"reference":"Patient/pat-1"}]}""",
        """{"resourceType":"Observation","id":"obs-other","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/pat-2"}}""",
        """{"resourceType":"Observation","id":"obs-elsewhere","status":"final","code":{"text":"x"},"subject":{"reference":"http://elsewhere.example.org/fhir/Patient/pat-1"}}""",
        """{"resourceType":"Observation","id":"obs-focus","status":"final","code":{"text":"x"},"focus":[{"reference":"Patient/pat-1"}]}""",
        """{"resourceType":"Encounter","id":"enc-1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/pat-1"}}""");

    [Theory]
    [InlineData("Observation", "", "obs-subject,obs-performer")]
    [InlineData("Observation", "status=final", "obs-subject,obs-performer")]
    [InlineData("Observation", "subject=Patient/pat-2", "obs-performer")]
    [InlineData("Encounter", "", "enc-1")]
    [InlineData("Organization", "", "")]
    public void GivenAPatientCompartment_WhenSearched_ThenOnlyResourcesReferencingThePatientThroughTheDefinitionsParametersMatch(string resourceType, string query, string expected)
    {
        // obs-other references another patient, obs-elsewhere a patient on another server, obs-focus the patient through a
        // parameter the R4 Patient compartment does not list; Organization is never in a Patient compartment.
        IndexedTestSource source = Clinic();

        string[] ids = Search(source, resourceType, query, new CompartmentSearchExpression("Patient", "pat-1"));

        ids.ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
    }

    [Fact]
    public void GivenTheR4PatientCompartment_WhenThePatientItselfIsTested_ThenOnlyAPatientLinkingToItIsAMember()
    {
        // R4 lists only 'link' for Patient, and no {def}: the compartment's own patient is not in it by definition.
        IndexedTestSource source = Clinic();

        Search(source, "Patient", string.Empty, new CompartmentSearchExpression("Patient", "pat-2")).ShouldBe(["pat-1"]);
        Search(source, "Patient", string.Empty, new CompartmentSearchExpression("Patient", "pat-1")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FhirVersion.R4, "Encounter", true)]
    [InlineData(FhirVersion.R4, "Patient", false)]
    [InlineData(FhirVersion.R5, "Patient", true)]
    [InlineData(FhirVersion.Stu3, "Encounter", true)]
    public void GivenACompartmentsOwnResource_WhenTested_ThenItIsAMemberExactlyWhenTheDefinitionListsDef(FhirVersion version, string compartmentType, bool expected)
    {
        InMemoryCompartmentMembership membership = Membership(version);

        bool member = membership.IsInCompartment(new ResourceKey(compartmentType, "x1"), [], compartmentType, "x1");
        bool other = membership.IsInCompartment(new ResourceKey(compartmentType, "x2"), [], compartmentType, "x1");

        member.ShouldBe(expected);
        other.ShouldBeFalse();
    }

    [Fact]
    public void GivenCompartmentFilteredResourceTypes_WhenSearched_ThenOtherTypesDoNotMatch()
    {
        IndexedTestSource source = Clinic();

        Search(source, "Observation", string.Empty, new CompartmentSearchExpression("Patient", "pat-1", new HashSet<string> { "Encounter" })).ShouldBeEmpty();
        Search(source, "Encounter", string.Empty, new CompartmentSearchExpression("Patient", "pat-1", new HashSet<string> { "Encounter" })).ShouldBe(["enc-1"]);
    }

    [Fact]
    public void GivenAnUnknownCompartmentType_WhenSearchedOrTested_ThenTheRequestIsRejected()
    {
        IndexedTestSource source = Clinic();

        Should.Throw<BadSearchRequestException>(() => Search(source, "Observation", string.Empty, new CompartmentSearchExpression("Organization", "org-1")))
            .Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("'Organization' is not a compartment type");
        Should.Throw<ArgumentException>(() => R4.IsInCompartment(new ResourceKey("Observation", "o"), [], "patient", "pat-1"));
    }

    // Guard: compartment search stays opt-in; at 2e56e3f it always threw, and it still throws without compartment definitions.
    [Fact]
    public void GivenAnEngineWithoutCompartmentDefinitions_WhenACompartmentIsSearched_ThenItFailsAsNotSupported()
    {
        IndexedTestSource source = Clinic();
        var options = new SearchOptions { ResourceType = "Observation", Expression = new CompartmentSearchExpression("Patient", "pat-1") };

        Should.Throw<SearchOperationNotSupportedException>(() => new InMemorySearchEngine(source, InMemorySearchFeatures.All).Search(options));
        Should.Throw<SearchOperationNotSupportedException>(() => options.Expression.AcceptVisitor(new SearchQueryInterpreter(), default)(source.Enumerate("Observation")).ToList());
    }

    [Fact]
    public void GivenATypeWhoseCompartmentParametersCannotBeEvaluated_WhenSearched_ThenItFailsBeforeReadingAnyResource()
    {
        // STU3 lists chained codes for AuditEvent; the source holds no AuditEvent, so a data-dependent check would pass.
        InMemoryCompartmentMembership stu3 = Membership(FhirVersion.Stu3);
        var source = new ListSearchSource([]);
        var options = new SearchOptions { ResourceType = "AuditEvent", Expression = new CompartmentSearchExpression("Patient", "p1") };

        SearchOperationNotSupportedException ex = Should.Throw<SearchOperationNotSupportedException>(() => new InMemorySearchEngine(source, InMemorySearchFeatures.None, stu3).Search(options));

        ex.Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("agent.patient, entity.patient");
        Should.Throw<SearchOperationNotSupportedException>(() => stu3.IsInCompartment(new ResourceKey("AuditEvent", "a1"), [], "Patient", "p1"));
    }

    /// <summary>
    /// Census: every code a shipped CompartmentDefinition lists, other than <c>{def}</c>, is a reference search parameter of
    /// its type, except these STU3 chained codes. A definition change that adds an unevaluable code fails here instead of
    /// silently narrowing membership.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.Stu3, "Patient/AuditEvent: agent.patient, entity.patient|Patient/Provenance: target.patient, target.subject")]
    [InlineData(FhirVersion.R4, "")]
    [InlineData(FhirVersion.R4B, "")]
    [InlineData(FhirVersion.R5, "")]
    [InlineData(FhirVersion.R6, "")]
    public void GivenTheShippedCompartmentDefinitions_WhenResolved_ThenOnlyTheKnownCodesAreUnevaluable(FhirVersion version, string expected)
    {
        InMemoryCompartmentMembership membership = Membership(version);

        string[] unevaluable = [.. membership.CompartmentTypes
            .SelectMany(compartment => membership.ResourceTypes(compartment).Select(type => (compartment, type, codes: membership.UnevaluableParameters(compartment, type))))
            .Where(rule => rule.codes.Count > 0)
            .Select(rule => $"{rule.compartment}/{rule.type}: {string.Join(", ", rule.codes)}")];

        unevaluable.ShouldBe(expected.Length == 0 ? [] : expected.Split('|'));
    }

    private static InMemoryCompartmentMembership Membership(FhirVersion version) => new(
        new CompartmentDefinitionManager(version),
        new SearchParameterDefinitionManager(version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance));

    /// <summary>Searches <paramref name="resourceType"/> for <paramref name="query"/> within <paramref name="compartment"/>, as the server's compartment handler combines them.</summary>
    private static string[] Search(IndexedTestSource source, string resourceType, string query, CompartmentSearchExpression compartment)
    {
        SearchOptions parsed = SearchCorpus.Options(resourceType, query);
        var options = new SearchOptions(parsed)
        {
            Expression = parsed.Expression is null ? compartment : Expression.And(compartment, parsed.Expression),
        };

        return [.. new InMemorySearchEngine(source, InMemorySearchFeatures.All, R4).Search(options).Select(match => match.Location.Id)];
    }
}
