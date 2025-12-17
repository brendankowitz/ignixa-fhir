// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Features.Metadata.Models;

/// <summary>
/// Tests for CapabilityStatement serialization to verify correct JSON output
/// for different FHIR versions (R4, R4B, R5, Stu3).
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
        parsed.ShouldNotBeNull();
        parsed!["resourceType"]!.GetValue<string>().ShouldBe("CapabilityStatement");
        parsed["url"]!.GetValue<string>().ShouldBe("http://example.com/fhir/CapabilityStatement/test");
        parsed["version"]!.GetValue<string>().ShouldBe("1.0.0");
        parsed["name"]!.GetValue<string>().ShouldBe("TestCapabilityStatement");
        parsed["status"]!.GetValue<string>().ShouldBe("active");
        parsed["experimental"]!.GetValue<bool>().ShouldBeTrue();
        parsed["date"]!.GetValue<string>().ShouldBe("2025-01-15T10:30:00Z");
        parsed["publisher"]!.GetValue<string>().ShouldBe("Test Publisher");
        parsed["kind"]!.GetValue<string>().ShouldBe("instance");
        parsed["fhirVersion"]!.GetValue<string>().ShouldBe("4.0.1");
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
        parsed.ShouldNotBeNull();
        parsed!.AsObject().ContainsKey("experimental").ShouldBeFalse("null properties should be omitted");
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
        software.ShouldNotBeNull();
        software!["name"]!.GetValue<string>().ShouldBe("Ignixa FHIR Server");
        software["version"]!.GetValue<string>().ShouldBe("2.0.0");
        software["releaseDate"]!.GetValue<string>().ShouldBe("2025-10-19");
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
        capability.Format.Add("application/fhir+json");
        capability.Format.Add("application/fhir+xml");
        capability.PatchFormat.Add("application/json-patch+json");

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var format = parsed!["format"]!.AsArray();
        format.Count.ShouldBe(2);
        format[0]!.GetValue<string>().ShouldBe("application/fhir+json");
        format[1]!.GetValue<string>().ShouldBe("application/fhir+xml");

        var patchFormat = parsed["patchFormat"]!.AsArray();
        patchFormat.Count.ShouldBe(1);
        patchFormat[0]!.GetValue<string>().ShouldBe("application/json-patch+json");
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
        capability.Rest.Add(new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            Documentation = "Test REST API",
        });

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var rest = parsed!["rest"]!.AsArray();
        rest.Count.ShouldBe(1);
        rest[0]!["mode"]!.GetValue<string>().ShouldBe("server");
        rest[0]!["documentation"]!.GetValue<string>().ShouldBe("Test REST API");
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
        restComponent.Resource.Add(new ResourceComponentJsonNode
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
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var resource = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!;
        resource["type"]!.GetValue<string>().ShouldBe("Patient");
        resource["documentation"]!.GetValue<string>().ShouldBe("Patient resource operations");
        resource["versioning"]!.GetValue<string>().ShouldBe("versioned");
        resource["readHistory"]!.GetValue<bool>().ShouldBeTrue();
        resource["updateCreate"]!.GetValue<bool>().ShouldBeTrue();
        resource["conditionalCreate"]!.GetValue<bool>().ShouldBeFalse();
        resource["conditionalUpdate"]!.GetValue<bool>().ShouldBeTrue();
        resource["conditionalDelete"]!.GetValue<string>().ShouldBe("single");
    }

    #endregion

    #region Interaction Components

    [Fact]
    public void GivenResourceInteractions_WhenSerializing_ThenInteractionsSerializeCorrectly()
    {
        // Arrange
        var resourceComponent = new ResourceComponentJsonNode
        {
            Type = "Patient",
        };
        resourceComponent.Interaction.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Read, Documentation = "Read patient" });
        resourceComponent.Interaction.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Create });
        resourceComponent.Interaction.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Update });
        resourceComponent.Interaction.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.Delete });
        resourceComponent.Interaction.Add(new ResourceInteractionJsonNode { Code = TypeRestfulInteraction.SearchType });

        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.Resource.Add(resourceComponent);

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var interactions = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["interaction"]!.AsArray();
        interactions.Count.ShouldBe(5);
        interactions[0]!["code"]!.GetValue<string>().ShouldBe("read");
        interactions[0]!["documentation"]!.GetValue<string>().ShouldBe("Read patient");
        interactions[1]!["code"]!.GetValue<string>().ShouldBe("create");
        interactions[2]!["code"]!.GetValue<string>().ShouldBe("update");
        interactions[3]!["code"]!.GetValue<string>().ShouldBe("delete");
        interactions[4]!["code"]!.GetValue<string>().ShouldBe("search-type");
    }

    [Fact]
    public void GivenSystemInteractions_WhenSerializing_ThenInteractionsSerializeCorrectly()
    {
        // Arrange
        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.Interaction.Add(new SystemInteractionJsonNode { Code = SystemRestfulInteraction.Transaction });
        restComponent.Interaction.Add(new SystemInteractionJsonNode { Code = SystemRestfulInteraction.Batch });
        restComponent.Interaction.Add(new SystemInteractionJsonNode { Code = SystemRestfulInteraction.SearchSystem });
        restComponent.Interaction.Add(new SystemInteractionJsonNode { Code = SystemRestfulInteraction.HistorySystem });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var interactions = parsed!["rest"]!.AsArray()[0]!["interaction"]!.AsArray();
        interactions.Count.ShouldBe(4);
        interactions[0]!["code"]!.GetValue<string>().ShouldBe("transaction");
        interactions[1]!["code"]!.GetValue<string>().ShouldBe("batch");
        interactions[2]!["code"]!.GetValue<string>().ShouldBe("search-system");
        interactions[3]!["code"]!.GetValue<string>().ShouldBe("history-system");
    }

    #endregion

    #region Search Parameters

    [Fact]
    public void GivenSearchParameters_WhenSerializing_ThenParametersSerializeCorrectly()
    {
        // Arrange
        var resourceComponent = new ResourceComponentJsonNode
        {
            Type = "Patient",
        };
        resourceComponent.SearchParam.Add(new SearchParamJsonNode
        {
            Name = "family",
            Definition = "http://hl7.org/fhir/SearchParameter/Patient-family",
            Type = SearchParamType.String,
            Documentation = "A portion of the family name",
        });
        resourceComponent.SearchParam.Add(new SearchParamJsonNode
        {
            Name = "birthdate",
            Definition = "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
            Type = SearchParamType.Date,
        });

        var restComponent = new RestComponentJsonNode
        {
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.Resource.Add(resourceComponent);

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var searchParams = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["searchParam"]!.AsArray();
        searchParams.Count.ShouldBe(2);

        searchParams[0]!["name"]!.GetValue<string>().ShouldBe("family");
        searchParams[0]!["definition"]!.GetValue<string>().ShouldBe("http://hl7.org/fhir/SearchParameter/Patient-family");
        searchParams[0]!["type"]!.GetValue<string>().ShouldBe("string");
        searchParams[0]!["documentation"]!.GetValue<string>().ShouldBe("A portion of the family name");

        searchParams[1]!["name"]!.GetValue<string>().ShouldBe("birthdate");
        searchParams[1]!["definition"]!.GetValue<string>().ShouldBe("http://hl7.org/fhir/SearchParameter/Patient-birthdate");
        searchParams[1]!["type"]!.GetValue<string>().ShouldBe("date");
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
        codeableConcept.Coding.Add(coding);

        var securityComponent = new SecurityComponentJsonNode
        {
            Cors = true,
            Description = "SMART-on-FHIR compliant",
        };
        securityComponent.Service.Add(codeableConcept);

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
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var security = parsed!["rest"]!.AsArray()[0]!["security"]!;
        security["cors"]!.GetValue<bool>().ShouldBeTrue();
        security["description"]!.GetValue<string>().ShouldBe("SMART-on-FHIR compliant");

        var service = security["service"]!.AsArray()[0]!;
        service["text"]!.GetValue<string>().ShouldBe("OAuth2");

        var codingNode = service["coding"]!.AsArray()[0]!;
        codingNode["system"]!.GetValue<string>().ShouldBe("http://terminology.hl7.org/CodeSystem/restful-security-service");
        codingNode["code"]!.GetValue<string>().ShouldBe("SMART-on-FHIR");
        codingNode["display"]!.GetValue<string>().ShouldBe("SMART-on-FHIR");
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
        };
        restComponent.Operation.Add(new OperationJsonNode
        {
            Name = "validate",
            Definition = "http://hl7.org/fhir/OperationDefinition/Resource-validate",
            Documentation = "Validate a resource",
        });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var operations = parsed!["rest"]!.AsArray()[0]!["operation"]!.AsArray();
        operations.Count.ShouldBe(1);
        operations[0]!["name"]!.GetValue<string>().ShouldBe("validate");
        operations[0]!["definition"]!.GetValue<string>().ShouldBe("http://hl7.org/fhir/OperationDefinition/Resource-validate");
        operations[0]!["documentation"]!.GetValue<string>().ShouldBe("Validate a resource");
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
        restComponent.Resource.Add(new ResourceComponentJsonNode
        {
            Type = "Patient",
            Profile = ReferenceOrCanonicalJsonNode.FromCanonical("http://hl7.org/fhir/StructureDefinition/Patient"),
        });

        var capability = new CapabilityStatementJsonNode
        {
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var profile = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["profile"];
        profile.ShouldNotBeNull();

        // In R4, this should serialize as a simple canonical string (not an object)
        // When only Reference is set (no Display), Profile setter uses JsonValue.Create(string)
        profile!.GetValue<string>().ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenReferenceOrCanonicalWithDisplay_WhenSerializing_ThenBothPropertiesSerialize()
    {
        // Arrange - Stu3 uses Reference object with reference and display
        // Build from inside-out to ensure FhirVersion is set before Profile
        var resourceComponent = new ResourceComponentJsonNode
        {
            FhirVersion = FhirVersion.Stu3,
            Type = "Patient",
        };
        resourceComponent.Profile = ReferenceOrCanonicalJsonNode.FromCanonical(
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "Patient Profile");

        var restComponent = new RestComponentJsonNode
        {
            FhirVersion = FhirVersion.Stu3,
            Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
        };
        restComponent.Resource.Add(resourceComponent);

        var capability = new CapabilityStatementJsonNode
        {
            FhirVersion = FhirVersion.Stu3,
            Status = CapabilityStatementJsonNode.PublicationStatus.Active,
            Kind = CapabilityStatementJsonNode.CapabilityStatementKind.Instance,
        };
        capability.Rest.Add(restComponent);

        // Act
        var json = capability.SerializeToString();
        var parsed = JsonNode.Parse(json);

        // Assert
        var profile = parsed!["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["profile"];
        profile.ShouldNotBeNull();
        profile!["reference"]!.GetValue<string>().ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
        profile["display"]!.GetValue<string>().ShouldBe("Patient Profile");
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
        restComponent.Resource.Add(new ResourceComponentJsonNode
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
        original.Format.Add("application/fhir+json");
        original.Rest.Add(restComponent);

        // Act
        var json = original.SerializeToString();
        var deserializedResource = JsonSourceNodeFactory.Parse(json);

        // Assert - Deserialize as ResourceJsonNode, then access as CapabilityStatement
        deserializedResource.ShouldNotBeNull();
        deserializedResource!.ResourceType.ShouldBe("CapabilityStatement");

        // Access properties through MutableNode since we don't have strong typing
        var deserialized = deserializedResource.MutableNode;
        deserialized["url"]!.GetValue<string>().ShouldBe(original.Url);
        deserialized["version"]!.GetValue<string>().ShouldBe(original.Version);
        deserialized["name"]!.GetValue<string>().ShouldBe(original.Name);
        deserialized["status"]!.GetValue<string>().ShouldBe("active"); // Enum literal
        deserialized["kind"]!.GetValue<string>().ShouldBe("instance"); // Enum literal
        deserialized["fhirVersion"]!.GetValue<string>().ShouldBe(original.FhirVersionString);
        deserialized["format"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(original.Format);
        deserialized["software"]!["name"]!.GetValue<string>().ShouldBe(original.Software!.Name);
        deserialized["rest"]!.AsArray()[0]!["mode"]!.GetValue<string>().ShouldBe("server");
        deserialized["rest"]!.AsArray()[0]!["resource"]!.AsArray()[0]!["type"]!.GetValue<string>().ShouldBe("Patient");
    }

    #endregion
}
