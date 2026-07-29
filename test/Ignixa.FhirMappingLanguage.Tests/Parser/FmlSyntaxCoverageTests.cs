/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Serialization;
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

    [Fact]
    public void GivenADoubleQuotedMapHeader_WhenParsing_ThenTheUrlAndNameAreRead()
    {
        // Arrange
        const string Fml = """
            map "http://hl7.org/fhir/StructureMap/tutorial" = "tutorial"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Url.ShouldBe("http://hl7.org/fhir/StructureMap/tutorial");
        map.Identifier.ShouldBe("tutorial");
    }

    [Fact]
    public void GivenADoubleQuotedStringWithAnEscapedQuote_WhenParsing_ThenTheEscapeIsResolved()
    {
        // Arrange
        const string Fml = """
            map "http://example.org/T" = "a \" b"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Identifier.ShouldBe("a \" b");
    }

    [Fact]
    public void GivenADoubleQuotedStringWithAnEscapedBackslash_WhenParsing_ThenTheEscapeIsResolved()
    {
        // Arrange
        const string Fml = """
            map "http://example.org/T" = "a \\ b"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Identifier.ShouldBe(@"a \ b");
    }

    [Fact]
    public void GivenADoubleQuotedStringWithBackslashThenEscapedQuote_WhenParsing_ThenBothEscapesAreResolved()
    {
        // Arrange
        const string Fml = """
            map "http://example.org/T" = "a \\\" b"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Identifier.ShouldBe("a \\\" b");
    }

    [Fact]
    public void GivenAConceptMapWithADoubleQuotedId_WhenParsing_ThenTheIdIsUnquoted()
    {
        // Arrange
        const string Fml = """
            map 'http://example.org/T' = 'T'

            conceptmap "#cm" {
              prefix s = 'http://a'
              prefix t = 'http://b'

              s:x == t:y
            }

            group Main(source src, target tgt) { src.a as a -> tgt.a = a; }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.ConceptMaps.Count.ShouldBe(1);
        map.ConceptMaps[0].Identifier.ShouldBe("#cm");
    }

    [Theory]
    [InlineData("<<types>>", GroupTypeMode.Types)]
    [InlineData("<<type+>>", GroupTypeMode.TypeAndTypes)]
    public void GivenAGroupTypeModeAnnotation_WhenParsing_ThenTheModeIsCaptured(string annotation, GroupTypeMode expected)
    {
        // Arrange
        var fml = $$"""
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {{annotation}} {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(fml);

        // Assert
        map.Groups[0].TypeMode.ShouldBe(expected);
    }

    [Fact]
    public void GivenNoTypeModeAnnotation_WhenParsing_ThenTheModeIsNone()
    {
        // Arrange
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Groups[0].TypeMode.ShouldBe(GroupTypeMode.None);
    }

    [Theory]
    [InlineData("<<types>>")]
    [InlineData("<<type+>>")]
    public void GivenAGroupTypeModeAnnotation_WhenRoundTripping_ThenTheAnnotationSurvives(string annotation)
    {
        // Arrange
        var fml = $$"""
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {{annotation}} {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var reparsed = new MappingParser().Parse(new FmlSerializer().Serialize(new MappingParser().Parse(fml)));

        // Assert
        reparsed.Groups[0].TypeMode.ShouldBe(new MappingParser().Parse(fml).Groups[0].TypeMode);
    }

    [Fact]
    public void GivenABareTypeAnnotation_WhenParsing_ThenItIsRejected()
    {
        // Arrange
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) <<type>> {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act & Assert
        var ex = Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
        ex.Message.ShouldContain("expected plus");
    }

    [Theory]
    [InlineData("<<types>>", GroupTypeMode.Types)]
    [InlineData("<<type+>>", GroupTypeMode.TypeAndTypes)]
    public void GivenAGroupWithExtendsAndTypeModeAnnotation_WhenRoundTripping_ThenBothSurvive(
        string annotation, GroupTypeMode expectedMode)
    {
        // Arrange
        var fml = $$"""
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) extends Base {{annotation}} {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var reparsed = new MappingParser().Parse(new FmlSerializer().Serialize(new MappingParser().Parse(fml)));

        // Assert
        reparsed.Groups[0].Extends.ShouldBe("Base");
        reparsed.Groups[0].TypeMode.ShouldBe(expectedMode);
    }
}
