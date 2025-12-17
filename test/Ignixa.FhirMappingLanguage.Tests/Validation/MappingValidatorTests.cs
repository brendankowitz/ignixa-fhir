/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for mapping validation functionality.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Validation;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Validation;

public class MappingValidatorTests
{
    #region Basic Validation Tests

    [Fact]
    public void GivenValidMapping_WhenValidating_ThenPassesValidation()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMappingWithoutUrl_WhenValidating_ThenReportsError()
    {
        // Arrange - Create invalid map manually
        var map = new Expressions.MapExpression(
            url: "",  // Invalid: empty URL
            identifier: "Test",
            new List<Expressions.UsesExpression>(),
            new List<Expressions.ImportsExpression>(),
            new List<Expressions.GroupExpression>
            {
                new Expressions.GroupExpression(
                    "Group1",
                    new List<Expressions.ParameterExpression>
                    {
                        new Expressions.ParameterExpression(Expressions.ParameterMode.Source, "src", "Patient")
                    },
                    null,
                    new List<Expressions.RuleExpression>())
            });

        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "MISSING_URL");
    }

    [Fact]
    public void GivenMappingWithoutGroups_WhenValidating_ThenReportsError()
    {
        // Arrange
        var map = new Expressions.MapExpression(
            url: "http://example.org/test",
            identifier: "Test",
            new List<Expressions.UsesExpression>(),
            new List<Expressions.ImportsExpression>(),
            new List<Expressions.GroupExpression>());  // No groups

        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "NO_GROUPS");
    }

    [Fact]
    public void GivenMappingWithDuplicateGroupNames_WhenValidating_ThenReportsError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group Transform(source src : Observation, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "DUPLICATE_GROUP");
    }

    #endregion

    #region Group Validation Tests

    [Fact]
    public void GivenGroupWithoutParameters_WhenValidating_ThenReportsWarning()
    {
        // Arrange - Groups without parameters are unusual but not invalid
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform() {
  // No parameters - unusual
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.Warnings.ShouldContain(w => w.Code == "NO_PARAMETERS");
    }

    [Fact]
    public void GivenGroupWithoutRules_WhenValidating_ThenReportsWarning()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  // No rules
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.Warnings.ShouldContain(w => w.Code == "NO_RULES");
    }

    [Fact]
    public void GivenGroupExtendingNonExistent_WhenValidating_ThenReportsError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) extends NonExistent {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "MISSING_BASE_GROUP");
    }

    [Fact]
    public void GivenCircularInheritance_WhenValidating_ThenReportsError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Group1(source src : Patient, target tgt : Bundle) extends Group2 {
  src.id -> tgt.id;
}

group Group2(source src : Patient, target tgt : Bundle) extends Group1 {
  src.name -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "CIRCULAR_INHERITANCE");
    }

    #endregion

    #region Rule Validation Tests

    [Fact]
    public void GivenRuleWithoutSources_WhenValidating_ThenReportsError()
    {
        // This would be caught by parser, but we test validator logic
        // Create invalid rule manually
        var map = new Expressions.MapExpression(
            url: "http://example.org/test",
            identifier:"Test",
            new List<Expressions.UsesExpression>(),
            new List<Expressions.ImportsExpression>(),
            new List<Expressions.GroupExpression>
            {
                new Expressions.GroupExpression(
                    "Group1",
                    new List<Expressions.ParameterExpression>
                    {
                        new Expressions.ParameterExpression(Expressions.ParameterMode.Source, "src", "Patient")
                    },
                    null,
                    new List<Expressions.RuleExpression>
                    {
                        new Expressions.RuleExpression(
                            null,
                            new List<Expressions.SourceExpression>(),  // No sources
                            new List<Expressions.TargetExpression>(),
                            null)
                    })
            });

        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "NO_SOURCES");
    }

    [Fact]
    public void GivenRuleWithoutTargets_WhenValidating_ThenReportsWarning()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.Warnings.ShouldContain(w => w.Code == "NO_TARGETS");
    }

    #endregion

    #region Transform Validation Tests

    [Fact]
    public void GivenUnknownTransformFunction_WhenValidating_ThenReportsWarning()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id = unknownFunction(src.id);
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.Warnings.ShouldContain(w => w.Code == "UNKNOWN_TRANSFORM");
    }

    [Fact]
    public void GivenCreateWithoutArguments_WhenValidating_ThenReportsError()
    {
        // Arrange - create() requires a type argument
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.entry = create();
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "MISSING_ARGUMENT");
    }

    [Fact]
    public void GivenTranslateWithTooFewArgs_WhenValidating_ThenReportsError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.gender -> tgt.type = translate(src.gender);
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "MISSING_ARGUMENT");
    }

    #endregion

    #region Source Validation Tests

    [Fact]
    public void GivenSourceWithWhereAndDefault_WhenValidating_ThenReportsWarning()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.status default 'active' where status.exists() -> tgt.type;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.Warnings.ShouldContain(w => w.Code == "WHERE_WITH_DEFAULT");
    }

    #endregion

    #region Import Validation Tests

    [Fact]
    public void GivenImportWithoutUrl_WhenValidating_ThenReportsError()
    {
        // Arrange - manually create map with invalid import
        var map = new Expressions.MapExpression(
            url: "http://example.org/test",
            identifier:"Test",
            new List<Expressions.UsesExpression>(),
            new List<Expressions.ImportsExpression>
            {
                new Expressions.ImportsExpression("") // Empty URL
            },
            new List<Expressions.GroupExpression>
            {
                new Expressions.GroupExpression(
                    "Group1",
                    new List<Expressions.ParameterExpression>
                    {
                        new Expressions.ParameterExpression(Expressions.ParameterMode.Source, "src", "Patient")
                    },
                    null,
                    new List<Expressions.RuleExpression>())
            });

        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "MISSING_IMPORT_URL");
    }

    [Fact]
    public void GivenUnresolvedImport_WhenValidatingWithResolver_ThenReportsWarning()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'
