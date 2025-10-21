// <copyright file="ResourceReferenceHelperTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.Helpers;
using Ignixa.SourceNodeSerialization.Models;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Xunit;

namespace Ignixa.SourceNodeSerialization.Tests;

public class ResourceReferenceHelperTests
{
    private readonly TestReferenceMetadataProvider _metadataProvider;

    public ResourceReferenceHelperTests()
    {
        _metadataProvider = new TestReferenceMetadataProvider();
    }

    #region GetReferences Tests

    [Fact]
    public void GetReferences_WithNoReferences_ReturnsEmptyList()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "Patient", _metadataProvider);

        // Assert
        Assert.Empty(references);
    }

    [Fact]
    public void GetReferences_WithSingleReference_ReturnsSingleReference()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""Organization/org-456""}");

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "Patient", _metadataProvider);

        // Assert
        var reference = Assert.Single(references);
        Assert.Equal("managingOrganization", reference.ElementPath);
        Assert.Equal("Organization/org-456", reference.Value);
        Assert.Equal(ReferenceType.Relative, reference.Type);
        Assert.Equal("Organization", reference.ResourceType);
        Assert.Equal("org-456", reference.ResourceId);
        Assert.False(reference.IsCollection);
        Assert.Contains("Organization", reference.TargetResourceTypes);
    }

    [Fact]
    public void GetReferences_WithMultipleReferences_ReturnsAllReferences()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = "obs-123",
        };
        resource.MutableNode["subject"] = JsonNode.Parse(@"{""reference"": ""Patient/pat-123""}");
        resource.MutableNode["performer"] = JsonNode.Parse(@"[
                    {""reference"": ""Practitioner/prac-1""},
                    {""reference"": ""Practitioner/prac-2""}
                ]");

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "Observation", _metadataProvider);

        // Assert
        Assert.Equal(3, references.Count);

        var subjectRef = references[0];
        Assert.Equal("subject", subjectRef.ElementPath);
        Assert.Equal("Patient/pat-123", subjectRef.Value);

        var performer1 = references[1];
        Assert.Equal("performer", performer1.ElementPath);
        Assert.Equal("Practitioner/prac-1", performer1.Value);
        Assert.True(performer1.IsCollection);

        var performer2 = references[2];
        Assert.Equal("performer", performer2.ElementPath);
        Assert.Equal("Practitioner/prac-2", performer2.Value);
    }

    [Fact]
    public void GetReferences_WithAbsoluteUrl_ParsesCorrectly()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""https://example.org/fhir/Organization/org-456""}");

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "Patient", _metadataProvider);

        // Assert
        var reference = Assert.Single(references);
        Assert.Equal("https://example.org/fhir/Organization/org-456", reference.Value);
        Assert.Equal(ReferenceType.Absolute, reference.Type);
        Assert.Equal("Organization", reference.ResourceType);
        Assert.Equal("org-456", reference.ResourceId);
    }

    [Fact]
    public void GetReferences_WithLogicalReference_ParsesCorrectly()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""urn:uuid:12345678-1234-1234-1234-123456789012""}");

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "Patient", _metadataProvider);

        // Assert
        var reference = Assert.Single(references);
        Assert.Equal("urn:uuid:12345678-1234-1234-1234-123456789012", reference.Value);
        Assert.Equal(ReferenceType.Logical, reference.Type);
        Assert.Null(reference.ResourceType);
        Assert.Null(reference.ResourceId);
    }

    [Fact]
    public void GetReferences_WithUnknownResourceType_ReturnsEmpty()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "UnknownResource",
            Id = "test-123",
        };

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "UnknownResource", _metadataProvider);

        // Assert
        Assert.Empty(references);
    }

    [Fact]
    public void GetReferences_WithNonReferenceField_IgnoresField()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["name"] = JsonNode.Parse(@"[{""family"": ""Doe""}]");
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""Organization/org-456""}");

        // Act
        var references = ResourceReferenceHelper.GetReferences(resource, "Patient", _metadataProvider);

        // Assert
        var reference = Assert.Single(references);
        Assert.Equal("managingOrganization", reference.ElementPath);
    }

    #endregion

    #region UpdateReference Tests

    [Fact]
    public void UpdateReference_WithSingleReference_UpdatesSuccessfully()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""Organization/org-old""}");

        // Act
        bool result = ResourceReferenceHelper.UpdateReference(resource, "managingOrganization", "Organization/org-new");

        // Assert
        Assert.True(result);
        var updatedElement = resource.MutableNode["managingOrganization"];
        Assert.Equal("Organization/org-new", updatedElement["reference"].GetValue<string>());
    }

    [Fact]
    public void UpdateReference_WithArrayReference_UpdatesSpecificIndex()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = "obs-123",
        };
        resource.MutableNode["performer"] = JsonNode.Parse(@"[
                    {""reference"": ""Practitioner/prac-1""},
                    {""reference"": ""Practitioner/prac-2""},
                    {""reference"": ""Practitioner/prac-3""}
                ]");

        // Act
        bool result = ResourceReferenceHelper.UpdateReference(resource, "performer", "Practitioner/prac-updated", arrayIndex: 1);

        // Assert
        Assert.True(result);
        var array = resource.MutableNode["performer"] as JsonArray;
        Assert.NotNull(array);
        Assert.Equal(3, array.Count);
        Assert.Equal("Practitioner/prac-1", array[0]["reference"].GetValue<string>());
        Assert.Equal("Practitioner/prac-updated", array[1]["reference"].GetValue<string>());
        Assert.Equal("Practitioner/prac-3", array[2]["reference"].GetValue<string>());
    }

    [Fact]
    public void UpdateReference_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };

        // Act
        bool result = ResourceReferenceHelper.UpdateReference(resource, "nonExistentField", "Organization/org-new");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UpdateReference_WithInvalidArrayIndex_ReturnsFalse()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = "obs-123",
        };
        resource.MutableNode["performer"] = JsonNode.Parse(@"[
                    {""reference"": ""Practitioner/prac-1""}
                ]");

        // Act - try to update index 5 when only 1 element exists
        bool result = ResourceReferenceHelper.UpdateReference(resource, "performer", "Practitioner/prac-new", arrayIndex: 5);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UpdateReference_WithNegativeArrayIndex_ReturnsFalse()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = "obs-123",
        };
        resource.MutableNode["performer"] = JsonNode.Parse(@"[
                    {""reference"": ""Practitioner/prac-1""}
                ]");

        // Act
        bool result = ResourceReferenceHelper.UpdateReference(resource, "performer", "Practitioner/prac-new", arrayIndex: -1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UpdateReference_WithArrayIndexOnNonArray_ReturnsFalse()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""Organization/org-old""}");

        // Act - try to use array index on a single reference
        bool result = ResourceReferenceHelper.UpdateReference(resource, "managingOrganization", "Organization/org-new", arrayIndex: 0);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region UpdateAllReferences Tests

    [Fact]
    public void UpdateAllReferences_WithSingleMatch_UpdatesOne()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = "obs-123",
        };
        resource.MutableNode["subject"] = JsonNode.Parse(@"{""reference"": ""Patient/pat-old""}");
        resource.MutableNode["performer"] = JsonNode.Parse(@"[
                    {""reference"": ""Practitioner/prac-1""}
                ]");

        // Act
        int count = ResourceReferenceHelper.UpdateAllReferences(resource, "Observation", "Patient/pat-old", "Patient/pat-new", _metadataProvider);

        // Assert
        Assert.Equal(1, count);
        var subjectElement = resource.MutableNode["subject"];
        Assert.Equal("Patient/pat-new", subjectElement["reference"].GetValue<string>());
    }

    [Fact]
    public void UpdateAllReferences_WithMultipleMatches_UpdatesAll()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = "obs-123",
        };
        resource.MutableNode["performer"] = JsonNode.Parse(@"[
                    {""reference"": ""Practitioner/prac-old""},
                    {""reference"": ""Practitioner/prac-other""},
                    {""reference"": ""Practitioner/prac-old""}
                ]");

        // Act
        int count = ResourceReferenceHelper.UpdateAllReferences(resource, "Observation", "Practitioner/prac-old", "Practitioner/prac-new", _metadataProvider);

        // Assert
        Assert.Equal(2, count);
        var array = resource.MutableNode["performer"] as JsonArray;
        Assert.NotNull(array);
        Assert.Equal("Practitioner/prac-new", array[0]["reference"].GetValue<string>());
        Assert.Equal("Practitioner/prac-other", array[1]["reference"].GetValue<string>());
        Assert.Equal("Practitioner/prac-new", array[2]["reference"].GetValue<string>());
    }

    [Fact]
    public void UpdateAllReferences_WithNoMatches_ReturnsZero()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };
        resource.MutableNode["managingOrganization"] = JsonNode.Parse(@"{""reference"": ""Organization/org-123""}");

        // Act
        int count = ResourceReferenceHelper.UpdateAllReferences(resource, "Patient", "Organization/org-999", "Organization/org-new", _metadataProvider);

        // Assert
        Assert.Equal(0, count);
        // Verify original value unchanged
        var element = resource.MutableNode["managingOrganization"];
        Assert.Equal("Organization/org-123", element["reference"].GetValue<string>());
    }

    [Fact]
    public void UpdateAllReferences_WithEmptyResource_ReturnsZero()
    {
        // Arrange
        var resource = new ResourceJsonNode
        {
            ResourceType = "Patient",
            Id = "test-123",
        };

        // Act
        int count = ResourceReferenceHelper.UpdateAllReferences(resource, "Patient", "Organization/org-old", "Organization/org-new", _metadataProvider);

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region Test Helper Classes

    /// <summary>
    /// Test implementation of IReferenceMetadataProvider with sample metadata for Patient and Observation.
    /// </summary>
    private class TestReferenceMetadataProvider : IReferenceMetadataProvider
    {
        private static readonly string[] OrganizationTargets = new[] { "Organization" };
        private static readonly string[] GeneralPractitionerTargets = new[] { "Organization", "Practitioner", "PractitionerRole" };
        private static readonly string[] SubjectTargets = new[] { "Patient", "Group", "Device", "Location" };
        private static readonly string[] PerformerTargets = new[] { "Practitioner", "PractitionerRole", "Organization", "CareTeam", "Patient", "RelatedPerson" };
        private static readonly string[] BasedOnTargets = new[] { "CarePlan", "DeviceRequest", "ImmunizationRecommendation", "MedicationRequest", "NutritionOrder", "ServiceRequest" };

        private readonly Dictionary<string, List<ReferenceFieldMetadata>> _metadata = new()
        {
            ["Patient"] = new List<ReferenceFieldMetadata>
            {
                new ReferenceFieldMetadata("managingOrganization", 0, "1", OrganizationTargets, true),
                new ReferenceFieldMetadata("generalPractitioner", 0, "*", GeneralPractitionerTargets, false),
            },
            ["Observation"] = new List<ReferenceFieldMetadata>
            {
                new ReferenceFieldMetadata("subject", 0, "1", SubjectTargets, true),
                new ReferenceFieldMetadata("performer", 0, "*", PerformerTargets, true),
                new ReferenceFieldMetadata("basedOn", 0, "*", BasedOnTargets, true),
            },
        };

        public IReadOnlyList<ReferenceFieldMetadata> GetMetadata(string resourceType)
        {
            return _metadata.TryGetValue(resourceType, out var metadata)
                ? metadata
                : Array.Empty<ReferenceFieldMetadata>();
        }

        public bool HasReferences(string resourceType)
        {
            return _metadata.ContainsKey(resourceType);
        }
    }

    #endregion
}
