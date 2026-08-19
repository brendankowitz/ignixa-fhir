// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Pins that a <see cref="ValidationState"/> always carries a resource root, and that the schema entry
/// point supplies one when the caller does not.
/// </summary>
/// <remarks>
/// <para>
/// An unrooted state leaves <c>%resource</c> empty. <c>FhirPathInvariantCheck</c> reads an empty result
/// as a failed constraint, so a conformant resource is rejected for a defect in the caller — and only at
/// <see cref="ValidationDepth.Full"/>, where invariants run at all. That is the bug this type's shape now
/// prevents, and it is worth restating that shipping it once was possible because seeding was a step a
/// caller had to remember rather than something the type required.
/// </para>
/// <para>
/// The two tests below cover the two halves of the guarantee: the first is behavioural — the entry point
/// really does bind <c>%resource</c> — and the second pins the construction rule that makes the first
/// impossible to regress by omission. Without the second, someone could reintroduce a parameterless
/// constructor and every existing test would keep passing, because none of them would use it.
/// </para>
/// </remarks>
public class ValidationStateRootingTests
{
    private readonly ISchema _schema = new R4CoreSchemaProvider();

    [Fact]
    public void GivenAnInvariantReferencingResource_WhenValidatingAtFullWithNoStateSupplied_ThenResourceIsBound()
    {
        // Arrange — a constraint that holds for this Patient, but only if %resource resolves. Unbound,
        // the comparison yields empty and the check reports a failure the resource did not earn.
        var element = ToElement("""{ "resourceType": "Patient", "id": "abc" }""");
        var schema = new ValidationSchema(
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "Patient",
            universalChecks: [],
            specChecks: [],
            profileChecks: [ResourceIdInvariant()]);

        // Act — state omitted, which is how every production caller invokes this.
        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full });

        // Assert
        result.IsValid.ShouldBeTrue(Describe(result));
        result.Issues.ShouldBeEmpty(Describe(result));
    }

    [Fact]
    public void GivenAnInvariantContradictingResource_WhenValidatingAtFullWithNoStateSupplied_ThenTheConstraintFails()
    {
        // Arrange - the negative twin of the test above. If %resource actually resolves to the bound
        // element (rather than an unrooted, empty %resource), this constraint MUST fail because "abc" !=
        // "not-abc". FhirPathInvariantCheck returns Success() on parse failure, an unsupported engine
        // feature, and an AppliesTo miss - none of which apply here - so a failure here is the only signal
        // that distinguishes "the invariant ran and %resource was bound" from "the invariant never ran".
        var element = ToElement("""{ "resourceType": "Patient", "id": "abc" }""");
        var schema = new ValidationSchema(
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "Patient",
            universalChecks: [],
            specChecks: [],
            profileChecks: [ResourceIdInvariant(expectedId: "not-abc")]);

        // Act - state omitted, same as every production caller.
        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full });

        // Assert
        result.IsValid.ShouldBeFalse(Describe(result));
        result.Issues.Count.ShouldBe(1, Describe(result));
        result.Issues[0].Code.ShouldBe("test-resource-bound-1");
    }

    [Fact]
    public void GivenTheValidationStateType_WhenInspectingItsPublicConstructors_ThenThereAreNone()
    {
        // ForRoot is the only public entry point, and it requires the root. A public constructor of any
        // arity would reopen the "state exists but has no resource" hole; a parameterless one would make
        // forgetting silent again.
        typeof(ValidationState)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(nameof(ResourceScope.Resource))]
    [InlineData(nameof(ResourceScope.RootResource))]
    public void GivenResourceScope_WhenInspectingItsMembers_ThenBothAreRequired(string memberName)
    {
        // "Unrooted state is unrepresentable" rests equally on ResourceScope.Resource/RootResource being
        // required and non-nullable (ResourceScope.cs:57,64), not only on ValidationState's constructor
        // shape above. If either member lost `required` (or went nullable), the compiler would stop
        // enforcing the guarantee at every call site, and ForRoot/EnterContainedResource would keep
        // compiling with a scope that could carry a null resource.
        PropertyInfo property = typeof(ResourceScope).GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Expected public property '{memberName}' on {nameof(ResourceScope)}.");

        property.GetCustomAttribute<RequiredMemberAttribute>().ShouldNotBeNull(
            $"{memberName} must be marked `required` so a {nameof(ResourceScope)} cannot be built without it.");

        var nullabilityContext = new NullabilityInfoContext();
        NullabilityInfo nullability = nullabilityContext.Create(property);
        nullability.ReadState.ShouldBe(NullabilityState.NotNull, $"{memberName} must be non-nullable.");
    }

    private FhirPathInvariantCheck ResourceIdInvariant(string expectedId = "abc") => new(
        new Ignixa.Specification.ConstraintDefinition
        {
            Key = "test-resource-bound-1",
            Severity = ConstraintSeverity.Error,
            Human = "%resource must be bound to the resource under validation",
            Expression = $"%resource.id = '{expectedId}'",
            Xpath = null,
            AppliesTo = ["Patient"]
        },
        _schema,
        new FhirPathParser());

    private IElement ToElement(string json)
        => JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(_schema);

    private static string Describe(ValidationResult result)
        => result.Issues.Count == 0
            ? "(no issues)"
            : string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Code}@{i.Path}"));
}