imports 'http://example.org/fhir/StructureMap/NonExistent'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);

        var registry = new MapRegistry();
        var loader = new DictionaryMapLoader();
        var resolver = new ImportResolver(registry, compiler, loader);

        var validator = new MappingValidator(resolver);

        // Act
        var result = validator.Validate(map);

        // Assert
        result.Warnings.ShouldContain(w => w.Code == "UNRESOLVED_IMPORT");
    }

    #endregion

    #region Validation Result Tests

    [Fact]
    public void GivenValidationResult_WhenGettingSummary_ThenReturnsCorrectSummary()
    {
        // Arrange
        var result = new ValidationResult();

        // Act
        var summary = result.GetSummary();

        // Assert
        summary.ShouldContain("passed");
        summary.ShouldContain("no errors");
    }

    [Fact]
    public void GivenValidationResultWithWarnings_WhenGettingSummary_ThenIncludesWarningCount()
    {
        // Arrange
        var result = new ValidationResult();
        result.AddWarning("Test warning");

        // Act
        var summary = result.GetSummary();

        // Assert
        summary.ShouldContain("1 warning");
    }

    [Fact]
    public void GivenValidationResultWithErrors_WhenGettingSummary_ThenIncludesErrorCount()
    {
        // Arrange
        var result = new ValidationResult();
        result.AddError("Test error");

        // Act
        var summary = result.GetSummary();

        // Assert
        summary.ShouldContain("failed");
        summary.ShouldContain("1 error");
    }

    [Fact]
    public void GivenValidationError_WhenToString_ThenFormatsCorrectly()
    {
        // Arrange
        var error = new ValidationError("Test message", "Group: Test", "TEST_CODE");

        // Act
        var result = error.ToString();

        // Assert
        result.ShouldContain("Group: Test");
        result.ShouldContain("Test message");
        result.ShouldContain("[TEST_CODE]");
    }

    [Fact]
    public void GivenMergedValidationResults_WhenMerging_ThenCombinesErrorsAndWarnings()
    {
        // Arrange
        var result1 = new ValidationResult();
        result1.AddError("Error 1");
        result1.AddWarning("Warning 1");

        var result2 = new ValidationResult();
        result2.AddError("Error 2");
        result2.AddWarning("Warning 2");

        // Act
        result1.Merge(result2);

        // Assert
        result1.Errors.Count.ShouldBe(2);
        result1.Warnings.Count.ShouldBe(2);
    }

    #endregion

    #region Complex Validation Tests

    [Fact]
    public void GivenComplexValidMapping_WhenValidating_ThenPassesAllChecks()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Base(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group Extended(source src : Patient, target tgt : Bundle) extends Base {
  src.name -> tgt.entry = create('BundleEntry');
  src.active default true -> tgt.type;
  src.gender where gender.exists() check gender.length() > 0 log 'gender processed' -> tgt.total;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var validator = new MappingValidator();

        // Act
        var result = validator.Validate(map);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    #endregion
}
