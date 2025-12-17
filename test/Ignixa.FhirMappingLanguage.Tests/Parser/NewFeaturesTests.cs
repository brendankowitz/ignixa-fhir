/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for new FML features: ConceptMap declarations, Constants, and Rule names.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

public class NewFeaturesTests
{
    private readonly MappingParser _parser = new();

    #region Rule Names (Trailing Strings)

    [Fact]
    public void GivenRuleWithTrailingString_WhenParsing_ThenRuleNameIsExtracted()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            group Main(source src, target tgt) {
              src.name -> tgt.name 'copy name';
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.Groups.Count.ShouldBe(1);
        result.Groups[0].Rules.Count.ShouldBe(1);
        result.Groups[0].Rules[0].Name.ShouldBe("copy name");
    }

    [Fact]
    public void GivenRuleWithoutTrailingString_WhenParsing_ThenRuleNameIsNull()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            group Main(source src, target tgt) {
              src.name -> tgt.name;
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.Groups[0].Rules[0].Name.ShouldBeNull();
    }

    [Fact]
    public void GivenRuleWithTransformAndTrailingString_WhenParsing_ThenBothAreParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            group Main(source src, target tgt) {
              src.gender -> tgt.gender = create('code') 'create gender code';
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        var rule = result.Groups[0].Rules[0];
        rule.Name.ShouldBe("create gender code");
        rule.Targets[0].Transform.ShouldBeOfType<TransformExpression>();
        (rule.Targets[0].Transform as TransformExpression)!.FunctionName.ShouldBe("create");
    }

    #endregion

    #region Constant Declarations

    [Fact]
    public void GivenConstantWithStringValue_WhenParsing_ThenConstantIsParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            constant SYSTEM_URL = 'http://terminology.hl7.org/CodeSystem/v3-ActCode'
            group Main(source src, target tgt) {
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.Constants.Count.ShouldBe(1);
        result.Constants[0].Name.ShouldBe("SYSTEM_URL");
        result.Constants[0].Value.ShouldBeOfType<LiteralExpression>();
        (result.Constants[0].Value as LiteralExpression)!.Value.ShouldBe("http://terminology.hl7.org/CodeSystem/v3-ActCode");
    }

    [Fact]
    public void GivenConstantWithNumericValue_WhenParsing_ThenConstantIsParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            constant MAX_LENGTH = 100
            group Main(source src, target tgt) {
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.Constants.Count.ShouldBe(1);
        result.Constants[0].Name.ShouldBe("MAX_LENGTH");
        result.Constants[0].Value.ShouldBeOfType<LiteralExpression>();
        (result.Constants[0].Value as LiteralExpression)!.Value.ShouldBe(100);
    }

    [Fact]
    public void GivenMultipleConstants_WhenParsing_ThenAllConstantsAreParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            constant SYSTEM_URL = 'http://example.org'
            constant MAX_LENGTH = 100
            constant IS_ACTIVE = true
            group Main(source src, target tgt) {
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.Constants.Count.ShouldBe(3);
        result.Constants[0].Name.ShouldBe("SYSTEM_URL");
        result.Constants[1].Name.ShouldBe("MAX_LENGTH");
        result.Constants[2].Name.ShouldBe("IS_ACTIVE");
    }

    #endregion

    #region ConceptMap Declarations

    [Fact]
    public void GivenConceptMapWithPrefixes_WhenParsing_ThenPrefixesAreParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            conceptmap '#genderMap' {
              prefix fhir = 'http://hl7.org/fhir/administrative-gender'
              prefix v2 = 'http://terminology.hl7.org/CodeSystem/v2-0001'
            }
            group Main(source src, target tgt) {
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.ConceptMaps.Count.ShouldBe(1);
        result.ConceptMaps[0].Identifier.ShouldBe("#genderMap");
        result.ConceptMaps[0].Prefixes.Count.ShouldBe(2);
        result.ConceptMaps[0].Prefixes[0].PrefixName.ShouldBe("fhir");
        result.ConceptMaps[0].Prefixes[0].Url.ShouldBe("http://hl7.org/fhir/administrative-gender");
        result.ConceptMaps[0].Prefixes[1].PrefixName.ShouldBe("v2");
    }

    [Fact]
    public void GivenConceptMapWithCodeMappings_WhenParsing_ThenMappingsAreParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            conceptmap '#genderMap' {
              prefix s = 'http://source'
              prefix t = 'http://target'
              s:male == t:M
              s:female == t:F
              s:other ~= t:O
            }
            group Main(source src, target tgt) {
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        result.ConceptMaps.Count.ShouldBe(1);
        var conceptMap = result.ConceptMaps[0];
        conceptMap.Groups.Count.ShouldBe(1);
        conceptMap.Groups[0].CodeMaps.Count.ShouldBe(3);

        var map1 = conceptMap.Groups[0].CodeMaps[0];
        map1.SourcePrefix.ShouldBe("s");
        map1.SourceCode.ShouldBe("male");
        map1.Equivalence.ShouldBe(ConceptMapEquivalence.Equivalent);
        map1.TargetPrefix.ShouldBe("t");
        map1.TargetCode.ShouldBe("M");

        var map3 = conceptMap.Groups[0].CodeMaps[2];
        map3.Equivalence.ShouldBe(ConceptMapEquivalence.RelatedTo);
    }

    [Fact]
    public void GivenConceptMapWithNumericCodes_WhenParsing_ThenCodesAreParsed()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'Test'
            conceptmap '#codeMap' {
              prefix s = 'http://source'
              prefix t = 'http://target'
              s:12345 == t:67890
            }
            group Main(source src, target tgt) {
            }
            """;

        // Act
        var result = _parser.Parse(fml);

        // Assert
        var map = result.ConceptMaps[0].Groups[0].CodeMaps[0];
        map.SourceCode.ShouldBe("12345");
        map.TargetCode.ShouldBe("67890");
    }

    #endregion
}
