// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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

namespace Ignixa.Validation.Tests;

/// <summary>
/// Tests for FHIRPath tree-context scoping: %resource / %rootResource seeding via ValidationState,
/// resolve() across contained and Bundle scopes, and the reference-integrity check.
/// </summary>
public class TreeContextScopingTests
{
    private readonly ISchema _schema = new R4CoreSchemaProvider();
    private readonly FhirPathParser _parser = new();

    private static IElement ToElement(string json)
    {
        var node = JsonNode.Parse(json);
        return JsonNodeSourceNode.Create(node!).ToElement(TestSchemaProvider.GetR4Schema());
    }

    private static IElement ToElement(JsonNode node)
        => JsonNodeSourceNode.Create(node).ToElement(TestSchemaProvider.GetR4Schema());

    [Fact]
    public void GivenResource_WhenEnterRootResource_ThenResourceEqualsRootResource()
    {
        // Arrange
        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""1"" }");

        // Act
        var state = new ValidationState().EnterRootResource(element);

        // Assert
        state.Scope.Resource.ShouldBeSameAs(element);
        state.Scope.RootResource.ShouldBeSameAs(element);
        state.Scope.Resolver.ShouldNotBeNull();
    }

    [Fact]
    public void GivenContained_WhenEnterContainedResource_ThenResourceIsContainedAndRootIsParent()
    {
        // Arrange
        var parent = ToElement(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs1"",
            ""contained"": [ { ""resourceType"": ""Patient"", ""id"": ""p1"" } ]
        }");
        var contained = parent.Children("contained")[0];

        // Act
        var state = new ValidationState()
            .EnterRootResource(parent)
            .EnterContainedResource(contained);

        // Assert
        state.Scope.Resource.ShouldBeSameAs(contained);
        state.Scope.RootResource.ShouldBeSameAs(parent);
    }

    [Fact]
    public void GivenResourceConstraintReferencingResourceVariable_WhenSeeded_ThenEvaluatesAgainstResource()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "res-id",
            Severity = ConstraintSeverity.Error,
            Human = "Resource must have an id",
            Expression = "%resource.id.exists()",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""abc"" }");
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenContainedConstraintReferencingResourceVariable_WhenScopedToContained_ThenEvaluatesAgainstContained()
    {
        // Arrange — a constraint asserting %resource is a Patient. When evaluated on the contained
        // resource with proper scoping, %resource must be the contained Patient, not the parent.
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "res-is-patient",
            Severity = ConstraintSeverity.Error,
            Human = "%resource must be a Patient",
            Expression = "%resource.id = 'p1'",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var parent = ToElement(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs1"",
            ""contained"": [ { ""resourceType"": ""Patient"", ""id"": ""p1"" } ]
        }");
        var contained = parent.Children("contained")[0];
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState()
            .EnterRootResource(parent)
            .EnterContainedResource(contained);

        // Act
        var result = check.Validate(contained, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenUnseededState_WhenEvaluatingConstraintWithoutResourceVariable_ThenEvaluatesContextFree()
    {
        // Arrange — no scope seeded. Evaluation must proceed exactly as before (context-free):
        // ordinary element-relative constraints still work, preserving existing-test behavior.
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "res-fallback",
            Severity = ConstraintSeverity.Error,
            Human = "Has an id",
            Expression = "id.exists()",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""1"" }");
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(element, settings, state);

        // Assert — unseeded evaluation must not throw and remains valid for element-relative checks
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenContainedReference_WhenConstraintUsesResolve_ThenResolvesContained()
    {
        // Arrange
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "resolve-contained",
            Severity = ConstraintSeverity.Error,
            Human = "generalPractitioner resolves to a Practitioner",
            Expression = "generalPractitioner.reference.resolve().is(Practitioner)",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ],
            ""generalPractitioner"": [ { ""reference"": ""#p1"" } ]
        }");
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenBundleEntryReference_WhenConstraintUsesResolve_ThenResolvesSiblingEntry()
    {
        // Arrange — a Bundle constraint resolving an intra-bundle reference by Type/id.
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "resolve-bundle",
            Severity = ConstraintSeverity.Error,
            Human = "Observation.subject resolves within bundle",
            Expression = "entry.resource.ofType(Observation).subject.reference.resolve().is(Patient)",
            Xpath = null,
            AppliesTo = new[] { "Bundle" }
        };

        var element = ToElement(@"{
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
                        ""code"": { ""text"": ""x"" },
                        ""subject"": { ""reference"": ""Patient/1"" }
                    }
                }
            ]
        }");
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenBareHashAtRootScope_WhenConstraintUsesResolve_ThenResolvesToEmpty()
    {
        // Arrange — measured against Firely 5.13.1/6.0.1 (ScopedNodeOnBaseTests asserts
        // Resolve("#") is null for a non-contained root): bare '#' only resolves to the container
        // from inside a contained resource's own scope, not at root scope, where the resource is
        // not contained in anything. This locks in that a root-level invariant calling resolve() on
        // '#' sees nothing, via the same RootResource/Resource seeding used by
        // FhirPathInvariantCheck and SlicingCheck.
        var constraint = new Ignixa.Specification.ConstraintDefinition
        {
            Key = "resolve-bare-hash",
            Severity = ConstraintSeverity.Error,
            Human = "'#' resolves to nothing at root scope",
            Expression = "'#'.resolve().empty()",
            Xpath = null,
            AppliesTo = new[] { "Patient" }
        };

        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""example"" }");
        var check = new FhirPathInvariantCheck(constraint, _schema, _parser);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenBundleWithDanglingLocalReference_WhenReferenceResolutionCheck_ThenReportsIssue()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Observation/2"",
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""text"": ""x"" },
                        ""subject"": { ""reference"": ""Patient/999"" }
                    }
                }
            ]
        }");
        var check = new ReferenceResolutionCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "ref-resolve");
    }

    [Fact]
    public void GivenExternalReference_WhenReferenceResolutionCheck_ThenDoesNotReportIssue()
    {
        // Arrange — absolute external URL must not be flagged even though it doesn't resolve locally.
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""text"": ""x"" },
                        ""subject"": { ""reference"": ""https://other.example.org/fhir/Patient/1"" }
                    }
                }
            ]
        }");
        var check = new ReferenceResolutionCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoResolverSeeded_WhenReferenceResolutionCheck_ThenDoesNotReportIssue()
    {
        // Arrange — without a seeded resolver the check is inert.
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""1"",
            ""generalPractitioner"": [ { ""reference"": ""#missing"" } ]
        }");
        var check = new ReferenceResolutionCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        var state = new ValidationState();

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
