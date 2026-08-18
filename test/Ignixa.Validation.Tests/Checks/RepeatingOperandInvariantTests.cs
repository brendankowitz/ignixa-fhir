// <copyright file="RepeatingOperandInvariantTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Guards the boundary between "the engine refused to evaluate this constraint" and "this resource is
/// invalid", using the shipped R4 invariant that actually crosses it.
/// </summary>
/// <remarks>
/// <para>
/// R4/R4B/STU3 ship <c>tim-9</c> as
/// <c>offset.empty() or (when.exists() and ((when in ('C' | 'CM' | 'CD' | 'CV')).not()))</c>.
/// <c>Timing.repeat.when</c> is 0..*, so two or more codes hand <c>in</c> a multi-item left operand,
/// which FHIRPath requires the engine to signal an error for. R5 replaced the expression with
/// <c>when.select($this in (…)).allFalse()</c> precisely because the R4 form is ill-formed for a
/// repeating element.
/// </para>
/// <para>
/// The engine is right to refuse. What must not happen is the refusal being reported as a resource
/// error: an unevaluable constraint is a defect in the constraint, not evidence about the instance.
/// </para>
/// <para>
/// The constraint text is read out of <see cref="R4CoreSchemaProvider"/> rather than pasted here, so
/// these tests track whatever the spec actually ships. The element under test is navigated out of a
/// realistic <c>ServiceRequest</c> rather than hand-built, because <c>Timing.repeat</c> is where the
/// invariant is declared and therefore the altitude the validator evaluates it at.
/// </para>
/// </remarks>
public class RepeatingOperandInvariantTests
{
    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenShippedR4Tim9_WhenInspectingTheExpression_ThenItStillUsesTheRepeatingInForm()
    {
        // Arrange & Act
        var tim9 = ShippedTimingRepeatConstraint("tim-9");

        // Assert — the premise of every other test in this file.
        tim9.Expression.ShouldContain("when in (");
        tim9.Severity.ShouldBe("error", StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void GivenTimingRepeatWithMultipleWhenCodes_WhenValidatingTim9_ThenResourceStaysValidWithAWarning()
    {
        // Arrange — two 'when' codes make the left operand of 'in' a two-item collection.
        var repeat = ServiceRequestTimingRepeat("""
            "when": ["MORN", "EVE"],
            "offset": 30
        """);

        // Act
        var result = ValidateTim9(repeat);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldNotBeEmpty();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Warning);
        result.Issues.ShouldContain(i => i.Code == "tim-9" && i.Message.Contains("could not be evaluated"));
    }

    /// <summary>
    /// The guard clause tim-9 opens with is only a guard if <c>or</c> short-circuits. With no
    /// <c>offset</c>, <c>offset.empty()</c> is true and the spec's truth table makes the whole expression
    /// true whatever the right operand would have been - so the ill-formed <c>in</c> is never reached and
    /// there is nothing to warn about.
    /// </summary>
    [Fact]
    public void GivenTimingRepeatWithMultipleWhenCodesAndNoOffset_WhenValidatingTim9_ThenTheGuardShortCircuitsAndNoIssueIsRaised()
    {
        // Arrange — same two 'when' codes, but the guard on the left of 'or' now holds.
        var repeat = ServiceRequestTimingRepeat("""
            "when": ["MORN", "EVE"]
        """);

        // Act
        var result = ValidateTim9(repeat);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTimingRepeatWithASingleAllowedWhenCode_WhenValidatingTim9_ThenNoIssueIsRaised()
    {
        // Arrange — one 'when' keeps 'in' singleton, so the constraint really is evaluated.
        var repeat = ServiceRequestTimingRepeat("""
            "when": ["MORN"],
            "offset": 30
        """);

        // Act
        var result = ValidateTim9(repeat);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldBeEmpty();
    }

    /// <summary>
    /// Negative control. Without it the tests above would pass just as happily if tim-9 had been
    /// neutered into something that can never fail; a single disallowed <c>when</c> keeps <c>in</c>
    /// singleton, so the engine evaluates the constraint and it must still reject the element.
    /// </summary>
    [Fact]
    public void GivenTimingRepeatWithADisallowedWhenCode_WhenValidatingTim9_ThenItStillFails()
    {
        // Arrange
        var repeat = ServiceRequestTimingRepeat("""
            "when": ["C"],
            "offset": 30
        """);

        // Act
        var result = ValidateTim9(repeat);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "tim-9" && i.Severity == IssueSeverity.Error);
    }

    private ValidationResult ValidateTim9(IElement repeat)
    {
        var shipped = ShippedTimingRepeatConstraint("tim-9");
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = shipped.Key,
            Severity = string.Equals(shipped.Severity, "warning", StringComparison.OrdinalIgnoreCase)
                ? ConstraintSeverity.Warning
                : ConstraintSeverity.Error,
            Human = shipped.Human ?? string.Empty,
            Expression = shipped.Expression,
            Xpath = shipped.Xpath,
            AppliesTo = new[] { "Timing.repeat" }
        };

        return new FhirPathInvariantCheck(constraint, _schema, _parser)
            .Validate(repeat, new ValidationSettings { Depth = ValidationDepth.Spec }, ValidationState.ForRoot(repeat));
    }

    private IConstraint ShippedTimingRepeatConstraint(string key)
    {
        var timing = _schema.GetTypeDefinition("Timing").ShouldNotBeNull();
        var repeat = timing.Children
            .FirstOrDefault(c => string.Equals(c.Info.Name, "repeat", StringComparison.Ordinal))
            .ShouldNotBeNull();

        return ((ITypeExtended)repeat).Constraints
            .FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal))
            .ShouldNotBeNull();
    }

    private static IElement ServiceRequestTimingRepeat(string repeatBody)
    {
        var resource = $$"""
        {
            "resourceType": "ServiceRequest",
            "id": "timing-example",
            "status": "active",
            "intent": "order",
            "subject": { "reference": "Patient/example" },
            "occurrenceTiming": {
                "repeat": {
                    "frequency": 2,
                    "period": 1,
                    "periodUnit": "d",
                    {{repeatBody}}
                }
            }
        }
        """;

        var node = JsonNodeSourceNode.Create(JsonNode.Parse(resource)!);
        var repeat = node.Children("occurrenceTiming").First().Children("repeat").First();
        return repeat.ToElement(TestSchemaProvider.GetR4Schema());
    }

    private static string Describe(ValidationResult result)
        => string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Code}:{i.Message}"));
}
