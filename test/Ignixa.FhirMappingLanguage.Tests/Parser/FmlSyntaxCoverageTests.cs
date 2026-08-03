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
    [InlineData("<<types>>", GroupTypeMode.Types)]
    [InlineData("<<type+>>", GroupTypeMode.TypeAndTypes)]
    public void GivenAGroupTypeModeAnnotation_WhenRoundTripping_ThenTheAnnotationSurvives(
        string annotation, GroupTypeMode expected)
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
        reparsed.Groups[0].TypeMode.ShouldBe(expected);
    }

    [Fact]
    public void GivenNoTypeModeAnnotation_WhenSerializing_ThenNoAnnotationIsEmitted()
    {
        // Arrange
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var serialized = new FmlSerializer().Serialize(new MappingParser().Parse(Fml));

        // Assert
        serialized.ShouldNotContain("<<");
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

    #region R6 metadata-declaration form (Task 5)

    [Fact]
    public void GivenMetadataDeclarationsInsteadOfAMapHeader_WhenParsing_ThenUrlAndNameAreDerivedFromMetadata()
    {
        // Arrange
        const string Fml = """
            /// url = 'http://hl7.org/fhir/uv/xver/StructureMap/Element4to6'
            /// name = 'Element4to6'
            /// title = 'Element Transforms: R4 to R6'

            group Element(source src, target tgt) {
              src.id as v -> tgt.id = v;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Url.ShouldBe("http://hl7.org/fhir/uv/xver/StructureMap/Element4to6");
        map.Identifier.ShouldBe("Element4to6");
        map.Metadata["title"].ShouldBe("Element Transforms: R4 to R6");
    }

    [Fact]
    public void GivenOnlyCommentsAndWhitespace_WhenParsing_ThenAParseExceptionIsThrown()
    {
        // Arrange
        const string Fml = """
            // nothing to see here
            """;

        // Act & Assert
        Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
    }

    [Fact]
    public void GivenBothExplicitHeaderAndMetadata_WhenParsing_ThenExplicitHeaderWinsForUrlAndName()
    {
        // Arrange — explicit 'map' header overrides any metadata declarations with the same keys
        const string Fml = """
            /// url = 'http://metadata.example.org/ShouldNotWin'
            /// name = 'MetadataName'
            /// title = 'A Title From Metadata'
            map 'http://header.example.org/ShouldWin' = 'HeaderName'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert — explicit header values win
        map.Url.ShouldBe("http://header.example.org/ShouldWin");
        map.Identifier.ShouldBe("HeaderName");
        // Metadata dict still populated (title is metadata-only)
        map.Metadata["title"].ShouldBe("A Title From Metadata");
    }

    [Fact]
    public void GivenDuplicateNonSpecialMetadataKey_WhenParsing_ThenLastValueWins()
    {
        // Arrange — two /// title declarations; last-wins must apply and no exception may be thrown
        const string Fml = """
            /// title = 'First title'
            /// title = 'Second title'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Metadata["title"].ShouldBe("Second title");
    }

    [Fact]
    public void GivenDuplicateUrlMetadataKey_WhenParsing_ThenLastValueWinsAndUrlIsPopulated()
    {
        // Arrange — two /// url declarations; the later one must win for MapExpression.Url
        const string Fml = """
            /// url = 'http://example.org/First'
            /// url = 'http://example.org/Second'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Url.ShouldBe("http://example.org/Second");
    }

    [Fact]
    public void GivenMetadataWithNoGroups_WhenParsing_ThenExceptionMessageDescribesActualCondition()
    {
        // Arrange — title metadata is present but there are no groups; groups are mandatory per spec
        // Classification (a): old guard checked url+identifier conjuncts, which was non-conformant;
        // groupDeclaration+ in the official grammar means groups are the only required element.
        const string Fml = "/// title = 'Only a title'";

        // Act & Assert
        var ex = Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
        ex.Message.ShouldBe("The input has no group declarations; at least one group is required.");
    }

    // ── FIX 1: malformed metadata declarations ──────────────────────────────────

    [Fact]
    public void GivenMetadataLineWithMissingEquals_WhenParsing_ThenThrowsWithLineInfo()
    {
        // Arrange — '/// url value' has no '='; must be rejected with position, not silently dropped
        const string Fml = """
            /// url 'http://example.org/ShouldNotBeSilentlyDropped'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act & Assert
        var ex = Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
        ex.Message.ShouldContain("Malformed metadata declaration");
        ex.Message.ShouldContain("at line 1");
    }

    [Fact]
    public void GivenMetadataKeyStartingWithDigit_WhenParsing_ThenThrowsWithLineInfo()
    {
        // Arrange — '/// 1title = x' has a key starting with a digit, violating the key grammar
        const string Fml = """
            /// 1title = 'x'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act & Assert
        var ex = Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
        ex.Message.ShouldContain("Malformed metadata declaration");
        ex.Message.ShouldContain("at line 1");
    }

    [Fact]
    public void GivenFourSlashPrefix_WhenParsing_ThenThrowsWithLineInfo()
    {
        // Arrange — '////' is not a valid metadata prefix (no whitespace after ///)
        const string Fml = """
            //// url = 'http://example.org'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act & Assert
        var ex = Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
        ex.Message.ShouldContain("Malformed metadata declaration");
        ex.Message.ShouldContain("at line 1");
    }

    [Fact]
    public void GivenNoSpaceAfterTripleSlash_WhenParsing_ThenThrowsWithLineInfo()
    {
        // Arrange — '///url = x' omits required whitespace between /// and key
        const string Fml = """
            ///url = 'http://example.org'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act & Assert
        var ex = Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
        ex.Message.ShouldContain("Malformed metadata declaration");
        ex.Message.ShouldContain("at line 1");
    }

    [Fact]
    public void GivenMetadataWithEmptyValue_WhenParsing_ThenValueIsEmptyString()
    {
        // Arrange — the grammar makes the value optional; '/// title =' is valid with value = ""
        const string Fml = """
            /// title =

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Metadata["title"].ShouldBe("");
    }

    // ── FIX 3: UnescapeString path pinning ──────────────────────────────────────

    [Fact]
    public void GivenUnquotedMetadataValue_WhenParsing_ThenValueIsReturnedLiterally()
    {
        // Arrange — bare unquoted value is returned as-is by UnescapeString
        const string Fml = """
            /// status = draft

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Metadata["status"].ShouldBe("draft");
    }

    [Fact]
    public void GivenDoubleQuotedMetadataValue_WhenParsing_ThenQuotesAreStripped()
    {
        // Arrange — double-quoted value has its surrounding quotes removed by UnescapeString
        const string Fml = """
            /// title = "Hello World"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Metadata["title"].ShouldBe("Hello World");
    }

    // ── FIX 4: Ordinal case-sensitivity pin ─────────────────────────────────────

    [Fact]
    public void GivenUpperCaseMetadataUrlKey_WhenParsing_ThenKeyPreservedAndMapUrlRemainsEmpty()
    {
        // Arrange — 'URL' (uppercase) is a different key from 'url' (lowercase) under Ordinal compare;
        // GetValueOrDefault("url") must NOT find it, so MapExpression.Url stays empty.
        const string Fml = """
            /// URL = 'http://example.org/UpperCase'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        map.Metadata["URL"].ShouldBe("http://example.org/UpperCase");
        map.Url.ShouldBe("");
    }

    #endregion

    [Fact]
    public void GivenSimpleParenthesizedTransform_WhenParsing_ThenPathExpressionIsExtracted()
    {
        // Arrange — corpus line: tgt.gender = (item.answer.valueString)
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              src.item as item -> tgt.gender = (item.answer.valueString);
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        var target = map.Groups[0].Rules[0].Targets[0];
        var expr = target.Transform.ShouldBeOfType<FhirPathExpression>();
        expr.PathExpression.ShouldBe("item.answer.valueString");
    }

    [Fact]
    public void GivenParenthesizedTransformWithOperator_WhenParsing_ThenFullExpressionTextIsExtracted()
    {
        // Arrange — corpus line: ext.system = ('urn:uuid:' + r.lower()) "rootuuid"
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              src -> tgt.identifer as ext, ext.system = ('urn:uuid:' + r.lower()) "rootuuid";
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        var rule = map.Groups[0].Rules[0];
        var target = rule.Targets[1];
        var expr = target.Transform.ShouldBeOfType<FhirPathExpression>();
        expr.PathExpression.ShouldBe("'urn:uuid:' + r.lower()");
        rule.Name.ShouldBe("rootuuid");
    }

    [Fact]
    public void GivenParenthesizedTransformWithExternalConstantAndQuantity_WhenParsing_ThenFullExpressionTextIsExtracted()
    {
        // Arrange — corpus line: tgt.birthDate = (%value + 5 days) "plus"
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              ext.value as value -> tgt.birthDate = (%value + 5 days) "plus";
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert
        var rule = map.Groups[0].Rules[0];
        var target = rule.Targets[0];
        var expr = target.Transform.ShouldBeOfType<FhirPathExpression>();
        expr.PathExpression.ShouldBe("%value + 5 days");
        rule.Name.ShouldBe("plus");
    }

    [Fact]
    public void GivenNonParenthesizedTargetWithAsAndRuleName_WhenParsing_ThenVariableAndNameAreNotSwallowedByFhirPath()
    {
        // Arrange — regression guard: verifies ParenthesizedFhirPathExpression was used, not the
        // greedy FhirPathExpression, which would consume 'as v "name"' as part of the expression.
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              src.a as a -> tgt.x = foo as v "name";
            }
            """;

        // Act
        var map = new MappingParser().Parse(Fml);

        // Assert — the greedy FhirPathExpression would swallow 'as v "name"' into the expression;
        // ParenthesizedFhirPathExpression cannot start with an Identifier so it falls through,
        // leaving QualifiedIdentifier to bind 'foo' and the rule machinery to bind 'v' and 'name'.
        var rule = map.Groups[0].Rules[0];
        var target = rule.Targets[0];
        target.Variable.ShouldBe("v");
        rule.Name.ShouldBe("name");
        target.Transform.ShouldBeOfType<IdentifierExpression>()
            .Name.ShouldBe("foo");
    }
}
