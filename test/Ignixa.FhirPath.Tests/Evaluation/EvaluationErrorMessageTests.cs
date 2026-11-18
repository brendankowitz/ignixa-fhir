// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Evaluation errors are logged with their exception and returned to callers, so a message must name
/// types and functions and never echo the resource values that triggered it.
/// </summary>
public class EvaluationErrorMessageTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private const string Sensitive = "Zebra-Sensitive-Value";

    [Fact]
    public void GivenNonBooleanDataValue_WhenAllTrue_ThenMessageNamesTheTypeNotTheValue()
    {
        // Arrange
        var patient = Element($$$"""{"resourceType":"Patient","name":[{"family":"{{{Sensitive}}}"}]}""");

        // Act
        var error = Should.Throw<FhirPathEvaluationException>(() => patient.Select("Patient.name.family.allTrue()").ToList());

        // Assert
        error.Message.ShouldNotContain(Sensitive);
        error.Message.ShouldContain("allTrue");
        error.Message.ShouldContain("string");
    }

    [Theory]
    [InlineData("@1974-12-25 - 4242 'zzunit'", "4242")]
    [InlineData("@T10:00 + 4242 'zzunit'", "zzunit")]
    [InlineData("@T10:00 + 4242 day", "4242")]
    [InlineData("@1974-12-25 + 4242 'a'", "4242")]
    public void GivenQuantityThatTheTemporalCannotTake_WhenAdded_ThenMessageDoesNotEchoTheQuantity(string expression, string echoed)
    {
        // Arrange
        var patient = Element("""{"resourceType":"Patient","id":"p1"}""");

        // Act
        var error = Should.Throw<FhirPathEvaluationException>(() => patient.Select(expression).ToList());

        // Assert
        error.Message.ShouldNotContain(echoed);
    }

    [Fact]
    public void GivenUcumDefiniteDurationUnitOnDate_WhenAdded_ThenMessageSuggestsTheCalendarKeyword()
    {
        // Arrange
        var patient = Element("""{"resourceType":"Patient","id":"p1"}""");

        // Act
        var error = Should.Throw<FhirPathEvaluationException>(() => patient.Select("@1974-12-25 + 4242 'a'").ToList());

        // Assert
        error.Message.ShouldContain("'year'");
    }

    private static IElement Element(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
