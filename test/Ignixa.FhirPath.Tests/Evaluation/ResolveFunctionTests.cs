// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Unit tests for the <c>resolve()</c> function's in-instance resolution (GitHub issue #400):
/// contained resources and sibling Bundle entries must resolve without an external
/// <see cref="FhirEvaluationContext.ElementResolver"/>, and the in-instance result must take
/// precedence when a reference could also be resolved externally.
/// </summary>
public class ResolveFunctionTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private IElement ToElement(string json) =>
        ResourceJsonNode.Parse(json).ToElement(_r4Provider);

    private const string ObservationWithContainedPatientJson = @"{
        ""resourceType"": ""Observation"",
        ""id"": ""obs1"",
        ""status"": ""final"",
        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
        ""subject"": { ""reference"": ""#p1"" },
        ""contained"": [
            { ""resourceType"": ""Patient"", ""id"": ""p1"" }
        ]
    }";

    [Fact]
    public void GivenContainedPatientReferencedByFragment_WhenNoElementResolverIsSupplied_ThenResolveFindsIt()
    {
        // Arrange
        var expr = _parser.Parse("Observation.subject.where(resolve() is Patient).exists()");
        var observation = ToElement(ObservationWithContainedPatientJson);
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenContainedPatientReferencedByFragment_WhenElementResolverOnlyDoesExternalLookups_ThenResolveStillFindsIt()
    {
        // Arrange
        var expr = _parser.Parse("Observation.subject.where(resolve() is Patient).exists()");
        var observation = ToElement(ObservationWithContainedPatientJson);
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => null,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenBundleWithSiblingEntries_WhenResolvingByTypeAndIdWithNoExternalResolver_ThenFindsSibling()
    {
        // Arrange
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                },
                {
                    ""fullUrl"": ""http://example.org/fhir/Observation/2"",
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
                        ""subject"": { ""reference"": ""Patient/1"" }
                    }
                }
            ]
        }");
        var expr = _parser.Parse("Bundle.entry.resource.ofType(Observation).subject.resolve().id");
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var result = _evaluator.Evaluate(bundle, expr, context).Single();

        // Assert
        result.Value.ShouldBe("1");
    }

    [Fact]
    public void GivenBundleWithSiblingEntries_WhenResolvingByFullUrlWithNoExternalResolver_ThenFindsSibling()
    {
        // Arrange
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                },
                {
                    ""fullUrl"": ""http://example.org/fhir/Observation/2"",
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
                        ""subject"": { ""reference"": ""http://example.org/fhir/Patient/1"" }
                    }
                }
            ]
        }");
        var expr = _parser.Parse("Bundle.entry.resource.ofType(Observation).subject.resolve().id");
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var result = _evaluator.Evaluate(bundle, expr, context).Single();

        // Assert
        result.Value.ShouldBe("1");
    }

    [Fact]
    public void GivenContainingResource_WhenResolvingBareHash_ThenReturnsTheContainingResource()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.ShouldBeSameAs(observation);
    }

    [Fact]
    public void GivenReferenceResolvableBothInInstanceAndExternally_WhenResolving_ThenInInstanceResultWins()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var externalPatient = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""external-decoy"" }");
        var expr = _parser.Parse("Observation.subject.resolve().id");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => externalPatient,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe("p1");
    }

    [Fact]
    public void GivenReferenceNotPresentInTheInstance_WhenResolving_ThenFallsBackToElementResolver()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var externalPatient = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""99"" }");
        var expr = _parser.Parse("'Patient/99'.resolve().id");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = reference => reference == "Patient/99" ? externalPatient : null,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe("99");
    }

    [Fact]
    public void GivenNoRootAndNoElementResolver_WhenResolving_ThenReturnsEmptyWithoutThrowing()
    {
        // Arrange
        var input = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""example"" }");
        var expr = _parser.Parse("'Patient/1'.resolve()");
        var context = new EvaluationContext();

        // Act
        var result = _evaluator.Evaluate(input, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenElementResolverThatThrows_WhenResolving_ThenReturnsEmptyWithoutThrowing()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new InvalidOperationException("host resolver failure"),
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }
}
