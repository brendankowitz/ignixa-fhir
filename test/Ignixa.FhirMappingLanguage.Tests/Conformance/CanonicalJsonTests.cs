/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for CanonicalJson structural JSON canonicalization.
 */

using System;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

public class CanonicalJsonTests
{
    [Fact]
    public void GivenTwoObjectsDifferingOnlyByWhitespace_WhenCanonicalizing_ThenTheResultsMatch()
    {
        // Arrange
        var withSpaces = """{"resourceType" : "Patient", "gender" : "female"}""";
        var withoutSpaces = """{"resourceType":"Patient","gender":"female"}""";

        // Act / Assert
        CanonicalJson.Canonicalize(withSpaces).ShouldBe(CanonicalJson.Canonicalize(withoutSpaces));
    }

    [Fact]
    public void GivenTwoObjectsDifferingOnlyByPropertyOrder_WhenCanonicalizing_ThenTheResultsMatch()
    {
        // Arrange
        var genderFirst = """{"gender":"female","resourceType":"Patient"}""";
        var resourceTypeFirst = """{"resourceType":"Patient","gender":"female"}""";

        // Act / Assert
        CanonicalJson.Canonicalize(genderFirst).ShouldBe(CanonicalJson.Canonicalize(resourceTypeFirst));
    }

    [Fact]
    public void GivenArraysInDifferentOrder_WhenCanonicalizing_ThenTheResultsDiffer()
    {
        // Arrange
        var abOrder = """{"given":["a","b"]}""";
        var baOrder = """{"given":["b","a"]}""";

        // Act / Assert
        CanonicalJson.Canonicalize(abOrder).ShouldNotBe(CanonicalJson.Canonicalize(baOrder));
    }

    [Fact]
    public void GivenNonAsciiText_WhenCanonicalizing_ThenTheCharactersAreNotEscaped()
    {
        // Arrange
        var json = """{"family":"Brönnimann-Bertholet"}""";

        // Act
        var result = CanonicalJson.Canonicalize(json);

        // Assert
        result.ShouldContain("Brönnimann-Bertholet");
    }

    // Proves Sort() recurses into nested object values, not just top-level properties.
    [Fact]
    public void GivenNestedObjectWithDifferentPropertyOrder_WhenCanonicalizing_ThenResultsMatch()
    {
        // Arrange
        var familyFirst = """{"name":{"family":"X","given":"Y"}}""";
        var givenFirst = """{"name":{"given":"Y","family":"X"}}""";

        // Act / Assert
        CanonicalJson.Canonicalize(familyFirst).ShouldBe(CanonicalJson.Canonicalize(givenFirst));
    }

    // Proves Sort() recurses through array elements into the objects they contain.
    [Fact]
    public void GivenObjectInsideArrayWithDifferentPropertyOrder_WhenCanonicalizing_ThenResultsMatch()
    {
        // Arrange
        var familyFirst = """{"name":[{"family":"X","given":"Y"}]}""";
        var givenFirst = """{"name":[{"given":"Y","family":"X"}]}""";

        // Act / Assert
        CanonicalJson.Canonicalize(familyFirst).ShouldBe(CanonicalJson.Canonicalize(givenFirst));
    }

    // Proves an implementation that discards values cannot pass by returning a constant.
    [Fact]
    public void GivenObjectsWithDifferentValues_WhenCanonicalizing_ThenResultsDiffer()
    {
        // Arrange
        var female = """{"gender":"female"}""";
        var male = """{"gender":"male"}""";

        // Act / Assert
        CanonicalJson.Canonicalize(female).ShouldNotBe(CanonicalJson.Canonicalize(male));
    }

    // Pins the exact canonical form: keys sorted by ordinal, WriteIndented=true (2-space indent).
    // Environment.NewLine is correct because System.Text.Json uses it for indented output.
    [Fact]
    public void GivenTwoPropertyObject_WhenCanonicalizing_ThenOutputMatchesExpectedForm()
    {
        // Arrange
        var json = """{"gender":"female","resourceType":"Patient"}""";
        // "gender" < "resourceType" under StringComparer.Ordinal ('g' < 'r')
        var nl = Environment.NewLine;
        var expected = $"{{{nl}  \"gender\": \"female\",{nl}  \"resourceType\": \"Patient\"{nl}}}";

        // Act
        var result = CanonicalJson.Canonicalize(json);

        // Assert
        result.ShouldBe(expected);
    }

    // ParamName is asserted so these fail if some unrelated call throws ArgumentException instead.
    // Null is deliberately not covered: JsonNode.Parse throws ArgumentNullException with the same
    // ParamName, so such a test would pass whether or not the guard exists.
    [Fact]
    public void GivenEmptyInput_WhenCanonicalizing_ThenThrowsArgumentExceptionWithCorrectParamName()
    {
        var exception = Should.Throw<ArgumentException>(() => CanonicalJson.Canonicalize(""));
        exception.ParamName.ShouldBe("json");
    }

    [Fact]
    public void GivenWhitespaceOnlyInput_WhenCanonicalizing_ThenThrowsArgumentExceptionWithCorrectParamName()
    {
        var exception = Should.Throw<ArgumentException>(() => CanonicalJson.Canonicalize("   "));
        exception.ParamName.ShouldBe("json");
    }

    // Canonicalized output is itself valid input: a second pass produces an identical string.
    [Fact]
    public void GivenCanonicalized_WhenCanonicalizingAgain_ThenResultIsUnchanged()
    {
        // Arrange
        var json = """{"resourceType":"Patient","name":[{"given":["John"],"family":"Doe"}],"gender":"male"}""";

        // Act
        var once = CanonicalJson.Canonicalize(json);
        var twice = CanonicalJson.Canonicalize(once);

        // Assert
        twice.ShouldBe(once);
    }
}
