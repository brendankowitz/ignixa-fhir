/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Liveness guard for the shared differential subject.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Proves the differential subject resolves and types the elements the corpus navigates.
/// </summary>
/// <remarks>
/// <para>
/// A differential harness reports disagreement between two evaluation paths. Two paths that both
/// return empty agree, so a corpus whose element paths do not resolve is green and worthless. These
/// tests are the anti-vacuity guard: if a corpus path stops resolving - a renamed element, a schema
/// regression, a JSON typo in the subject - the differential theories stay green and this class goes
/// red instead.
/// </para>
/// <para>
/// They also pin the property that motivated moving the subject off hand-built elements: temporal
/// elements arrive as <see cref="FhirTemporal"/> produced by the schema-aware parser, not as strings
/// a test author chose to inject.
/// </para>
/// </remarks>
public class DifferentialSubjectTypingTests
{
    [Theory]
    [InlineData("birthDate", "date", "1974-12-25")]
    [InlineData("meta.lastUpdated", "instant", "2024-06-15T08:00:00Z")]
    [InlineData("extension.value", "time", "10:30:00")]
    [InlineData("contact.period.start", "dateTime", "2020-01-01")]
    [InlineData("contact.period.end", "dateTime", "2021-06-15")]
    public void GivenACorpusTemporalPath_WhenNavigated_ThenYieldsATypedFhirTemporal(
        string path,
        string expectedInstanceType,
        string expectedLiteral)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select(path).ToList();

        // Assert
        result.Count.ShouldBe(1, $"'{path}' must resolve, or every differential over it is vacuous.");
        result[0].InstanceType.ShouldBe(expectedInstanceType);
        result[0].Value.ShouldBeOfType<FhirTemporal>().Literal.ShouldBe(expectedLiteral);
    }

    [Theory]
    [InlineData("gender", "code", "male")]
    [InlineData("identifier.value", "string", "abc")]
    [InlineData("name.first().family", "string", "Smith")]
    [InlineData("telecom.where(system = 'phone').value", "string", "555-1234")]
    public void GivenACorpusStringPath_WhenNavigated_ThenYieldsTheWireValue(
        string path,
        string expectedInstanceType,
        string expectedValue)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select(path).ToList();

        // Assert
        result.Count.ShouldBe(1, $"'{path}' must resolve, or every differential over it is vacuous.");
        result[0].InstanceType.ShouldBe(expectedInstanceType);
        result[0].Value.ShouldBe(expectedValue);
    }

    [Fact]
    public void GivenTheBooleanPath_WhenNavigated_ThenYieldsATypedBoolean()
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("active").ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("boolean");
        result[0].Value.ShouldBe(true);
    }

    [Fact]
    public void GivenTheIntegerChoicePath_WhenNavigated_ThenYieldsATypedInteger()
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("multipleBirthInteger").ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("integer");
        result[0].Value.ShouldBe(2);
    }

    [Fact]
    public void GivenTheDecimalExtensionPath_WhenNavigated_ThenYieldsATypedDecimal()
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("contact.extension.value").ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("decimal");
        result[0].Value.ShouldBe(1.5m);
    }

    [Fact]
    public void GivenARepeatingPath_WhenNavigated_ThenYieldsEveryOccurrence()
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var names = subject.Select("name").ToList();
        var given = subject.Select("name.given").ToList();
        var telecom = subject.Select("telecom").ToList();

        // Assert
        names.Count.ShouldBe(2);
        given.Count.ShouldBe(3);
        telecom.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("photo")]
    [InlineData("missingElement")]
    public void GivenAnAbsentPath_WhenNavigated_ThenYieldsEmpty(string path)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select(path).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheSubject_WhenInspected_ThenIsTheProductionElementCarryingASchemaTypeDefinition()
    {
        // The hand-built element this replaced returned null from Type, so nothing downstream that
        // consults the schema was ever exercised by the differential harnesses. Asserting the
        // concrete type is the tripwire: swapping the fixture back to a convenient hand-built stub
        // silently reintroduces the blind spot, and every other test here would stay green.

        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var type = subject.Type;

        // Assert
        subject.GetType().Name.ShouldBe("SchemaAwareElement");
        subject.InstanceType.ShouldBe("Patient");
        type.ShouldNotBeNull();
    }
}
