// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Features.Metadata.Models;

/// <summary>
/// Tests for CapabilityStatement serialization to verify correct JSON output
/// for different FHIR versions (R4, R4B, R5, STU3).
/// Ensures the BaseJsonNode pattern correctly serializes all properties.
/// </summary>
public class CapabilityStatementSerializationTests
{
    #region Basic Properties

    [Fact]
    public void GivenCapabilityStatement_WhenSerializing_ThenBasicPropertiesSerializeCorrectly()
    {
        // Arrange
        var capability = new CapabilityStatementJsonNode
        {
            Url = "http://example.com/fhir/CapabilityStatement/test",
            Version = "1.0.0",
            Name = "TestCapabilityStatement",
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Experimental = true,
            Date = "2025-01-15T10:30:00Z",
            Publisher = "Test Publisher",
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
            FhirVersionString = "4.0.1",
        };

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        parsed.Should().NotBeNull();
        parsed!["resourceType"]!.GetValue<string>().Should().Be("CapabilityStatement");
        parsed["url"]!.GetValue<string>().Should().Be("http://example.com/fhir/CapabilityStatement/test");
        parsed["version"]!.GetValue<string>().Should().Be("1.0.0");
        parsed["name"]!.GetValue<string>().Should().Be("TestCapabilityStatement");
        parsed["status"]!.GetValue<string>().Should().Be("active");
        parsed["experimental"]!.GetValue<bool>().Should().BeTrue();
        parsed["date"]!.GetValue<string>().Should().Be("2025-01-15T10:30:00Z");
        parsed["publisher"]!.GetValue<string>().Should().Be("Test Publisher");
        parsed["kind"]!.GetValue<string>().Should().Be("instance");
        parsed["fhirVersion"]!.GetValue<string>().Should().Be("4.0.1");
    }

    [Fact]
    public void GivenCapabilityStatementWithNullableProperties_WhenSerializing_ThenNullPropertiesAreOmitted()
    {
        // Arrange
        var capability = new CapabilityStatementJsonNode
        {
            Url = "http://example.com/fhir/CapabilityStatement/test",
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
            // Experimental is null
        };

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        parsed.Should().NotBeNull();
        parsed!.AsObject().ContainsKey("experimental").Should().BeFalse("null properties should be omitted");
    }

    #endregion

    #region Software Component

