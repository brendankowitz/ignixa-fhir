// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

/// <summary>
/// Pins <see cref="FreeTextEdgeCaseStrategy"/>'s documented promise that a CJK/RTL/emoji value is
/// "never dropped into a bound code, system URL, reference, or id".
/// </summary>
/// <remarks>
/// The promise used to be kept by accident. <c>Reference.reference</c> reported instance type
/// <c>Reference</c> - not because the schema said so, but because of the name-equality recursion
/// heuristic issue #454 removed - and the gate excludes anything that is not <c>string</c> or
/// <c>markdown</c>. Correcting the element model makes these elements report their real declared type,
/// <c>string</c>, and they carry no terminology binding, so they became eligible for free-text
/// mutation with nothing to stop them: a strategy declaring <see cref="ValidityIntent.PreservesValidity"/>
/// would emit <c>"reference": "Patient/[emoji]"</c>.
/// <para>
/// The exclusion is now stated where it is enforced rather than inferred from a type name, so it holds
/// whatever the element model reports. These are the three elements whose type the #454 fix changed
/// from a datatype name to <c>string</c>.
/// </para>
/// </remarks>
public class FreeTextStructuralExclusionTests
{
    public static TheoryData<string, string, string> StructuralStringLeaves => new()
    {
        {
            "a relative reference",
            """{"resourceType":"Patient","id":"p1","managingOrganization":{"reference":"Organization/o1"}}""",
            "Patient.managingOrganization.reference"
        },
        {
            "an element id",
            """{"resourceType":"Patient","id":"p1","name":[{"id":"n1","family":"Smith"}]}""",
            "Patient.name[0].id"
        },
    };

    [Theory]
    [MemberData(nameof(StructuralStringLeaves))]
    public void GivenAStructuralStringLeaf_WhenAFreeTextStrategyIsOffered_ThenItDeclines(
        string because, string json, string path)
    {
        var target = EdgeCaseTargetFactory.AtPath(json, path);

        // The element model reports the schema's declared type, which is what makes the exclusion
        // necessary: gating on type alone would let these through.
        target.InstanceType.ShouldBe("string");
        target.IsRequiredBound.ShouldBeFalse();

        foreach (var strategy in new FreeTextEdgeCaseStrategy[]
        {
            new EmojiUnicodeStrategy(), new CjkUnicodeStrategy(), new RtlUnicodeStrategy(),
            new ZeroWidthUnicodeStrategy(), new CombiningUnicodeStrategy(), new MultiScriptLongUnicodeStrategy(),
        })
        {
            strategy.CanApply(target).ShouldBeFalse(
                $"{strategy.Category} must not mutate {path} ({because}); its Intent is {strategy.Intent}");
        }
    }

    /// <summary>
    /// The complement, so the exclusion cannot be satisfied by refusing everything: a genuine
    /// free-text string on the same resource is still eligible.
    /// </summary>
    [Fact]
    public void GivenAGenuineFreeTextLeaf_WhenAFreeTextStrategyIsOffered_ThenItApplies()
    {
        var target = EdgeCaseTargetFactory.AtPath(
            """{"resourceType":"Patient","id":"p1","name":[{"family":"Smith"}]}""",
            "Patient.name[0].family");

        new EmojiUnicodeStrategy().CanApply(target).ShouldBeTrue();
    }
}
