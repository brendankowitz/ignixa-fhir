// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

/// <summary>
/// Direct coverage of <see cref="ElementTreeEnumerator"/>. The enumerator is intentionally broad: it
/// yields EVERY primitive string-valued leaf with its schema-correct
/// <see cref="MutationTarget.InstanceType"/> and required-binding flag. Eligibility gating (free-text
/// vs bound code vs infra key) lives in the strategies, not the enumerator. These tests lock down the
/// schema facts strategies depend on, the shadow-primitive handling, choice-element typing, array
/// indexing, and the non-string exclusion.
/// </summary>
public class ElementTreeEnumeratorTests
{
    private static readonly IFhirSchemaProvider Schema = EdgeCaseTargetFactory.Schema;

    private static IReadOnlyList<MutationTarget> Enumerate(string json)
        => ElementTreeEnumerator.Enumerate(ResourceJsonNode.Parse(json), Schema);

    // ── IsRequiredBound (regression for the PascalCase casing bug) ─────────────
    //
    // The generated R4 schema emits PascalCase "Required" for gender's binding; the pre-fix code
    // matched case-sensitive "required" and so the flag never fired in production. gender is a code
    // (string-valued), so it IS enumerated — the flag is what keeps free-text strategies off it.

    [Fact]
    public void GivenRequiredBoundCodeElement_WhenCheckingIsRequiredBound_ThenTrue()
    {
        var resource = ResourceJsonNode.Parse(
            """{ "resourceType": "Patient", "id": "rb", "gender": "male" }""");
        var gender = resource.ToElement(Schema).Children("gender").Single();

        gender.Type.ShouldBeAssignableTo<ITypeExtended>();
        ((ITypeExtended)gender.Type!).Binding!.Strength.ShouldBe("Required");

        ElementTreeEnumerator.IsRequiredBound(gender.Type).ShouldBeTrue();
    }

    [Fact]
    public void GivenUnboundStringElement_WhenCheckingIsRequiredBound_ThenFalse()
    {
        var resource = ResourceJsonNode.Parse(
            """{ "resourceType": "Patient", "id": "rb", "name": [{ "family": "Smith" }] }""");
        var family = resource.ToElement(Schema)
            .Children("name").Single()
            .Children("family").Single();

        ElementTreeEnumerator.IsRequiredBound(family.Type).ShouldBeFalse();
    }

    [Fact]
    public void GivenEnumeratedBoundCode_WhenInspectingTarget_ThenFlaggedRequiredBoundAndFreeTextStrategyRejectsIt()
    {
        var target = Enumerate(
            """{ "resourceType": "Patient", "id": "rb", "gender": "male" }""")
            .Single(t => t.Path == "Patient.gender");

        // The enumerator yields the bound code; the flag (not omission) is what protects it.
        target.InstanceType.ShouldBe("code");
        target.IsRequiredBound.ShouldBeTrue();
        new CjkUnicodeStrategy().CanApply(target).ShouldBeFalse();
    }

    // ── Shadowed primitive ─────────────────────────────────────────────────────

    [Fact]
    public void GivenShadowedPrimitive_WhenEnumerating_ThenTargetCarriesRealValueNotShadow()
    {
        const string json = """
            {
              "resourceType": "Patient",
              "id": "shadow",
              "name": [{ "family": "X", "_family": { "extension": [{ "url": "http://e", "valueString": "ext" }] } }]
            }
            """;

        var target = Enumerate(json).Single(t => t.Path == "Patient.name[0].family");

        target.Value.ShouldBe("X");
        target.InstanceType.ShouldBe("string");
    }

    [Fact]
    public void GivenShadowedPrimitive_WhenReplacing_ThenMutatesValueAndLeavesExtensionIntact()
    {
        const string json = """
            {
              "resourceType": "Patient",
              "id": "shadow",
              "name": [{ "family": "X", "_family": { "extension": [{ "url": "http://e", "valueString": "ext" }] } }]
            }
            """;
        var resource = ResourceJsonNode.Parse(json);
        var target = ElementTreeEnumerator.Enumerate(resource, Schema)
            .Single(t => t.Path == "Patient.name[0].family");

        target.Replace("NEW");

        var name0 = resource.MutableNode["name"]!.AsArray()[0]!.AsObject();
        name0["family"]!.GetValue<string>().ShouldBe("NEW");
        name0["_family"]!["extension"]!.AsArray()[0]!["valueString"]!.GetValue<string>().ShouldBe("ext");
    }

