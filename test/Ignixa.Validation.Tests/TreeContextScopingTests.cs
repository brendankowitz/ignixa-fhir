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
using Ignixa.Validation.Schema;
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
    private readonly IValidationSchemaResolver _schemaResolver =
        new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(new R4CoreSchemaProvider()));

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
    public void GivenDocumentBundleWithDanglingRelativeReference_WhenReferenceResolutionCheck_ThenReportsIssue()
    {
        // Arrange — a document bundle requires intra-bundle reference integrity, so a Type/id
        // reference that points outside the bundle is genuinely unresolved.
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""document"",
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

    [Theory]
    [InlineData("searchset")]
    [InlineData("transaction")]
    [InlineData("collection")]
    public void GivenNonDocumentBundleWithUnresolvedRelativeReference_WhenReferenceResolutionCheck_ThenDoesNotReportIssue(string bundleType)
    {
        // Arrange — searchset/transaction/collection bundles legitimately reference server-resident
        // resources not present in the bundle. A Type/id reference that is absent must NOT be flagged.
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": """ + bundleType + @""",
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
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenDocumentBundleWithResolvableRelativeReference_WhenReferenceResolutionCheck_ThenDoesNotReportIssue()
    {
        // Arrange — the referenced Patient/1 is present as a bundle entry, so it resolves.
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""document"",
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
    public void GivenDocumentBundleEntryWithOwnFragmentReference_WhenReferenceResolutionCheck_ThenDoesNotReportIssue()
    {
        // Arrange — a #fragment reference inside an entry resource resolves against THAT entry's own
        // contained set, which the bundle-root resolver does not index. The root check must not flag
        // it (it is checked under the entry's own scope, not here).
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""document"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/MedicationRequest/2"",
                    ""resource"": {
                        ""resourceType"": ""MedicationRequest"",
                        ""id"": ""2"",
                        ""status"": ""active"",
                        ""intent"": ""order"",
                        ""subject"": { ""reference"": ""Patient/1"" },
                        ""contained"": [ { ""resourceType"": ""Medication"", ""id"": ""med1"" } ],
                        ""medicationReference"": { ""reference"": ""#med1"" }
                    }
                },
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                }
            ]
        }");
        var check = new ReferenceResolutionCheck();
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert — the entry-local #med1 must not be flagged by the bundle-root walk.
        result.Issues.ShouldNotContain(i => i.Code == "ref-resolve" && i.Path.Contains("med1", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenResourceWithDanglingFragmentReference_WhenReferenceResolutionCheck_ThenReportsIssue()
    {
        // Arrange — a seeded resolver with no matching contained resource: the #missing fragment is
        // genuinely unresolved and must be flagged (non-Bundle local-reference path).
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""1"",
            ""generalPractitioner"": [ { ""reference"": ""#missing"" } ]
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
    public void GivenResourceWithResolvableFragmentReference_WhenReferenceResolutionCheck_ThenDoesNotReportIssue()
    {
        // Arrange — the #p1 fragment resolves to a contained resource.
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""1"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ],
            ""generalPractitioner"": [ { ""reference"": ""#p1"" } ]
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

    [Fact]
    public void GivenDocumentBundleWithUnknownTypeToken_WhenCheckHasResourceTypeRegistry_ThenDoesNotReportIssue()
    {
        // Arrange — "MyCustomType/1" is shaped like a relative reference but is not a real resource
        // type. With a resource-type registry the token is rejected, so it is never treated as a
        // bundle-relative reference and not flagged as unresolved.
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""document"",
            ""entry"": [
                {
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""text"": ""x"" },
                        ""subject"": { ""reference"": ""MyCustomType/1"" }
                    }
                }
            ]
        }");
        var registry = new HashSet<string>(StringComparer.Ordinal) { "Patient", "Observation", "Bundle" };
        var check = new ReferenceResolutionCheck(registry);
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        var state = new ValidationState().EnterRootResource(element);

        // Act
        var result = check.Validate(element, settings, state);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "ref-resolve");
    }

    [Fact]
    public void GivenDanglingReferenceInsideContained_WhenValidatingAtFullDepth_ThenReportsRefResolveExactlyOnce()
    {
        // Arrange — a dangling fragment reference lives INSIDE a contained resource. The full
        // pipeline runs the root resource's ReferenceResolutionCheck (which must not descend into
        // the contained resource's scope) and ContainedResourceCheck's re-validation of the
        // contained resource (which owns and checks it). The issue must be reported exactly once.
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs1"",
            ""status"": ""final"",
            ""code"": { ""text"": ""x"" },
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""p1"",
                    ""managingOrganization"": { ""reference"": ""#nope"" }
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var schema = _schemaResolver.GetSchema("Observation").ShouldNotBeNull();
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };

        // Act — note: no explicit EnterRootResource. ValidationSchema.Validate auto-seeds the root
        // scope, so the reference-integrity check runs without callers having to remember to seed.
        var result = schema.Validate(element, settings);

        // Assert
        result.Issues.Count(i => i.Code == "ref-resolve").ShouldBe(1);
    }

    [Fact]
    public void GivenUnseededState_WhenValidatingResourceThroughSchema_ThenScopeIsAutoSeeded()
    {
        // Arrange — a dangling #fragment in a standalone resource. The caller does NOT seed the
        // scope; ValidationSchema.Validate must seed it so the reference-integrity check fires.
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""1"",
            ""managingOrganization"": { ""reference"": ""#missing"" }
        }");
        var element = JsonNodeSourceNode.Create(json!).ToElement(TestSchemaProvider.GetR4Schema());
        var schema = _schemaResolver.GetSchema("Patient").ShouldNotBeNull();
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };

        // Act — unseeded state passed in; auto-seeding inside Validate enables the check.
        var result = schema.Validate(element, settings, new ValidationState());

        // Assert
        result.Issues.ShouldContain(i => i.Code == "ref-resolve");
    }
}