    [Fact]
    public void GivenSoftwareComponent_WhenSerializing_ThenPropertiesSerializeCorrectly()
    {
        // Arrange
        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
            Software = new SoftwareComponentJsonNode
            {
                Name = "Ignixa FHIR Server",
                Version = "2.0.0",
                ReleaseDate = "2025-10-19",
            },
        };

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var software = parsed!["software"];
        software.Should().NotBeNull();
        software!["name"]!.GetValue<string>().Should().Be("Ignixa FHIR Server");
        software["version"]!.GetValue<string>().Should().Be("2.0.0");
        software["releaseDate"]!.GetValue<string>().Should().Be("2025-10-19");
    }

    #endregion

    #region Format Arrays

    [Fact]
    public void GivenFormatAndPatchFormat_WhenSerializing_ThenArraysSerializeCorrectly()
    {
        // Arrange
        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.SetFormats(new List<string> { "application/fhir+json", "application/fhir+xml" });
        capability.SetPatchFormats(new List<string> { "application/json-patch+json" });

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var format = parsed!["format"]!.AsArray();
        format.Should().HaveCount(2);
        format[0]!.GetValue<string>().Should().Be("application/fhir+json");
        format[1]!.GetValue<string>().Should().Be("application/fhir+xml");

        var patchFormat = parsed["patchFormat"]!.AsArray();
        patchFormat.Should().HaveCount(1);
        patchFormat[0]!.GetValue<string>().Should().Be("application/json-patch+json");
    }

    #endregion

    #region REST Component

    [Fact]
    public void GivenRestComponent_WhenSerializing_ThenPropertiesSerializeCorrectly()
    {
        // Arrange
        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Documentation = "Test REST API",
        });

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var rest = parsed!["rest"]!.AsArray();
        rest.Should().HaveCount(1);
        rest[0]!["mode"]!.GetValue<string>().Should().Be("server");
        rest[0]!["documentation"]!.GetValue<string>().Should().Be("Test REST API");
    }

    #endregion

    #region Resource Component

    [Fact]
    public void GivenResourceComponent_WhenSerializing_ThenPropertiesSerializeCorrectly()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.AddResource(new ResourceComponentJsonNode
        {
            Type = "Patient",
            Documentation = "Patient resource operations",
            Versioning = ResourceComponentJsonNode.ResourceVersionPolicy.Versioned,
            ReadHistory = true,
            UpdateCreate = true,
            ConditionalCreate = false,
            ConditionalUpdate = true,
            ConditionalDelete = ConditionalDeleteStatus.Single,
        });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var resource = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!;
        resource["type"]!.GetValue<string>().Should().Be("Patient");
        resource["documentation"]!.GetValue<string>().Should().Be("Patient resource operations");
        resource["versioning"]!.GetValue<string>().Should().Be("versioned");
        resource["readHistory"]!.GetValue<bool>().Should().BeTrue();
        resource["updateCreate"]!.GetValue<bool>().Should().BeTrue();
        resource["conditionalCreate"]!.GetValue<bool>().Should().BeFalse();
        resource["conditionalUpdate"]!.GetValue<bool>().Should().BeTrue();
        resource["conditionalDelete"]!.GetValue<string>().Should().Be("single");
    }

    #endregion

    #region Interaction Components

    [Fact]
    public void GivenResourceInteractions_WhenSerializing_ThenInteractionsSerializeCorrectly()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.AddResource(new ResourceComponentJsonNode
        {
            Type = "Patient",
            Interaction = new List<ResourceInteractionJsonNode>
            {
                new() { Code = TypeRestfulInteraction.Read, Documentation = "Read patient" },
                new() { Code = TypeRestfulInteraction.Create },
                new() { Code = TypeRestfulInteraction.Update },
                new() { Code = TypeRestfulInteraction.Delete },
                new() { Code = TypeRestfulInteraction.SearchType },
            },
        });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var interactions = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["interaction"]!.AsArray();
        interactions.Should().HaveCount(5);
        interactions[0]!["code"]!.GetValue<string>().Should().Be("read");
        interactions[0]!["documentation"]!.GetValue<string>().Should().Be("Read patient");
        interactions[1]!["code"]!.GetValue<string>().Should().Be("create");
        interactions[2]!["code"]!.GetValue<string>().Should().Be("update");
        interactions[3]!["code"]!.GetValue<string>().Should().Be("delete");
        interactions[4]!["code"]!.GetValue<string>().Should().Be("search-type");
    }

    [Fact]
    public void GivenSystemInteractions_WhenSerializing_ThenInteractionsSerializeCorrectly()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Interaction = new List<SystemInteractionJsonNode>
            {
                new() { Code = SystemRestfulInteraction.Transaction },
                new() { Code = SystemRestfulInteraction.Batch },
                new() { Code = SystemRestfulInteraction.SearchSystem },
                new() { Code = SystemRestfulInteraction.HistorySystem },
            },
        };

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var interactions = parsed!["rest"]!.AsArray()[0]!["interaction"]!.AsArray();
        interactions.Should().HaveCount(4);
        interactions[0]!["code"]!.GetValue<string>().Should().Be("transaction");
        interactions[1]!["code"]!.GetValue<string>().Should().Be("batch");
        interactions[2]!["code"]!.GetValue<string>().Should().Be("search-system");
        interactions[3]!["code"]!.GetValue<string>().Should().Be("history-system");
    }

    #endregion

    #region Search Parameters

    [Fact]
    public void GivenSearchParameters_WhenSerializing_ThenParametersSerializeCorrectly()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.AddResource(new ResourceComponentJsonNode
        {
            Type = "Patient",
            SearchParam = new List<SearchParamJsonNode>
            {
                new SearchParamJsonNode
                {
                    Name = "family",
                    Definition = "http://hl7.org/fhir/SearchParameter/Patient-family",
                    Type = SearchParamType.String,
                    Documentation = "A portion of the family name",
                },
                new SearchParamJsonNode
                {
                    Name = "birthdate",
                    Definition = "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
                    Type = SearchParamType.Date,
                },
            },
        });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var searchParams = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["searchParam"]!.AsArray();
        searchParams.Should().HaveCount(2);

        searchParams[0]!["name"]!.GetValue<string>().Should().Be("family");
        searchParams[0]!["definition"]!.GetValue<string>().Should().Be("http://hl7.org/fhir/SearchParameter/Patient-family");
        searchParams[0]!["type"]!.GetValue<string>().Should().Be("string");
        searchParams[0]!["documentation"]!.GetValue<string>().Should().Be("A portion of the family name");

        searchParams[1]!["name"]!.GetValue<string>().Should().Be("birthdate");
        searchParams[1]!["definition"]!.GetValue<string>().Should().Be("http://hl7.org/fhir/SearchParameter/Patient-birthdate");
        searchParams[1]!["type"]!.GetValue<string>().Should().Be("date");
    }

    #endregion

    #region Security Component

    [Fact]
    public void GivenSecurityComponent_WhenSerializing_ThenPropertiesSerializeCorrectly()
    {
        // Arrange
        var coding = new Ignixa.Application.Features.Metadata.Models.CodingJsonNode
        {
            System = "http://terminology.hl7.org/CodeSystem/restful-security-service",
            Code = "SMART-on-FHIR",
            Display = "SMART-on-FHIR",
        };

        var codeableConcept = new Ignixa.Application.Features.Metadata.Models.CodeableConceptJsonNode
        {
            Text = "OAuth2",
        };
        codeableConcept.AddCoding(coding);

        var securityComponent = new SecurityComponentJsonNode
        {
            Cors = true,
            Description = "SMART-on-FHIR compliant",
        };
        securityComponent.AddService(codeableConcept);

        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Security = securityComponent,
        };

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var security = parsed!["rest"]!.AsArray()[0]!["security"]!;
        security["cors"]!.GetValue<bool>().Should().BeTrue();
        security["description"]!.GetValue<string>().Should().Be("SMART-on-FHIR compliant");

        var service = security["service"]!.AsArray()[0]!;
        service["text"]!.GetValue<string>().Should().Be("OAuth2");

        var codingNode = service["coding"]!.AsArray()[0]!;
        codingNode["system"]!.GetValue<string>().Should().Be("http://terminology.hl7.org/CodeSystem/restful-security-service");
        codingNode["code"]!.GetValue<string>().Should().Be("SMART-on-FHIR");
        codingNode["display"]!.GetValue<string>().Should().Be("SMART-on-FHIR");
    }

    #endregion

    #region Operations

    [Fact]
    public void GivenOperations_WhenSerializing_ThenOperationsSerializeCorrectly()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Operation = new List<OperationJsonNode>
            {
                new OperationJsonNode
                {
                    Name = "validate",
                    Definition = "http://hl7.org/fhir/OperationDefinition/Resource-validate",
                    Documentation = "Validate a resource",
                },
            },
        };

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var operations = parsed!["rest"]!.AsArray()[0]!["operation"]!.AsArray();
        operations.Should().HaveCount(1);
        operations[0]!["name"]!.GetValue<string>().Should().Be("validate");
        operations[0]!["definition"]!.GetValue<string>().Should().Be("http://hl7.org/fhir/OperationDefinition/Resource-validate");
        operations[0]!["documentation"]!.GetValue<string>().Should().Be("Validate a resource");
    }

    #endregion

    #region ReferenceOrCanonical (Version-Specific)

    [Fact]
    public void GivenReferenceOrCanonical_WhenSerializingAsCanonical_ThenSerializesAsString()
    {
        // Arrange - R4+ uses canonical string
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.AddResource(new ResourceComponentJsonNode
        {
            Type = "Patient",
            Profile = ReferenceOrCanonicalJsonNode.FromCanonical("http://hl7.org/fhir/StructureDefinition/Patient"),
        });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var profile = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["profile"];
        profile.Should().NotBeNull();

        // In R4, this should serialize as a simple canonical string (not an object)
        // When only Reference is set (no Display), Profile setter uses JsonValue.Create(string)
        profile!.GetValue<string>().Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenReferenceOrCanonicalWithDisplay_WhenSerializing_ThenBothPropertiesSerialize()
    {
        // Arrange - STU3 uses Reference object with reference and display
        // Build from inside-out to ensure FhirVersion is set before Profile
        var resourceComponent = new ResourceComponentJsonNode
        {
            FhirVersion = FhirSpecification.Stu3,
            Type = "Patient",
        };
        resourceComponent.Profile = ReferenceOrCanonicalJsonNode.FromCanonical(
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "Patient Profile");

        var restComponent = new RestComponentJsonNode
        {
            FhirVersion = FhirSpecification.Stu3,
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.AddResource(resourceComponent);

        var capability = new CapabilityStatementJsonNode
        {
            FhirVersion = FhirSpecification.Stu3,
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.AddRest(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var profile = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["profile"];
        profile.Should().NotBeNull();
        profile!["reference"]!.GetValue<string>().Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");
        profile["display"]!.GetValue<string>().Should().Be("Patient Profile");
    }

    #endregion

    #region Round-Trip Serialization

    [Fact]
    public void GivenCompleteCapabilityStatement_WhenSerializingAndDeserializing_ThenDataIsPreserved()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.AddResource(new ResourceComponentJsonNode
        {
            Type = "Patient",
            Versioning = ResourceComponentJsonNode.ResourceVersionPolicy.Versioned,
        });

        var original = new CapabilityStatementJsonNode
        {
            Url = "http://example.com/fhir/CapabilityStatement/test",
            Version = "1.0.0",
            Name = "TestCapabilityStatement",
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
            FhirVersionString = "4.0.1",
            Software = new SoftwareComponentJsonNode
            {
                Name = "Test Server",
                Version = "1.0.0",
            },
        };
        original.SetFormats(new List<string> { "application/fhir+json" });
        original.AddRest(restComponent);

        // Act
        var json = original.SerializeToString();
        var deserializedResource = JsonSourceNodeFactory.Parse(json);

        // Assert - Deserialize as ResourceJsonNode, then access as CapabilityStatement
        deserializedResource.Should().NotBeNull();
        deserializedResource!.ResourceType.Should().Be("CapabilityStatement");

        // Access properties through MutableNode since we don't have strong typing
        var deserialized = deserializedResource.MutableNode;
        deserialized["url"]!.GetValue<string>().Should().Be(original.Url);
        deserialized["version"]!.GetValue<string>().Should().Be(original.Version);
        deserialized["name"]!.GetValue<string>().Should().Be(original.Name);
        deserialized["status"]!.GetValue<string>().Should().Be("active"); // Enum literal
        deserialized["kind"]!.GetValue<string>().Should().Be("instance"); // Enum literal
        deserialized["fhirVersion"]!.GetValue<string>().Should().Be(original.FhirVersionString);
        deserialized["format"]!.AsArray().Select(n => n!.GetValue<string>()).Should().BeEquivalentTo(original.Format);
        deserialized["software"]!["name"]!.GetValue<string>().Should().Be(original.Software!.Name);
        deserialized["rest"]!.AsArray()[0]!["mode"]!.GetValue<string>().Should().Be("server");
        deserialized["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["type"]!.GetValue<string>().Should().Be("Patient");
    }

    #endregion
}
