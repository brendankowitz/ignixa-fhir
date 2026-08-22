/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for the one operand shape that reaches the interval-bound helpers
 * FhirPathEvaluator delegates to FhirTemporal.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the delegation of <c>FhirPathEvaluator</c>'s interval-bound arithmetic to
/// <see cref="FhirTemporal"/>'s, at both call sites: ordering comparison and the anchor that
/// partial-precision arithmetic resolves its operand through.
/// </summary>
/// <remarks>
/// <para>
/// Reaching those bounds at all is the hard part, and it is why this file is short.
/// <c>CompareDateTimesWithPrecision</c> prefers a typed comparison whenever both operands resolve to
/// a <see cref="FhirTemporal"/>, and they nearly always do - literals parse through
/// <see cref="FhirTemporal.TryParse"/>, and a schema-typed <c>date</c> read out of a resource is
/// materialised as a <see cref="FhirTemporal"/> rather than as its wire string. An operand only
/// falls through to the bounds when it <i>fails</i> <see cref="FhirTemporal.TryParse"/> while still
/// carrying a recognisable precision by shape.
/// </para>
/// <para>
/// A year with a leading sign is exactly that shape, which makes it both the reachability proof and
/// the pin for the single deliberate behavioural narrowing the delegation carries. The deleted
/// implementation parsed the year with <c>NumberStyles.Integer</c> and resolved <c>"+2020"</c> to
/// the year 2020; <see cref="FhirTemporal"/> uses <c>NumberStyles.None</c> and rejects it. It is
/// not a valid FHIR <c>date</c> - the specification's regex permits no sign - and FHIRPath
/// prescribes an empty result for an invalid operand, so the narrowing is the fail-safe direction
/// and is kept deliberately.
/// </para>
/// <para>
/// The same narrowing also rejects surrounding whitespace, which is <i>not</i> observable here: the
/// JSON source node trims primitive values before the schema sees them, so <c>" 2020"</c> arrives
/// as <c>"2020"</c> and never reaches the bounds. That half is pinned directly on the helpers
/// instead.
/// </para>
/// <para>
/// The saturation the delegation also buys at the top of <see cref="DateTime"/>'s range is
/// unreachable from here for the same structural reason: every <c>9999</c> shape that overflowed
/// the deleted arithmetic parses cleanly into a <see cref="FhirTemporal"/> and is therefore
/// answered by the typed comparison on both revisions. It is pinned directly too, in
/// <c>Ignixa.Abstractions.Tests.FhirTemporalBoundTests</c>.
/// </para>
/// </remarks>
public class FhirPathDateTimeBoundDelegationTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    [Fact]
    public void GivenABirthDateYearCarryingASign_WhenOrderedAgainstALiteral_ThenReturnsEmpty()
    {
        // Arrange
        var patient = Patient("+2020");

        // Act
        var result = patient.Select("Patient.birthDate < @2021").ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenABirthDateYearCarryingASign_WhenAddingACalendarYear_ThenReturnsEmpty()
    {
        // Arrange
        var patient = Patient("+2020");

        // Act
        var result = patient.Select("Patient.birthDate + 1 year").ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Control for both cases above. It fixes the empty result on the literal's shape rather than on
    /// the resource, the path, or year-precision handling in general, and it is a guard: a
    /// well-formed year behaves identically on either side of the delegation.
    /// </summary>
    [Fact]
    public void GivenAWellFormedBirthDateYear_WhenOrderedAndUsedInArithmetic_ThenBothStillResolve()
    {
        // Arrange
        var patient = Patient("2020");

        // Act
        var ordering = patient.Select("Patient.birthDate < @2021").Single();
        var arithmetic = patient.Select("Patient.birthDate + 1 year").Single();

        // Assert
        ordering.Value.ShouldBe(true);
        arithmetic.Value.ShouldBe("2021");
    }

    private static IElement Patient(string birthDate) => Parse($$"""
        {
          "resourceType": "Patient",
          "id": "p1",
          "birthDate": "{{birthDate}}"
        }
        """);

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
