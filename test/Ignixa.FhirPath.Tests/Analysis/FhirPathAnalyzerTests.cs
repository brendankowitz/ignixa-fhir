// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class FhirPathAnalyzerTests
{
    private readonly IFhirSchemaProvider _schema;
    private readonly FhirPathAnalyzer _analyzer;
    private readonly FhirPathParser _parser;

    public FhirPathAnalyzerTests()
    {
        _schema = FhirVersion.R4.GetSchemaProvider();
        _analyzer = new FhirPathAnalyzer(_schema);
        _parser = new FhirPathParser();
    }

    #region Type Inference - Simple Property Access

    [Fact]
    public void GivenSimplePropertyAccess_WhenAnalyzing_ThenInfersCorrectType()
    {
        var result = _analyzer.Analyze("Patient.name", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("HumanName", result.TypeNames);
    }

    [Fact]
    public void GivenNestedPropertyAccess_WhenAnalyzing_ThenInfersCorrectType()
    {
        var result = _analyzer.Analyze("Patient.name.family", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    [Fact]
    public void GivenDeepNestedPropertyAccess_WhenAnalyzing_ThenInfersCorrectType()
    {
        var result = _analyzer.Analyze("Patient.name.given", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    [Fact]
    public void GivenResourceTypeAtRoot_WhenAnalyzing_ThenReturnsResourceType()
    {
        var result = _analyzer.Analyze("Patient", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("Patient", result.TypeNames);
    }

    #endregion

    #region Type Inference - Choice Types

    [Fact]
    public void GivenChoiceTypeProperty_WhenAnalyzing_ThenInfersAllPossibleTypes()
    {
        var result = _analyzer.Analyze("Observation.value", "Observation");

        Assert.True(result.IsValid);
        Assert.True(result.TypeNames.Count() > 1, "Choice type should have multiple possible types");
    }

    [Fact]
    public void GivenObservationEffective_WhenAnalyzing_ThenInfersDateTimeOrPeriod()
    {
        var result = _analyzer.Analyze("Observation.effective", "Observation");

        Assert.True(result.IsValid);
        var typeNames = result.TypeNames.ToList();
        Assert.True(typeNames.Contains("dateTime") || typeNames.Contains("Period") || typeNames.Contains("Timing"));
    }

    #endregion

    #region Type Inference - Function Calls

    [Fact]
    public void GivenWhereFunction_WhenAnalyzing_ThenReturnsFocusType()
    {
        var result = _analyzer.Analyze("Patient.name.where(use = 'official')", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("HumanName", result.TypeNames);
    }

    [Fact]
    public void GivenSelectFunction_WhenAnalyzing_ThenReturnsSelectedType()
    {
        var result = _analyzer.Analyze("Patient.name.select(family)", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    [Fact]
    public void GivenFirstFunction_WhenAnalyzing_ThenReturnsFocusType()
    {
        var result = _analyzer.Analyze("Patient.name.first()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("HumanName", result.TypeNames);
    }

    [Fact]
    public void GivenCountFunction_WhenAnalyzing_ThenReturnsInteger()
    {
        var result = _analyzer.Analyze("Patient.name.count()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("integer", result.TypeNames);
    }

    [Fact]
    public void GivenExistsFunction_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("Patient.name.exists()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenEmptyFunction_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("Patient.name.empty()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenOfTypeFunction_WhenAnalyzing_ThenFiltersToSpecifiedType()
    {
        var result = _analyzer.Analyze("Observation.value.ofType(Quantity)", "Observation");

        Assert.True(result.IsValid);
        Assert.Contains("Quantity", result.TypeNames);
    }

    [Fact]
    public void GivenToStringFunction_WhenAnalyzing_ThenReturnsString()
    {
        var result = _analyzer.Analyze("Patient.birthDate.toString()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    #endregion

    #region Type Inference - Binary Operators

    [Fact]
    public void GivenEqualityOperator_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("Patient.active = true", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenComparisonOperator_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("Patient.birthDate > @1990-01-01", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenUnionOperator_WhenAnalyzing_ThenCombinesTypes()
    {
        var result = _analyzer.Analyze("Patient.name.family | Patient.name.given", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    [Fact]
    public void GivenAndOperator_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("Patient.active and Patient.name.exists()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenConcatenationOperator_WhenAnalyzing_ThenReturnsString()
    {
        var result = _analyzer.Analyze("Patient.name.family & ', ' & Patient.name.given.first()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    #endregion

    #region Type Inference - Constants

    [Fact]
    public void GivenStringConstant_WhenAnalyzing_ThenReturnsString()
    {
        var result = _analyzer.Analyze("'hello'", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    [Fact]
    public void GivenIntegerConstant_WhenAnalyzing_ThenReturnsInteger()
    {
        var result = _analyzer.Analyze("42", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("integer", result.TypeNames);
    }

    [Fact]
    public void GivenDecimalConstant_WhenAnalyzing_ThenReturnsDecimal()
    {
        var result = _analyzer.Analyze("3.14", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("decimal", result.TypeNames);
    }

    [Fact]
    public void GivenBooleanConstant_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("true", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    #endregion

    #region Validation - Property Errors

    [Fact]
    public void GivenInvalidProperty_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.unknownProperty", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains("unknownProperty", result.Errors.First(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenInvalidNestedProperty_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.name.invalidField", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains("invalidField", result.Errors.First(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenInvalidRootType_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.name", "NonExistentType");

        Assert.False(result.IsValid);
    }

    #endregion

    #region Validation - Function Errors

    [Fact]
    public void GivenUnknownFunction_WhenAnalyzing_ThenReturnsWarning()
    {
        var result = _analyzer.Analyze("Patient.name.unknownFunc()", "Patient");

        Assert.True(result.HasWarnings);
        Assert.Contains("unknownFunc", result.Warnings.First(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenFunctionWithWrongArgCount_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.name.take()", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains("argument", result.Errors.First(), StringComparison.Ordinal);
    }

    #endregion

    #region Validation - Variable Resolution

    [Fact]
    public void GivenResourceVariable_WhenAnalyzing_ThenResolvesCorrectly()
    {
        var result = _analyzer.Analyze("%resource.name", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("HumanName", result.TypeNames);
    }

    [Fact]
    public void GivenContextVariable_WhenAnalyzing_ThenResolvesCorrectly()
    {
        var result = _analyzer.Analyze("%context.id", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("id", result.TypeNames);
    }

    [Fact]
    public void GivenUnknownVariable_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("%unknownVar", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains("unknownVar", result.Errors.First(), StringComparison.Ordinal);
    }

    #endregion

    #region Analysis Context

    [Fact]
    public void GivenAnalysisContext_WhenCreated_ThenHasCorrectRootType()
    {
        var context = AnalysisContext.Create(_schema, "Patient");

        Assert.Equal("Patient", context.RootType);
        Assert.NotNull(context.Schema);
    }

    [Fact]
    public void GivenAnalysisContext_WhenResolvingResourceVariable_ThenReturnsRootType()
    {
        var context = AnalysisContext.Create(_schema, "Patient");

        var resourceVar = context.ResolveVariable("resource");

        Assert.NotNull(resourceVar);
        Assert.Contains("Patient", resourceVar.TypeNames(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAnalysisContext_WhenAddingIssue_ThenIssueIsTracked()
    {
        var context = AnalysisContext.Create(_schema, "Patient");

        context.AddError("Test error");

        Assert.Single(context.Issues);
        Assert.Equal(ValidationIssueSeverity.Error, context.Issues[0].Severity);
    }

    #endregion

    #region Analysis Result

    [Fact]
    public void GivenValidAnalysis_WhenCheckingIsValid_ThenReturnsTrue()
    {
        var result = _analyzer.Analyze("Patient.name", "Patient");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GivenInvalidAnalysis_WhenCheckingIsValid_ThenReturnsFalse()
    {
        var result = _analyzer.Analyze("Patient.invalidProperty", "Patient");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void GivenAnalysisWithWarnings_WhenCheckingHasWarnings_ThenReturnsTrue()
    {
        var result = _analyzer.Analyze("Patient.name.unknownFunction()", "Patient");

        Assert.True(result.HasWarnings);
    }

    #endregion

    #region Complex Expression Analysis

    [Fact]
    public void GivenComplexWhereExpression_WhenAnalyzing_ThenInfersCorrectType()
    {
        var result = _analyzer.Analyze(
            "Patient.name.where(use = 'official').family.first()",
            "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    [Fact]
    public void GivenIndexerExpression_WhenAnalyzing_ThenReturnsSingleType()
    {
        var result = _analyzer.Analyze("Patient.name[0]", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("HumanName", result.TypeNames);
    }

    [Fact]
    public void GivenUnaryNotExpression_WhenAnalyzing_ThenReturnsBoolean()
    {
        var result = _analyzer.Analyze("Patient.active.not()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenIifFunction_WhenAnalyzing_ThenReturnsArgumentType()
    {
        var result = _analyzer.Analyze("iif(Patient.active, 'yes', 'no')", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("string", result.TypeNames);
    }

    #endregion

    #region InferTypes and Validate Methods

    [Fact]
    public void GivenExpression_WhenCallingInferTypes_ThenReturnsTypeSet()
    {
        var types = _analyzer.InferTypes("Patient.identifier", "Patient");

        Assert.True(types.Types.Count > 0);
        Assert.Contains("Identifier", types.TypeNames(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenValidExpression_WhenCallingValidate_ThenReturnsNoErrors()
    {
        var issues = _analyzer.Validate("Patient.name.family", "Patient");

        Assert.DoesNotContain(issues, i => i.Severity == ValidationIssueSeverity.Error);
    }

    [Fact]
    public void GivenInvalidExpression_WhenCallingValidate_ThenReturnsErrors()
    {
        var issues = _analyzer.Validate("Patient.invalidProp", "Patient");

        Assert.Contains(issues, i => i.Severity == ValidationIssueSeverity.Error);
    }

    [Fact]
    public void GivenParsedExpression_WhenAnalyzing_ThenReturnsResult()
    {
        var parsed = _parser.Parse("Patient.active");
        var result = _analyzer.Analyze(parsed, "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenEmptyExpression_WhenAnalyzing_ThenReturnsParseError()
    {
        var result = _analyzer.Analyze("", "Patient");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GivenMalformedExpression_WhenAnalyzing_ThenReturnsParseError()
    {
        var result = _analyzer.Analyze("Patient.name..family", "Patient");

        Assert.False(result.IsValid);
    }

    #endregion

    #region Scoped vs Non-Scoped Function Context Tests

    [Fact]
    public void GivenScopedFunction_WhenThisInCriteria_ThenThisIsCurrentItem()
    {
        // Scoped function (where) should set $this to the current item type
        // Patient.name.where($this.family = 'Smith')
        // Inside where(), $this should be HumanName (the item being tested)
        var result = _analyzer.Analyze("Patient.name.where($this.family = 'Smith')", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("HumanName", result.TypeNames);
    }

    [Fact]
    public void GivenNonScopedFunction_WhenThisInArgument_ThenThisIsOuterContext()
    {
        // Non-scoped function (contains) should NOT change $this
        // Patient.name.family.first().contains($this.id)
        // Inside contains(), $this should still be Patient (outer context), not string
        // So $this.id should resolve to Patient.id (id)
        var result = _analyzer.Analyze("Patient.name.family.first().contains($this.id)", "Patient");

        // This should be valid because $this.id resolves to Patient.id (id)
        // and id is compatible with contains()'s string argument
        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenNonScopedFunction_WhenThisUsedIncorrectly_ThenReportsError()
    {
        // If $this in non-scoped function argument incorrectly resolved to the string focus,
        // then $this.name would fail because string doesn't have 'name'
        // But with correct scoping, $this is Patient, and Patient.name is valid
        var result = _analyzer.Analyze("Patient.id.contains($this.name.first().family)", "Patient");

        // Should be valid: $this is Patient, $this.name.first().family is string
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenNestedScopedInNonScoped_WhenAnalyzing_ThenScopesCorrectly()
    {
        // Non-scoped (startsWith) contains scoped (where)
        // The where() should create its own scope, but startsWith shouldn't
        var result = _analyzer.Analyze(
            "Patient.name.family.first().startsWith(Patient.identifier.where($this.system = 'http://example.org').value.first())",
            "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("boolean", result.TypeNames);
    }

    [Fact]
    public void GivenLengthOnPatient_WhenAnalyzing_ThenReportsError()
    {
        // The exact Firely test case: '123456789'.contains(trace('t').length().toString())
        // trace('t') returns $this which is Patient (outer context), not string
        // length() on Patient should report an error
        var result = _analyzer.Analyze("'123456789'.contains($this.length().toString())", "Patient");

        // Should report error: length() is not supported on Patient
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i =>
            i.Message.Contains("length", StringComparison.OrdinalIgnoreCase) &&
            i.Message.Contains("Patient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenLengthOnString_WhenAnalyzing_ThenIsValid()
    {
        // Contrast: length() on a string should be valid
        var result = _analyzer.Analyze("Patient.name.family.first().length()", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("integer", result.TypeNames);
    }

    #endregion

    #region Instance Selector Type Inference

    [Fact]
    public void GivenCodingInstanceSelector_WhenAnalyzing_ThenInfersCodingType()
    {
        var result = _analyzer.Analyze("Coding { system: 'http://x', code: 'c1' }", "Patient");

        Assert.True(result.IsValid);
        Assert.Contains("Coding", result.TypeNames);
    }

    [Fact]
    public void GivenUnknownTypeInstanceSelector_WhenAnalyzing_ThenReturnsPrimitiveTypeName()
    {
        var result = _analyzer.Analyze("UnknownFhirType { value: 'x' }", "Patient");

        Assert.Contains("UnknownFhirType", result.TypeNames);
    }

    #endregion
}
