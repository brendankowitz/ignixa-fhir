// <copyright file="RootDeclaredInvariantTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Constraints declared on a type's ROOT <c>ElementDefinition</c> row - dom-* on every DomainResource,
/// bdl-* on Bundle, qty-3 on Quantity, rng-2 on Range, rat-1 on Ratio.
/// </summary>
/// <remarks>
/// <para>
/// The core schema generator emitted a literal <c>constraints: null</c> for every type node and only
/// ever read child rows, so this entire family was absent from the shipped schema in all five FHIR
/// versions - not skipped by the validator, simply never present for it to evaluate. R4 went from 99
/// of 237 declared constraint keys to all 237.
/// </para>
/// <para>
/// Each case below pairs a violating instance with a conformant control. The control is what
/// distinguishes "the constraint is enforced" from "the constraint always fails", which matters more
/// than usual here: these expressions had never been executed by this engine before.
/// </para>
/// </remarks>
public class RootDeclaredInvariantTests
{
    private readonly ISchema _schema = TestSchemaProvider.GetR4Schema();
    private readonly IValidationSchemaResolver _resolver;

    public RootDeclaredInvariantTests()
    {
        _resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(_schema));
    }

    /// <summary>
    /// dom-2: <c>contained.contained.empty()</c> - a contained resource may not itself contain resources.
    /// </summary>
    [Fact]
    public void GivenAContainedResourceThatItselfContains_WhenValidatingAtFull_ThenDom2Fails()
    {
        // Arrange
        var patient = """
        {
            "resourceType": "Patient", "id": "outer",
            "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>" },
            "contained": [{
                "resourceType": "Organization", "id": "inner", "name": "Acme",
                "contained": [{ "resourceType": "Organization", "id": "innermost", "name": "Nested" }]
            }]
        }
        """;

        // Act
        var result = Validate(patient, "Patient");

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(i => i.Code == "dom-2" && i.Severity == IssueSeverity.Error, Describe(result));
    }

    [Fact]
    public void GivenASingleLevelContainedResource_WhenValidatingAtFull_ThenDom2DoesNotFire()
    {
        // Arrange
        var patient = """
        {
            "resourceType": "Patient", "id": "outer",
            "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>" },
            "contained": [{ "resourceType": "Organization", "id": "inner", "name": "Acme" }]
        }
        """;

        // Act
        var result = Validate(patient, "Patient");

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "dom-2", Describe(result));
    }

    /// <summary>
    /// dom-6 is a best-practice warning ("a resource should have narrative"). It must warn without
    /// invalidating - the distinction the whole severity mapping rests on.
    /// </summary>
    [Fact]
    public void GivenAResourceWithoutNarrative_WhenValidatingAtFull_ThenDom6WarnsWithoutFailing()
    {
        // Arrange
        var patient = """{ "resourceType": "Patient", "id": "bare", "active": true }""";

        // Act
        var result = Validate(patient, "Patient");

        // Assert
        result.Issues.ShouldContain(i => i.Code == "dom-6" && i.Severity == IssueSeverity.Warning, Describe(result));
        result.Issues.ShouldNotContain(i => i.Code == "dom-6" && i.Severity == IssueSeverity.Error, Describe(result));
    }

    /// <summary>
    /// bdl-1: <c>total.empty() or (type = 'searchset') or (type = 'history')</c>.
    /// </summary>
    [Fact]
    public void GivenATransactionBundleWithTotal_WhenValidatingAtFull_ThenBdl1Fails()
    {
        // Arrange
        var bundle = """{ "resourceType": "Bundle", "id": "b", "type": "transaction", "total": 3 }""";

        // Act
        var result = Validate(bundle, "Bundle");

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(i => i.Code == "bdl-1" && i.Severity == IssueSeverity.Error, Describe(result));
    }

    [Fact]
    public void GivenASearchsetBundleWithTotal_WhenValidatingAtFull_ThenBdl1DoesNotFire()
    {
        // Arrange
        var bundle = """{ "resourceType": "Bundle", "id": "b", "type": "searchset", "total": 3 }""";

        // Act
        var result = Validate(bundle, "Bundle");

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "bdl-1", Describe(result));
    }

    /// <summary>
    /// rng-2: <c>low.empty() or high.empty() or (low &lt;= high)</c>, on the Range datatype. Reached
    /// through <c>Observation.value[x]</c>, so this also exercises the polymorphic descent against a
    /// constraint that only exists at all because of the generator fix.
    /// </summary>
    [Fact]
    public void GivenARangeWithLowAboveHigh_WhenValidatingAtFull_ThenRng2Fails()
    {
        // Arrange
        var observation = ObservationWithValueRange("""
            "low": { "value": 90, "unit": "mg" }, "high": { "value": 10, "unit": "mg" }
        """);

        // Act
        var result = Validate(observation, "Observation");

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(i => i.Code == "rng-2" && i.Severity == IssueSeverity.Error, Describe(result));
    }

    [Fact]
    public void GivenARangeWithLowBelowHigh_WhenValidatingAtFull_ThenRng2DoesNotFire()
    {
        // Arrange
        var observation = ObservationWithValueRange("""
            "low": { "value": 10, "unit": "mg" }, "high": { "value": 90, "unit": "mg" }
        """);

        // Act
        var result = Validate(observation, "Observation");

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "rng-2", Describe(result));
    }

    /// <summary>
    /// qty-3: <c>code.empty() or system.exists()</c> - a coded unit needs the system that defines it.
    /// </summary>
    [Fact]
    public void GivenAQuantityWithCodeButNoSystem_WhenValidatingAtFull_ThenQty3Fails()
    {
        // Arrange
        var observation = ObservationWithValueQuantity("""
            "value": 5, "unit": "mg", "code": "mg"
        """);

        // Act
        var result = Validate(observation, "Observation");

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.ShouldContain(i => i.Code == "qty-3" && i.Severity == IssueSeverity.Error, Describe(result));
    }

    [Fact]
    public void GivenAQuantityWithCodeAndSystem_WhenValidatingAtFull_ThenQty3DoesNotFire()
    {
        // Arrange
        var observation = ObservationWithValueQuantity("""
            "value": 5, "unit": "mg", "code": "mg", "system": "http://unitsofmeasure.org"
        """);

        // Act
        var result = Validate(observation, "Observation");

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "qty-3", Describe(result));
    }

    private static string ObservationWithValueRange(string rangeBody) => $$"""
        {
            "resourceType": "Observation", "id": "obs",
            "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>" },
            "status": "final",
            "code": { "text": "test" },
            "valueRange": { {{rangeBody}} }
        }
        """;

    private static string ObservationWithValueQuantity(string quantityBody) => $$"""
        {
            "resourceType": "Observation", "id": "obs",
            "text": { "status": "generated", "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>" },
            "status": "final",
            "code": { "text": "test" },
            "valueQuantity": { {{quantityBody}} }
        }
        """;

    private ValidationResult Validate(string resourceJson, string resourceType)
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(resourceJson)!);
        var schema = _resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}")
            ?? throw new InvalidOperationException($"No schema for {resourceType}");

        var element = sourceNode.ToElement(_schema);
        return schema.Validate(
            element,
            new ValidationSettings { Depth = ValidationDepth.Full },
            new ValidationState().EnterRootResource(element));
    }

    private static string Describe(ValidationResult result)
        => result.Issues.Count == 0
            ? "(no issues)"
            : string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Code}@{i.Path}"));
}
