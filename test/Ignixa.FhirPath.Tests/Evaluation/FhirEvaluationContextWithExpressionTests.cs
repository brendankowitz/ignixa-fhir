/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins that `FhirEvaluationContext.ElementResolver` survives the `with`-expression in
 * `TypedElementExtensions.Select`.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// <c>Select</c> holds its local <c>context</c> as the base-typed <see cref="EvaluationContext"/> and, when
/// <see cref="EvaluationContext.Resource"/> or <see cref="EvaluationContext.RootResource"/> is unbound, does
/// <c>context = context with { Resource = ..., RootResource = ... }</c>. That line only preserves
/// <see cref="FhirEvaluationContext"/>'s fields - most importantly <see cref="FhirEvaluationContext.ElementResolver"/>,
/// which <c>resolve()</c> depends on - because a record's <c>with</c> compiles to a virtual clone that dispatches on
/// the runtime type, not the static type of the local. If either <see cref="EvaluationContext"/> or
/// <see cref="FhirEvaluationContext"/> stopped being a <c>record</c>, this either fails to compile (base declared as
/// class) or silently slices to the base type's fields (derived declared as class, base still a record) - the
/// second case does not throw anywhere; <c>resolve()</c> just starts returning empty, because it gates on
/// <c>context is not FhirEvaluationContext fhirContext || fhirContext.ElementResolver == null</c>. A compile-time
/// guard cannot catch that second case, so this class exercises the runtime behaviour instead.
/// </summary>
public class FhirEvaluationContextWithExpressionTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string ObservationJson = """
    {
      "resourceType": "Observation",
      "id": "obs1",
      "status": "final",
      "code": { "text": "probe" },
      "subject": { "reference": "Patient/p1" }
    }
    """;

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "p1"
    }
    """;

    [Fact]
    public void GivenElementResolverBoundButResourceAndRootResourceUnbound_WhenSelectDefaultsTheContext_ThenResolveStillInvokesTheResolver()
    {
        // Arrange
        // Resource/RootResource are deliberately left null so Select is forced to take the `with` branch -
        // pre-populating them would skip the branch this test exists to pin.
        var observation = Parse(ObservationJson);
        var patient = Parse(PatientJson);
        var resolvedReferences = new List<string>();
        var context = new FhirEvaluationContext
        {
            ElementResolver = reference =>
            {
                resolvedReferences.Add(reference);
                return reference == "Patient/p1" ? patient : null;
            }
        };

        // Act
        var result = observation.Select("subject.resolve()", context).ToList();

        // Assert
        resolvedReferences.ShouldBe(["Patient/p1"]);
        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(patient);
    }

    [Fact]
    public void GivenAnEvaluationContextTypedVariableHoldingAFhirEvaluationContext_WhenWithExpressionRuns_ThenTheClonedInstanceStaysFhirEvaluationContextAndKeepsTheResolver()
    {
        // Arrange
        // The static type of the local must be the base EvaluationContext - exactly like Select's local - for
        // this to say anything about the bug this test pins. Asserting the same thing on a FhirEvaluationContext-
        // typed local would pass unconditionally and prove nothing.
        Func<string, IElement?> resolver = static _ => null;
        EvaluationContext original = new FhirEvaluationContext { ElementResolver = resolver };

        // Act
        var cloned = original with { Resource = CreateElement("x") };

        // Assert
        cloned.ShouldBeOfType<FhirEvaluationContext>();
        ((FhirEvaluationContext)cloned).ElementResolver.ShouldBeSameAs(resolver);
    }

    [Fact]
    public void GivenNoResourceOrRootResourceBound_WhenSelectDefaultsThemViaWith_ThenBothResolveToTheInputElement()
    {
        // Arrange
        var observation = Parse(ObservationJson);
        var context = new FhirEvaluationContext();

        // Act
        var resourceResult = observation.Select("%resource", context).ToList();
        var rootResourceResult = observation.Select("%rootResource", context).ToList();

        // Assert
        resourceResult.ShouldHaveSingleItem();
        resourceResult[0].ShouldBeSameAs(observation);
        rootResourceResult.ShouldHaveSingleItem();
        rootResourceResult[0].ShouldBeSameAs(observation);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private static IElement CreateElement(string value) => new TestElement(value);

    private sealed class TestElement(string value) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "string";
        public object Value { get; } = value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
