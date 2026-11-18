// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;
using ConstraintDefinition = Ignixa.Specification.ConstraintDefinition;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// A constraint that hands a function a repeating element as a single-item argument is refused by the
/// engine as an evaluation error, so it is a non-failing Warning (#537), the same as tim-9's repeating
/// operand. Before the engine reported it as an evaluation error, the raw
/// <see cref="InvalidOperationException"/> was read as an engine defect and failed the resource.
/// </summary>
public class MultiItemArgumentInvariantTests
{
    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenAnInvariantPassingARepeatingElementAsAnArgument_WhenValidating_ThenResourceStaysValidWithAWarning()
    {
        // Arrange - two given names make the startsWith() argument a two-item collection.
        var patient = Patient("""["Peter", "James"]""");

        // Act
        var result = Validate(patient, "'Peter'.startsWith(name.given)");

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Warning);
        result.Issues.ShouldContain(i => i.Code == "test-1" && i.Message.Contains("could not be evaluated"));
    }

    [Fact]
    public void GivenAnInvariantPassingASingleItemArgument_WhenValidating_ThenItIsEvaluatedAndCanStillFail()
    {
        // Arrange - negative control: one given name, and a prefix the string does not have.
        var patient = Patient("""["Peter"]""");

        // Act
        var result = Validate(patient, "'Peter'.startsWith(name.given.first() + 'x')");

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "test-1" && i.Severity == IssueSeverity.Error);
    }

    private ValidationResult Validate(IElement patient, string expression)
    {
        var constraint = new ConstraintDefinition
        {
            Key = "test-1",
            Severity = ConstraintSeverity.Error,
            Human = "Test constraint",
            Expression = expression,
            AppliesTo = ["Patient"],
        };

        return new FhirPathInvariantCheck(constraint, _schema, _parser)
            .Validate(patient, new ValidationSettings { Depth = ValidationDepth.Spec }, ValidationState.ForRoot(patient));
    }

    private IElement Patient(string givenJson)
    {
        var resource = $$"""
        {
            "resourceType": "Patient",
            "id": "p1",
            "name": [ { "family": "Chalmers", "given": {{givenJson}} } ]
        }
        """;

        return JsonNodeSourceNode.Create(JsonNode.Parse(resource)!).ToElement(_schema);
    }

    private static string Describe(ValidationResult result)
        => string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Code}:{i.Message}"));
}
