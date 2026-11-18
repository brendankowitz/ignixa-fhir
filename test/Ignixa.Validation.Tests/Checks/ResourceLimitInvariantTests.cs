// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// An invariant stopped at an engine limit fails the instance, unlike one the engine refuses as ill-formed,
/// which only warns: the data can trigger a limit, so excusing it would let a crafted instance bypass the
/// invariant.
/// </summary>
public class ResourceLimitInvariantTests
{
    /// <summary>
    /// The lookahead forces the backtracking engine, and <c>(a+)+</c> inside it is exponential in a run of
    /// <c>a</c> that is not followed by <c>b</c> - so a long enough family name exhausts the match timeout.
    /// </summary>
    private const string Expression = "name.family.all($this.matches('(?=(a+)+b)').not())";

    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenDataThatMakesTheInvariantsRegexTimeOut_WhenValidating_ThenTheInstanceFails()
    {
        // Arrange
        var patient = Patient(new string('a', 40));
        using var scope = new FhirPathRegex.TimeoutScope(TimeSpan.FromMilliseconds(50));

        // Act
        var result = Validate(patient);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Code == "test-limit-1" && issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenOrdinaryData_WhenValidatingTheSameInvariant_ThenItPasses()
    {
        // Arrange — control: the invariant itself is satisfiable, so the failure above is the timeout's.
        var patient = Patient("Smith");

        // Act
        var result = Validate(patient);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    private ValidationResult Validate(IElement patient)
    {
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "test-limit-1",
            Severity = ConstraintSeverity.Error,
            Human = "Family names must not match the pattern",
            Expression = Expression,
            AppliesTo = ["Patient"],
        };

        return new FhirPathInvariantCheck(constraint, _schema, _parser)
            .Validate(patient, new ValidationSettings { Depth = ValidationDepth.Spec }, ValidationState.ForRoot(patient));
    }

    private IElement Patient(string family)
    {
        var json = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = "limit-example",
            ["name"] = new JsonArray(new JsonObject { ["family"] = family }),
        };

        return JsonNodeSourceNode.Create(json).ToElement(_schema);
    }
}