    // ── Choice element typing ──────────────────────────────────────────────────

    [Fact]
    public void GivenObservationValueString_WhenEnumerating_ThenInstanceTypeIsString()
    {
        const string json = """
            {
              "resourceType": "Observation",
              "id": "obs",
              "status": "final",
              "code": { "text": "note" },
              "valueString": "free text result"
            }
            """;

        var target = Enumerate(json).Single(t => t.Path == "Observation.valueString");

        target.InstanceType.ShouldBe("string");
        target.Value.ShouldBe("free text result");
    }

    [Fact]
    public void GivenObservationValueDateTime_WhenEnumerating_ThenInstanceTypeIsDateTime()
    {
        const string json = """
            {
              "resourceType": "Observation",
              "id": "obs",
              "status": "final",
              "code": { "text": "note" },
              "valueDateTime": "2021-06-15T10:00:00Z"
            }
            """;

        var target = Enumerate(json).Single(t => t.Path == "Observation.valueDateTime");

        target.InstanceType.ShouldBe("dateTime");
    }

    // ── Array leaves ───────────────────────────────────────────────────────────

    [Fact]
    public void GivenArrayLeaf_WhenEnumerating_ThenEachEntryIsYieldedWithIndexedPath()
    {
        const string json = """
            {
              "resourceType": "Patient",
              "id": "arr",
              "name": [{ "given": ["Jane", "Mary"] }]
            }
            """;

        var paths = Enumerate(json).Select(t => t.Path).ToList();

        paths.ShouldContain("Patient.name[0].given[0]");
        paths.ShouldContain("Patient.name[0].given[1]");
    }

    // ── Non-string leaves excluded ─────────────────────────────────────────────

    [Fact]
    public void GivenBooleanAndIntegerLeaves_WhenEnumerating_ThenNeitherIsYielded()
    {
        const string json = """
            {
              "resourceType": "Patient",
              "id": "nonstr",
              "active": true,
              "multipleBirthInteger": 2,
              "name": [{ "family": "Smith" }]
            }
            """;

        var paths = Enumerate(json).Select(t => t.Path).ToList();

        paths.ShouldNotContain("Patient.active");
        paths.ShouldNotContain(p => p.StartsWith("Patient.multipleBirth", StringComparison.Ordinal));
        paths.ShouldContain("Patient.name[0].family");
    }

    // ── Infra keys ─────────────────────────────────────────────────────────────
    //
    // The enumerator does NOT special-case infra keys; id / meta.versionId / text.status are
    // string-valued so they ARE yielded. What protects them is their non-free-text InstanceType
    // (id / instant / code / xhtml), so a free-text strategy never touches them.

    [Fact]
    public void GivenInfrastructureKeys_WhenEnumerating_ThenTheyAreYieldedButNotFreeTextEligible()
    {
        const string json = """
            {
              "resourceType": "Patient",
              "id": "infra-1",
              "meta": { "versionId": "1", "lastUpdated": "2021-01-01T00:00:00Z" },
              "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">hi</div>" },
              "name": [{ "family": "Smith" }]
            }
            """;
        var freeText = new CjkUnicodeStrategy();

        var targets = Enumerate(json);

        var infra = targets.Where(t =>
            t.Path is "Patient.id" or "Patient.meta.versionId" or "Patient.text.status" or "Patient.text.div").ToList();
        infra.ShouldNotBeEmpty();
        infra.ShouldAllBe(t => !freeText.CanApply(t));

        // family stays the lone free-text-eligible leaf here.
        targets.Where(t => freeText.CanApply(t)).Select(t => t.Path)
            .ShouldBe(["Patient.name[0].family"]);
    }

    // ── Direct array Replace (sibling isolation) ───────────────────────────────

    [Fact]
    public void GivenArrayLeafTarget_WhenReplacing_ThenOnlyThatEntryChangesAndSiblingIsUntouched()
    {
        const string json = """
            {
              "resourceType": "Patient",
              "id": "arr",
              "name": [{ "given": ["Jane", "Mary"] }]
            }
            """;
        var resource = ResourceJsonNode.Parse(json);
        var target = ElementTreeEnumerator.Enumerate(resource, Schema)
            .Single(t => t.Path == "Patient.name[0].given[0]");

        target.Replace("NEW");

        var given = resource.MutableNode["name"]!.AsArray()[0]!["given"]!.AsArray();
        given[0]!.GetValue<string>().ShouldBe("NEW");
        given[1]!.GetValue<string>().ShouldBe("Mary");
    }
}
