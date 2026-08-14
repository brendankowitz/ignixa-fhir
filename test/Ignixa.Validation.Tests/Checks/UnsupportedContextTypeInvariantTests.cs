// <copyright file="UnsupportedContextTypeInvariantTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

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
/// The second shape of "the engine refused to evaluate this constraint", alongside
/// <see cref="RepeatingOperandInvariantTests"/>: a function applied to a context type it is not defined
/// for, using the shipped R4B invariants that actually do it.
/// </summary>
/// <remarks>
/// <para>
/// R4B ships <c>sdf-24</c> and <c>sdf-25</c> on <c>StructureDefinition.snapshot</c>. Both compute
/// <c>id.substring(0, $this.length() - n)</c>, where <c>$this</c> inside
/// <c>element.where(…)</c> is an <c>ElementDefinition</c>, not the <c>id</c> string the arithmetic
/// clearly intends. <c>length()</c> on a complex type has no defined answer, so the engine signals an
/// error - correctly, and about the constraint, not about the instance.
/// </para>
/// <para>
/// The guard clauses ahead of it (<c>type.code='Reference'</c> and friends) keep most snapshots away
/// from that call entirely, which is why the resource here is built to satisfy them: a
/// <c>Reference</c>-typed element whose id ends in <c>.reference</c> and which carries a
/// <c>targetProfile</c>. Anything less and the <c>and</c> chain short-circuits and the test proves
/// nothing.
/// </para>
/// <para>
/// The constraints are read out of <see cref="R4BCoreSchemaProvider"/> rather than pasted here, so these
/// tests track whatever the spec actually ships.
/// </para>
/// </remarks>
public class UnsupportedContextTypeInvariantTests
{
    private readonly ISchema _schema = TestSchemaProvider.GetR4BSchema();
    private readonly FhirPathParser _parser = new();

    [Theory]
    [InlineData("sdf-24")]
    [InlineData("sdf-25")]
    public void GivenShippedR4BSnapshotConstraint_WhenInspectingTheExpression_ThenItStillCallsLengthOnThis(string key)
    {
        // Arrange & Act
        var constraint = ShippedSnapshotConstraint(key);

        // Assert — the premise of every other test in this file.
        constraint.Expression.ShouldContain("$this.length()");
        constraint.Severity.ShouldBe("error", StringCompareShould.IgnoreCase);
    }

    [Theory]
    [InlineData("sdf-24")]
    [InlineData("sdf-25")]
    public void GivenASnapshotThatReachesLengthOnAnElementDefinition_WhenValidating_ThenResourceStaysValidWithAWarning(string key)
    {
        // Arrange
        var snapshot = SnapshotOf(ReferenceAndConceptSnapshotJson);

        // Act
        var result = Validate(key, snapshot);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldNotBeEmpty();
        result.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Warning);
        result.Issues.ShouldContain(i => i.Code == key && i.Message.Contains("could not be evaluated"));
    }

    /// <summary>
    /// Negative control. Without it the tests above would pass just as happily if the constraint always
    /// warned; a snapshot whose elements fail the guard clauses never reaches <c>$this.length()</c>, so
    /// the constraint really is evaluated and must come back clean.
    /// </summary>
    [Theory]
    [InlineData("sdf-24")]
    [InlineData("sdf-25")]
    public void GivenASnapshotThatShortCircuitsBeforeLength_WhenValidating_ThenTheConstraintEvaluatesCleanly(string key)
    {
        // Arrange
        var snapshot = SnapshotOf(PlainSnapshotJson);

        // Act
        var result = Validate(key, snapshot);

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldBeEmpty();
    }

    private ValidationResult Validate(string key, IElement snapshot)
    {
        var shipped = ShippedSnapshotConstraint(key);
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = shipped.Key,
            Severity = string.Equals(shipped.Severity, "warning", StringComparison.OrdinalIgnoreCase)
                ? ConstraintSeverity.Warning
                : ConstraintSeverity.Error,
            Human = shipped.Human ?? string.Empty,
            Expression = shipped.Expression,
            Xpath = shipped.Xpath,
            AppliesTo = Array.Empty<string>()
        };

        return new FhirPathInvariantCheck(constraint, _schema, _parser)
            .Validate(snapshot, new ValidationSettings { Depth = ValidationDepth.Spec }, new ValidationState());
    }

    private IConstraint ShippedSnapshotConstraint(string key)
    {
        var structureDefinition = _schema.GetTypeDefinition("StructureDefinition").ShouldNotBeNull();
        var snapshot = structureDefinition.Children
            .FirstOrDefault(c => string.Equals(c.Info.Name, "snapshot", StringComparison.Ordinal))
            .ShouldNotBeNull();

        return ((ITypeExtended)snapshot).Constraints
            .FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal))
            .ShouldNotBeNull();
    }

    private IElement SnapshotOf(string structureDefinitionJson)
    {
        var resource = ResourceJsonNode.Parse(structureDefinitionJson).ToElement(_schema);
        return resource.Children("snapshot")[0];
    }

    /// <summary>
    /// Satisfies both constraints' guard clauses: a <c>Reference</c> element ending in <c>.reference</c>
    /// with a <c>targetProfile</c> (sdf-24) and a <c>CodeableConcept</c> element ending in
    /// <c>.concept</c> with a <c>binding</c> (sdf-25).
    /// </summary>
    private const string ReferenceAndConceptSnapshotJson = """
    {
        "resourceType": "StructureDefinition",
        "id": "codeable-reference-profile",
        "url": "http://example.org/StructureDefinition/codeable-reference-profile",
        "name": "CodeableReferenceProfile",
        "status": "active",
        "kind": "resource",
        "abstract": false,
        "type": "Observation",
        "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Observation",
        "derivation": "constraint",
        "snapshot": {
            "element": [
                {
                    "id": "Observation",
                    "path": "Observation"
                },
                {
                    "id": "Observation.subject.reference",
                    "path": "Observation.subject.reference",
                    "type": [
                        {
                            "code": "Reference",
                            "targetProfile": ["http://hl7.org/fhir/StructureDefinition/Patient"]
                        }
                    ]
                },
                {
                    "id": "Observation.code.concept",
                    "path": "Observation.code.concept",
                    "type": [ { "code": "CodeableConcept" } ],
                    "binding": {
                        "strength": "required",
                        "valueSet": "http://hl7.org/fhir/ValueSet/observation-codes"
                    }
                }
            ]
        }
    }
    """;

    /// <summary>
    /// The same shape with none of the guard clauses satisfied, so evaluation never reaches
    /// <c>$this.length()</c>.
    /// </summary>
    private const string PlainSnapshotJson = """
    {
        "resourceType": "StructureDefinition",
        "id": "plain-profile",
        "url": "http://example.org/StructureDefinition/plain-profile",
        "name": "PlainProfile",
        "status": "active",
        "kind": "resource",
        "abstract": false,
        "type": "Observation",
        "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Observation",
        "derivation": "constraint",
        "snapshot": {
            "element": [
                {
                    "id": "Observation",
                    "path": "Observation"
                },
                {
                    "id": "Observation.status",
                    "path": "Observation.status",
                    "type": [ { "code": "code" } ]
                }
            ]
        }
    }
    """;

    private static string Describe(ValidationResult result)
        => string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Code}:{i.Message}"));
}
