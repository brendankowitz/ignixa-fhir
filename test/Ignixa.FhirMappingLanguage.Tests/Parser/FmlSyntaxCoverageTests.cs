/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

/// <summary>
/// Regression coverage for FML syntax found in the official HL7 structure-mapping corpus.
/// Each test targets one previously unsupported construct.
/// </summary>
public class FmlSyntaxCoverageTests
{
    [Theory]
    [InlineData("where linkId.value in ('patient.sex') -> tgt.gender = 'x'", false, "linkId.value in ('patient.sex')")]
    [InlineData("where (linkId.value in ('patient.sex')) -> tgt.gender = 'x'", false, "linkId.value in ('patient.sex')")]
    [InlineData("where %value + 5 days -> tgt.gender = 'x'", false, "%value + 5 days")]
    [InlineData("check (%value + 5 days > 3) -> tgt.gender = 'x'", true, "%value + 5 days > 3")]
    public void GivenEmbeddedFhirPathWithSignificantWhitespace_WhenParsing_ThenTheOriginalSpacingIsPreserved(
        string clause, bool isCheck, string expected)
    {
        // Arrange
        var fml = $$"""
            map 'http://example.org/Test' = 'Test'

            group Main(source src, target tgt) {
              src.item as item {{clause}};
            }
            """;

        var parser = new MappingParser();

        // Act
        var map = parser.Parse(fml);

        // Assert
        var source = map.Groups[0].Rules[0].Sources[0];
        var expr = isCheck ? source.Check : source.Condition;
        expr.ShouldNotBeNull();
        expr.ShouldBeOfType<FhirPathExpression>();
        ((FhirPathExpression)expr!).PathExpression.ShouldBe(expected);
    }
}
