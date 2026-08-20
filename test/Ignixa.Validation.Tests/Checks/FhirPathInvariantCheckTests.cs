// <copyright file="FhirPathInvariantCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for FhirPathInvariantCheck.
/// Tests universal constraints (ele-1, dom-1) and resource-specific constraints.
/// </summary>
public class FhirPathInvariantCheckTests
{
    private readonly ISchema _schema;
    private readonly FhirPathParser _parser;

    public FhirPathInvariantCheckTests()
    {
        _schema = new R4CoreSchemaProvider();
        _parser = new FhirPathParser();
    }

    #region Universal Constraints

    /// <summary>
    /// Tests ele-1: All FHIR elements must have a @value or children.
    /// </summary>
    [Fact]
    public void GivenElementWithValue_WhenValidatingEle1_ThenReturnsSuccess()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "ele-1",
            Severity = ConstraintSeverity.Error,
            Human = "All FHIR elements must have a @value or children",
            Expression = "hasValue() or (children().count() > id.count())",
            Xpath = null,
            AppliesTo = new[] { "Element" }
        };

        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"id\":\"123\",\"gender\":\"male\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Tests ele-1 failure: Element with neither value nor children (simplified).
    /// Uses simpler expression due to current FHIRPath engine limitations.
    /// </summary>
    [Fact]
    public void GivenElementWithoutValueOrChildren_WhenValidatingEle1_ThenReturnsError()
    {
        // Arrange - Simplified constraint using children().count()
        // Real ele-1 uses hasValue() which requires more FHIRPath implementation
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "ele-1",
            Severity = ConstraintSeverity.Error,
            Human = "All FHIR elements must have a @value or children",
            Expression = "children().count() > 0", // Simplified from hasValue() or (children().count() > id.count())
            Xpath = null,
            AppliesTo = new[] { "Element" }
        };

        // Empty object with no children
        var json = JsonNode.Parse("{}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        // ele-1 is exempted on the resource root itself - a resource's presence is guaranteed, and the
        // reference validator does not fire it there either. This test is about a nested element, so the
        // scope is rooted at the enclosing resource rather than at the element under test.
        var enclosingResource = JsonNodeSourceNode
            .Create(JsonNode.Parse("""{"resourceType":"Patient","id":"p"}""")!)
            .ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(enclosingResource);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "ele-1");
    }

    /// <summary>
    /// The same near-empty element that fails ele-1 as a nested element is exempt when it is the resource
    /// root: a resource's presence is guaranteed, and the reference validator does not fire ele-1 there
    /// either. Without the exemption an otherwise-legal resource carrying only an id would be rejected.
    /// </summary>
    [Fact]
    public void GivenNearEmptyResourceRoot_WhenValidatingEle1_ThenTheRootIsExempt()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "ele-1",
            Severity = ConstraintSeverity.Error,
            Human = "All FHIR elements must have a @value or children",
            Expression = "hasValue() or (children().count() > id.count())",
            Xpath = null,
            AppliesTo = new[] { "Element" }
        };

        var element = JsonNodeSourceNode
            .Create(JsonNode.Parse("""{"resourceType":"Patient","id":"only-an-id"}""")!)
            .ToElement(TestSchemaProvider.GetR4Schema());
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };

        // The element under validation IS the scope root, which is what triggers the exemption.
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Tests simplified constraint for contained resources (replaces dom-1 test).
    /// Real dom-1 requires %resource variable and advanced FHIRPath features not yet implemented.
    /// </summary>
    [Fact]
    public void GivenContainedResourceWithReference_WhenValidatingSimplifiedConstraint_ThenReturnsSuccess()
    {
        // Arrange - Simplified test that validates contained resources exist
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "test-contained",
            Severity = ConstraintSeverity.Error,
            Human = "Contained resources must exist",
            Expression = "contained.count() > 0", // Simplified - just check contained count
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Practitioner"",
                    ""id"": ""p1"",
                    ""name"": [{""family"": ""House""}]
                }
            ],
            ""generalPractitioner"": [
                {
                    ""reference"": ""#p1""
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Resource-Specific Constraints

    /// <summary>
    /// Tests pat-1: Patient.contact SHALL have at least one of name, telecom, or address.
    /// </summary>
    [Fact]
    public void GivenPatientContactWithName_WhenValidatingPat1_ThenReturnsSuccess()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pat-1",
            Severity = ConstraintSeverity.Error,
            Human = "Contact SHALL have at least one of name, telecom, or address",
            Expression = "name.exists() or telecom.exists() or address.exists()",
            Xpath = null,
            AppliesTo = new[] { "Patient.contact" }
        };

        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""contact"": [
                {
                    ""name"": {""family"": ""Doe""}
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json)!.Children("contact").First();
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Tests simplified obs-7: Observation.component SHALL have a value (simplified expression).
    /// Uses basic child navigation instead of polymorphic value[x] matching.
    /// </summary>
    [Fact]
    public void GivenObservationComponentWithValue_WhenValidatingObs7_ThenReturnsSuccess()
    {
        // Arrange - Simplified to use explicit property name
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "obs-7",
            Severity = ConstraintSeverity.Error,
            Human = "Component must have a value",
            Expression = "valueQuantity.exists()", // Simplified from polymorphic value.exists()
            Xpath = null,
            AppliesTo = new[] { "Observation.component" }
        };

        var json = JsonNode.Parse(@"{
            ""code"": {""text"": ""Systolic BP""},
            ""valueQuantity"": {""value"": 120, ""unit"": ""mmHg""}
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Tests bdl-7: FullUrl must be unique in a bundle, or else entries with the same fullUrl must have different meta.versionId.
    /// </summary>
    [Fact]
    public void GivenBundleWithUniqueFullUrls_WhenValidatingBdl7_ThenReturnsSuccess()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "bdl-7",
            Severity = ConstraintSeverity.Error,
            Human = "FullUrl must be unique in a bundle, or else entries with the same fullUrl must have different meta.versionId",
            Expression = "entry.where(fullUrl.exists()).select(fullUrl&resource.meta.versionId).isDistinct()",
            Xpath = null,
            AppliesTo = new[] { "Bundle" }
        };

        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""searchset"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/Patient/1"",
                    ""resource"": {""resourceType"": ""Patient"", ""id"": ""1""}
                },
                {
                    ""fullUrl"": ""http://example.org/Patient/2"",
                    ""resource"": {""resourceType"": ""Patient"", ""id"": ""2""}
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Warning Constraints

    /// <summary>
    /// Tests warning-level constraint (hypothetical example).
    /// </summary>
    [Fact]
    public void GivenWarningConstraintFailure_WhenValidating_ThenReturnsWarningIssue()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "test-warn",
            Severity = ConstraintSeverity.Warning,
            Human = "This is a warning constraint",
            Expression = "gender = 'other'",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""gender"":""male""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid); // Warnings don't fail validation
        Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
        Assert.Equal("test-warn", result.Issues[0].Code);
    }

    #endregion

    #region Tier Filtering

    /// <summary>
    /// Tests that invariant checks are skipped when ValidationDepth is Minimal.
    /// </summary>
    [Fact]
    public void GivenMinimalDepth_WhenValidating_ThenSkipsInvariantCheck()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "test-constraint",
            Severity = ConstraintSeverity.Error,
            Human = "This should not run in Minimal depth",
            Expression = "false", // Always fails
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Minimal };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Tests handling of invalid FHIRPath expression.
    /// Parse failure must not be reported as a constraint violation — it yields a
    /// non-failing Warning so callers can distinguish "constraint failed" from "could not evaluate".
    /// </summary>
    [Fact]
    public void GivenUnparseableExpression_WhenValidating_ThenResultIsNonFailingWithWarning()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "bad-expr",
            Severity = ConstraintSeverity.Error,
            Human = "Invalid expression",
            Expression = "this is not valid FHIRPath !!!",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert — parse failure must NOT fail validation
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotBeEmpty();
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("bad-expr");
        result.Issues[0].Message.ShouldContain("could not be evaluated");
        result.Issues[0].Message.ShouldNotContain("Invalid expression");
    }

    /// <summary>
    /// A parse failure on a warning-severity constraint must also be non-failing and carry
    /// a Warning issue — parse failure severity is independent of the declared constraint severity.
    /// </summary>
    [Fact]
    public void GivenUnparseableExpressionWithWarningSeverity_WhenValidating_ThenResultIsNonFailingWithWarning()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "bad-warn",
            Severity = ConstraintSeverity.Warning,
            Human = "Invalid warning expression",
            Expression = "@@@ totally invalid @@@",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotBeEmpty();
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("bad-warn");
        result.Issues[0].Message.ShouldContain("could not be evaluated");
        result.Issues[0].Message.ShouldNotContain("Invalid warning expression");
    }

    /// <summary>
    /// Validates the same unparseable constraint across multiple calls — the Lazy must
    /// retain the null result and keep returning the non-failing Warning each time.
    /// </summary>
    [Fact]
    public void GivenUnparseableExpression_WhenValidatingMultipleTimes_ThenAlwaysNonFailing()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "repeat-bad",
            Severity = ConstraintSeverity.Error,
            Human = "Repeated bad expression",
            Expression = "!!! bad !!!",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act & Assert — each call must remain non-failing
        for (var i = 0; i < 3; i++)
        {
            var result = check.Validate(element, settings, state);
            result.IsValid.ShouldBeTrue();
            result.Issues[0].Message.ShouldContain("could not be evaluated");
        }
    }

    /// <summary>
    /// An expression that parses but throws at evaluation time — an unimplemented FHIRPath function
    /// such as conformsTo() — is a validator/engine limitation, not a resource error. It must yield a
    /// non-failing Warning ("could not be evaluated"), matching the parse-failure contract, rather
    /// than a failing Error. Regression guard for conformance over-strictness (txt-1/htmlChecks etc.).
    /// </summary>
    [Fact]
    public void GivenExpressionThatThrowsAtEvaluation_WhenValidating_ThenResultIsNonFailingWithWarning()
    {
        // Arrange — conformsTo() parses fine but throws NotSupportedException at evaluation.
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "eng-1",
            Severity = ConstraintSeverity.Error,
            Human = "Uses an unimplemented function",
            Expression = "conformsTo('http://hl7.org/fhir/StructureDefinition/Patient')",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert — engine limitation must NOT fail validation
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotBeEmpty();
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("eng-1");
        result.Issues[0].Message.ShouldContain("could not be evaluated");
    }

    /// <summary>
    /// The same contract for the other kind of engine-signalled error: a constraint whose boolean operand
    /// is a repeating element, which FHIRPath's Singleton Evaluation of Collections makes an error rather
    /// than a truthy existence check.
    /// </summary>
    /// <remarks>
    /// This is the case the spec works through by name - <c>Patient.active and Patient.gender and
    /// Patient.telecom</c> "will result in an error because of the multiple telecom elements" - and it is
    /// exactly the shape a hand-written invariant falls into, so enforcing the rule in the engine has to
    /// be paired with proof that it cannot reject a conformant resource. The verdict must stay
    /// <c>IsValid</c>: the defect is in the constraint's text, not in the instance.
    /// </remarks>
    [Fact]
    public void GivenConstraintWithMultiItemBooleanOperand_WhenValidating_ThenResultIsNonFailingWithWarning()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "sing-1",
            Severity = ConstraintSeverity.Error,
            Human = "Uses a repeating element as a boolean operand",
            Expression = "active and gender and telecom",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse("""
        {
          "resourceType": "Patient",
          "active": true,
          "gender": "male",
          "telecom": [
            { "system": "phone", "value": "555-1111" },
            { "system": "email", "value": "a@b.example" }
          ]
        }
        """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert - an ill-formed constraint must not reject a conformant resource
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotBeEmpty();
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("sing-1");
        result.Issues[0].Message.ShouldContain("could not be evaluated");
    }

    /// <summary>
    /// Tests expression that returns empty collection (treated as false).
    /// </summary>
    [Fact]
    public void GivenExpressionReturningEmpty_WhenValidating_ThenReturnsFalse()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "empty-result",
            Severity = ConstraintSeverity.Error,
            Human = "Empty result is false",
            Expression = "name.where(family = 'Nonexistent')", // Returns empty collection
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""name"":[{""family"":""Doe""}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("empty-result", result.Issues[0].Code);
    }

    /// <summary>
    /// Tests expression that returns non-boolean value (treated as true if non-empty).
    /// </summary>
    [Fact]
    public void GivenExpressionReturningInteger_WhenValidating_ThenReturnsTrueIfNonZero()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "count-check",
            Severity = ConstraintSeverity.Error,
            Human = "Must have at least one name",
            Expression = "name.count() > 0",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""name"":[{""family"":""Doe""}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    /// <summary>
    /// Pins the "expression yields true" branch: the constraint passes and no issue is
    /// raised at all. Companion to the false/empty/exception branches pinned below, so a
    /// refactor that touches <see cref="FhirPathInvariantCheck.IsResultTrue"/> or the
    /// success path has a positive control to fail against too.
    /// </summary>
    [Fact]
    public void GivenExpressionYieldingTrue_WhenValidating_ThenValidWithNoIssues()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-true",
            Severity = ConstraintSeverity.Error,
            Human = "Gender must exist",
            Expression = "gender = 'male'",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""gender"":""male""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    /// <summary>
    /// Pins the "expression yields false" branch on an Error-severity constraint: invalid,
    /// with an Error-severity issue. Paired with
    /// <see cref="GivenExpressionYieldingFalseOnWarningConstraint_WhenValidating_ThenValidWithWarning"/>,
    /// which proves the issue's severity is read from <c>_constraint.Severity</c> rather than
    /// hardcoded to Error - a refactor that hardcodes either value passes exactly one of the pair.
    /// </summary>
    [Fact]
    public void GivenExpressionYieldingFalse_WhenValidating_ThenInvalidWithErrorSeverity()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-false-error",
            Severity = ConstraintSeverity.Error,
            Human = "Gender must be female",
            Expression = "gender = 'female'",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""gender"":""male""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Error);
        result.Issues[0].Code.ShouldBe("pin-false-error");
    }

    /// <summary>
    /// Same false-evaluating expression as
    /// <see cref="GivenExpressionYieldingFalse_WhenValidating_ThenInvalidWithErrorSeverity"/>, but the
    /// constraint itself declares Warning severity. The result must stay non-failing
    /// (<c>IsValid: true</c>) with a Warning-severity issue - proving severity is sourced from the
    /// constraint's own declared severity, not hardcoded to Error in the false-branch.
    /// </summary>
    [Fact]
    public void GivenExpressionYieldingFalseOnWarningConstraint_WhenValidating_ThenValidWithWarning()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-false-warning",
            Severity = ConstraintSeverity.Warning,
            Human = "Gender should be female",
            Expression = "gender = 'female'",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""gender"":""male""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("pin-false-warning");
    }

    /// <summary>
    /// Pins the "expression yields empty" branch to the same outcome as an explicit
    /// <c>false</c> - invalid, with the issue carrying the constraint's own declared severity
    /// (Error here, exercised at Warning by the false-branch pair above).
    /// </summary>
    /// <remarks>
    /// This is the branch most likely to be "fixed" by someone who reasons that an empty result
    /// is indeterminate rather than failing. It is not: FHIR's <c>conformance-rules.html</c> for
    /// R4, R5 and R6 all require the constraint expression to "evaluate to true" - not merely "not
    /// evaluate to false" - so empty must fail alongside false, and FHIRPath's singleton-coercion
    /// rule (used elsewhere to fold a one-item collection into a boolean) never applies to the
    /// empty collection. Firely's reference implementation encodes the same reading:
    /// <c>FhirPathValidator:177</c> evaluates constraints with the strict <c>IsTrue</c> predicate
    /// (<c>result is not null &amp;&amp; result.Value</c>), which is deliberately distinct from the
    /// lenient <c>Predicate</c> helper it uses elsewhere for slicing discriminators - <c>Predicate</c>'s
    /// IL is <c>HasValue ? Value : true</c>, so under it empty means "matches", the opposite of
    /// <c>IsTrue</c>'s "constraint violated". Spec, Firely and this code all agree: don't relax this.
    /// </remarks>
    [Fact]
    public void GivenExpressionYieldingEmpty_WhenValidating_ThenInvalidWithSameSeverityAsFalse()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-empty",
            Severity = ConstraintSeverity.Error,
            Human = "Must have a name with family 'Nonexistent'",
            Expression = "name.where(family = 'Nonexistent')",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""name"":[{""family"":""Doe""}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert - same shape as the false-branch: invalid, Error severity (the constraint's own)
        result.IsValid.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Error);
        result.Issues[0].Code.ShouldBe("pin-empty");
    }

    /// <summary>
    /// Pins the <see cref="NotSupportedException"/> branch: an unimplemented FHIRPath function
    /// (<c>conformsTo()</c>) is an engine gap, not a resource defect, so evaluation degrades to a
    /// non-failing Warning rather than rejecting the resource.
    /// </summary>
    [Fact]
    public void GivenConstraintThrowingNotSupportedException_WhenValidating_ThenValidWithWarning()
    {
        // Arrange - conformsTo() parses fine but throws NotSupportedException at evaluation
        // (FhirSpecificFunctions.ConformsTo: "requires profile validation infrastructure").
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-notsupported",
            Severity = ConstraintSeverity.Error,
            Human = "Uses an unimplemented function",
            Expression = "conformsTo('http://hl7.org/fhir/StructureDefinition/Patient')",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("pin-notsupported");
    }

    /// <summary>
    /// Pins the <see cref="FhirPathEvaluationException"/> branch using R4's actual <c>tim-9</c>
    /// expression: <c>offset.empty() or (when.exists() and ((when in ('C' | 'CM' | 'CD' |
    /// 'CV')).not()))</c>. <c>Timing.repeat.when</c> is <c>0..*</c>, and FHIRPath's <c>in</c>
    /// operator requires a singleton left operand (<see cref="FhirPathEvaluator.EvaluateMembership"/>),
    /// so a <c>repeat</c> with two <c>when</c> codes makes the engine correctly refuse to evaluate -
    /// this is a defect in the constraint's text (R5 rewrote it as
    /// <c>when.select($this in (...)).allFalse()</c> for exactly this reason), not evidence the
    /// instance is invalid. <c>offset</c> must be present so <c>offset.empty()</c> is false and the
    /// short-circuiting <c>or</c> does not skip the ill-formed right-hand side.
    /// </summary>
    [Fact]
    public void GivenConstraintThrowingFhirPathEvaluationException_WhenValidating_ThenValidWithWarning()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "tim-9",
            Severity = ConstraintSeverity.Error,
            Human = "If there's an offset, there must be a when (and not C, CM, CD, CV)",
            Expression = "offset.empty() or (when.exists() and ((when in ('C' | 'CM' | 'CD' | 'CV')).not()))",
            Xpath = null,
            AppliesTo = new[] { "Timing.repeat" }
        };

        var json = JsonNode.Parse(@"{""offset"":15,""when"":[""MORN"",""EVE""]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert - an ill-formed constraint must not reject a conformant instance
        result.IsValid.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("tim-9");
    }

    /// <summary>
    /// Pins the <c>FhirPathEvaluationException</c> branch for the case the fix was written for: a
    /// function invoked without a required argument. A profile author writing <c>name.skip()</c> has
    /// produced an ill-formed expression, which the engine must refuse to evaluate - but that is a
    /// defect in the constraint, not in the instance, so it degrades to a non-failing Warning on the
    /// same footing as <c>tim-9</c> above rather than rejecting the resource.
    /// <para>
    /// The four cases cover the three collection functions whose argument checks sit on the hot
    /// navigation path plus one string function, so a regression confined to a single function file is
    /// still caught. The message assertion is deliberately split: <c>ShouldStartWith</c> pins the
    /// <em>tier</em> - the catch-all at <c>FhirPathInvariantCheck</c> formats its issue as
    /// "<c>{key}: unexpected error evaluating FHIRPath expression</c>", so a tier regression cannot
    /// satisfy this prefix - while <c>ShouldContain</c> pins that the refusal still names the offending
    /// function, which is the only thing that makes the warning actionable.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("name.skip()", "skip()")]
    [InlineData("name.take()", "take()")]
    [InlineData("name.where()", "where()")]
    [InlineData("name.first().family.substring()", "substring()")]
    public void GivenConstraintWithMissingFunctionArgument_WhenValidating_ThenValidWithWarning(
        string expression,
        string function)
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-missing-argument",
            Severity = ConstraintSeverity.Error,
            Human = "Malformed function invocation",
            Expression = expression,
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""name"":[{""family"":""Doe""}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("pin-missing-argument");
        result.Issues[0].Message.ShouldStartWith("Constraint 'pin-missing-argument' could not be evaluated:");
        result.Issues[0].Message.ShouldContain(function);
    }

    /// <summary>
    /// Pins the catch-all branch: an evaluation failure that is neither a known engine gap
    /// (<see cref="NotSupportedException"/>) nor a correctly-signalled refusal
    /// (<c>FhirPathEvaluationException</c>) is an unexpected engine defect and must fail loudly -
    /// invalid, Error severity - rather than being masked as a benign warning.
    /// <para>
    /// The vehicle is a stub <see cref="IElement"/> whose <see cref="IElement.Children"/> throws a bare
    /// <see cref="InvalidOperationException"/>. <c>Validate</c>'s catch clauses are, in order,
    /// <see cref="OperationCanceledException"/>, <see cref="NotSupportedException"/>,
    /// <c>FhirPathEvaluationException</c>, then <see cref="Exception"/>; there is no
    /// <c>catch (InvalidOperationException)</c>, so a bare one falls through to the catch-all even
    /// though <c>FhirPathEvaluationException</c> derives from it. The stub therefore asserts the tier
    /// boundary directly, which is what this test is for.
    /// </para>
    /// <para>
    /// It did not always. This pin previously rode on <c>substring()</c> called with no arguments,
    /// purely because that happened to throw a plain <see cref="ArgumentException"/>. Converting the
    /// function library to signal argument refusals as <c>FhirPathEvaluationException</c> re-tiered that
    /// expression to a Warning - correctly - and the pin silently stopped reaching the branch it named.
    /// No expression-level vehicle survives the conversion, and depending on one again would reintroduce
    /// exactly that fragility: a pin on the catch-all must not be hostage to some unrelated function's
    /// incidental exception type.
    /// </para>
    /// </summary>
    [Fact]
    public void GivenConstraintThrowingUnclassifiedException_WhenValidating_ThenInvalidWithError()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-unexpected",
            Severity = ConstraintSeverity.Error,
            Human = "Any well-formed constraint - the defect being pinned is in the engine",
            Expression = "name.exists()",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""name"":[{""family"":""Doe""}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var realElement = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        // A Patient root that is well-formed in every respect the check inspects before evaluating -
        // InstanceType for the AppliesTo filter, Location for the issue - but whose navigation fails the
        // way an engine defect would: an exception carrying no evaluation-tier signal at all.
        var element = Substitute.For<IElement>();
        element.Name.Returns(realElement.Name);
        element.InstanceType.Returns(realElement.InstanceType);
        element.Location.Returns(realElement.Location);
        element.Type.Returns(realElement.Type);
        element.Value.Returns(realElement.Value);
        element.HasPrimitiveValue.Returns(realElement.HasPrimitiveValue);
        element.Children(Arg.Any<string?>())
            .Returns<IReadOnlyList<IElement>>(_ =>
                throw new InvalidOperationException("simulated engine defect while reading children"));

        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert - an unexpected engine defect must be loud, not swallowed
        result.IsValid.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Error);
        result.Issues[0].Code.ShouldBe("pin-unexpected");
        result.Issues[0].Message.ShouldStartWith(
            "pin-unexpected: unexpected error evaluating FHIRPath expression:");
    }

    /// <summary>
    /// Pins the tier <c>repeatAll()</c>'s runaway-iteration guard now lands in. The guard fires after
    /// 100,000 passes (<c>CollectionFunctions.RepeatAll</c>) and signals
    /// <c>FhirPathEvaluationException</c> rather than the bare <see cref="InvalidOperationException"/> it
    /// used to, which moves a non-converging constraint expression off the catch-all's
    /// resource-rejecting Error and onto the non-failing Warning tier.
    /// <para>
    /// That is the right tier - the guard bounds a runaway <em>user expression</em>, so a projection
    /// that never terminates is a defect in the constraint's text and not evidence the instance is
    /// invalid - but it is a wider behaviour change than the missing-argument refusals it shipped
    /// alongside, and it is the only one that alters whether a resource is rejected without any
    /// malformed call being involved. It is pinned separately for that reason.
    /// <c>repeatAll($this)</c> re-enqueues its own input on every pass and therefore never converges.
    /// </para>
    /// </summary>
    [Fact]
    public void GivenConstraintExceedingRepeatAllIterationLimit_WhenValidating_ThenValidWithWarning()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "pin-runaway-repeatall",
            Severity = ConstraintSeverity.Error,
            Human = "Projection that never converges",
            Expression = "repeatAll($this).exists()",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""name"":[{""family"":""Doe""}]}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert - a runaway expression is a constraint defect, so it must not reject the instance
        result.IsValid.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].Severity.ShouldBe(IssueSeverity.Warning);
        result.Issues[0].Code.ShouldBe("pin-runaway-repeatall");
        result.Issues[0].Message.ShouldStartWith("Constraint 'pin-runaway-repeatall' could not be evaluated:");
        result.Issues[0].Message.ShouldContain("maximum iteration limit");
    }

    #region Performance

    /// <summary>
    /// Tests that FHIRPath expressions are compiled only once (lazy evaluation) by
    /// observing the private <c>_compiledExpression</c> <see cref="Lazy{T}"/> field via
    /// reflection: it must report <c>IsValueCreated</c> after the first evaluation, and the
    /// same <see cref="FhirPath.Expressions.Expression"/> instance (by reference) must be
    /// reused across every subsequent call. If the check re-parsed on each call, a fresh
    /// AST would be produced each time and the reference-equality assertion would fail.
    /// </summary>
    [Fact]
    public void GivenMultipleValidations_WhenValidating_ThenCompilesExpressionOnce()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "perf-test",
            Severity = ConstraintSeverity.Error,
            Human = "Performance test",
            Expression = "gender.exists()",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var json = JsonNode.Parse(@"{""resourceType"":""Patient"",""gender"":""male""}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = ValidationState.ForRoot(element);

        var compiledExpressionField = typeof(FhirPathInvariantCheck).GetField(
            "_compiledExpression",
            BindingFlags.NonPublic | BindingFlags.Instance);
        compiledExpressionField.ShouldNotBeNull("test relies on the private lazy-compilation field existing");
        var lazyBeforeAnyCall = (Lazy<FhirPath.Expressions.Expression?>)compiledExpressionField!.GetValue(check)!;
        lazyBeforeAnyCall.IsValueCreated.ShouldBeFalse("expression must not be parsed until first Validate() call");

        // Act - Run validation multiple times
        FhirPath.Expressions.Expression? firstCompiledExpression = null;
        for (int i = 0; i < 10; i++)
        {
            var result = check.Validate(element, settings, state);
            Assert.True(result.IsValid);

            var lazyAfterCall = (Lazy<FhirPath.Expressions.Expression?>)compiledExpressionField.GetValue(check)!;
            lazyAfterCall.IsValueCreated.ShouldBeTrue();

            if (firstCompiledExpression is null)
            {
                firstCompiledExpression = lazyAfterCall.Value;
            }
            else
            {
                ReferenceEquals(lazyAfterCall.Value, firstCompiledExpression).ShouldBeTrue(
                    "the same compiled Expression instance must be reused across calls, not re-parsed");
            }
        }
    }

    #endregion
}
