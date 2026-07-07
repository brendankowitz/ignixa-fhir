// <copyright file="BindingCheckTerminologySeverityTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Services;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests the three-outcome terminology severity semantics as surfaced through <see cref="BindingCheck"/>:
/// verified-in-valueset passes, verified-not-in-valueset errors for required bindings, and an unverifiable
/// external-system code degrades to a non-failing Warning (escalated only when the failure mode is Error).
/// </summary>
public class BindingCheckTerminologySeverityTests : IClassFixture<BindingCheckTerminologySeverityTests.Fixture>
{
    private readonly InMemoryTerminologyService _terminologyService;

    public BindingCheckTerminologySeverityTests(Fixture fixture)
    {
        _terminologyService = fixture.TerminologyService;
    }

    public sealed class Fixture
    {
        public InMemoryTerminologyService TerminologyService { get; } =
            new(new R4CoreSchemaProvider().ValueSetProvider);
    }

    private ValidationResult ValidateCoding(string system, string code, string strength, ValidationSettings settings)
    {
        var json = JsonNode.Parse($$"""
            {
                "resourceType":"Observation",
                "code":{
                    "coding":[{ "system":"{{system}}", "code":"{{code}}" }]
                }
            }
            """);
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new BindingCheck(
            "code",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            strength,
            _terminologyService);
        return check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, new ValidationState());
    }

    [Fact]
    public void GivenRequiredBindingWithVerifiedCode_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var settings = new ValidationSettings();

        // Act
        var result = ValidateCoding("http://hl7.org/fhir/administrative-gender", "male", "Required", settings);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenRequiredBindingWithCodeAbsentButSystemEnumerated_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var settings = new ValidationSettings();

        // Act
        var result = ValidateCoding("http://hl7.org/fhir/administrative-gender", "not-a-gender", "Required", settings);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenRequiredBindingWithUnverifiableExternalSystem_WhenFailureModeIsWarning_ThenReturnsNonFailingWarning()
    {
        // Arrange
        var settings = new ValidationSettings { TerminologyFailureMode = TerminologyFailureMode.Warning };

        // Act
        var result = ValidateCoding("http://snomed.info/sct", "703118005", "Required", settings);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void GivenRequiredBindingWithUnverifiableExternalSystem_WhenFailureModeIsError_ThenEscalatesToError()
    {
        // Arrange
        var settings = new ValidationSettings { TerminologyFailureMode = TerminologyFailureMode.Error };

        // Act
        var result = ValidateCoding("http://snomed.info/sct", "703118005", "Required", settings);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenCompatibilityDepthWithUnverifiableExternalSystem_WhenValidating_ThenEmitsNoError()
    {
        // Arrange - Compatibility keeps current leniency: no new errors for unverifiable codes.
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };

        // Act
        var result = ValidateCoding("http://snomed.info/sct", "703118005", "Required", settings);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Severity == IssueSeverity.Error);
    }
}
