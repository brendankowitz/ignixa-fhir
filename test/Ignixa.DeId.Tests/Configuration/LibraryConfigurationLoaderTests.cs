// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DeId.Configuration;
using Ignixa.DeId.Darts;
using Ignixa.DeId.Darts.Configuration;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.DeId.Tests.Configuration;

public class LibraryConfigurationLoaderTests
{
    private readonly LibraryConfigurationLoader _loader = new();

    [Fact]
    public void GivenValidLibraryResource_WhenLoadingConfiguration_ThenReturnsDeIdOptions()
    {
        // Arrange
        var options = new DeIdOptions
        {
            FhirVersion = "R4",
            Rules =
            [
                new FhirPathRule { Path = "Patient.name", Method = "redact" }
            ]
        };
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-1", "SAFE_HARBOR", options);

        // Act
        var result = _loader.LoadFromLibrary(library);

        // Assert
        result.ShouldNotBeNull();
        result.FhirVersion.ShouldBe("R4");
        result.Rules.Length.ShouldBe(1);
        result.Rules[0].Path.ShouldBe("Patient.name");
        result.Rules[0].Method.ShouldBe("redact");
    }

    [Fact]
    public void GivenLibraryWithMultipleRules_WhenLoadingConfiguration_ThenReturnsAllRules()
    {
        // Arrange
        var options = new DeIdOptions
        {
            FhirVersion = "R4",
            Rules =
            [
                new FhirPathRule { Path = "Patient.id", Method = "cryptoHash" },
                new FhirPathRule { Path = "Patient.name", Method = "redact" },
                new FhirPathRule { Path = "Patient.birthDate", Method = "dateShift" }
            ],
            Parameters = new ParameterOptions
            {
                EnablePartialDatesForRedact = true,
                EnablePartialAgesForRedact = false
            },
            Processing = new ProcessingOptions
            {
                ErrorHandling = ErrorHandlingMode.FailFast
            }
        };
        var library = LibraryConfigurationLoader.CreateLibraryResource("lib-2", "EXPERT_DETERMINATION", options, version: "2.0.0");

        // Act
        var result = _loader.LoadFromLibrary(library);

        // Assert
        result.Rules.Length.ShouldBe(3);
        result.Parameters.EnablePartialDatesForRedact.ShouldBeTrue();
        result.Parameters.EnablePartialAgesForRedact.ShouldBeFalse();
        result.Processing.ErrorHandling.ShouldBe(ErrorHandlingMode.FailFast);
    }

    [Fact]
    public void GivenNonLibraryResource_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var patientJson = """
            {
                "resourceType": "Patient",
                "id": "p1"
            }
            """;
        var patient = ResourceJsonNode.Parse(patientJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(patient))
            .Message.ShouldContain("Expected resourceType 'Library'");
    }

    [Fact]
    public void GivenLibraryWithoutTypeCoding_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var libraryJson = """
            {
                "resourceType": "Library",
                "id": "no-type-lib",
                "status": "active",
                "content": [
                    {
                        "contentType": "application/json",
                        "data": "eyJmaGlyVmVyc2lvbiI6IlI0IiwicnVsZXMiOlt7InBhdGgiOiJQYXRpZW50Lm5hbWUiLCJtZXRob2QiOiJyZWRhY3QifV19"
                    }
                ]
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain("Library.type is required and must contain a coding");
    }

    [Fact]
    public void GivenLibraryWithWrongTypeCoding_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var libraryJson = """
            {
                "resourceType": "Library",
                "id": "wrong-type-lib",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "http://terminology.hl7.org/CodeSystem/library-type",
                            "code": "logic-library"
                        }
                    ]
                },
                "content": [
                    {
                        "contentType": "application/json",
                        "data": "eyJmaGlyVmVyc2lvbiI6IlI0IiwicnVsZXMiOlt7InBhdGgiOiJQYXRpZW50Lm5hbWUiLCJtZXRob2QiOiJyZWRhY3QifV19"
                    }
                ]
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain($"Library.type must contain coding with system '{DartsConstants.LibraryTypeSystem}'");
    }

    [Fact]
    public void GivenLibraryWithEmptyFhirVersion_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var invalidJsonBytes = System.Text.Encoding.UTF8.GetBytes("""{"fhirVersion":"","fhirPathRules":[]}""");
        var base64 = Convert.ToBase64String(invalidJsonBytes);
        var libraryJson = $$"""
            {
                "resourceType": "Library",
                "id": "empty-version-lib",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "{{DartsConstants.LibraryTypeSystem}}",
                            "code": "{{DartsConstants.LibraryTypeCode}}"
                        }
                    ]
                },
                "content": [
                    {
                        "contentType": "application/json",
                        "data": "{{base64}}"
                    }
                ]
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain("DeIdOptions.fhirVersion is required");
    }

    [Fact]
    public void GivenLibraryWithEmptyRules_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var invalidJsonBytes = System.Text.Encoding.UTF8.GetBytes("""{"fhirVersion":"R4","fhirPathRules":[]}""");
        var base64 = Convert.ToBase64String(invalidJsonBytes);
        var libraryJson = $$"""
            {
                "resourceType": "Library",
                "id": "empty-rules-lib",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "{{DartsConstants.LibraryTypeSystem}}",
                            "code": "{{DartsConstants.LibraryTypeCode}}"
                        }
                    ]
                },
                "content": [
                    {
                        "contentType": "application/json",
                        "data": "{{base64}}"
                    }
                ]
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain("DeIdOptions.rules must contain at least one rule");
    }

    [Fact]
    public void GivenLibraryWithoutContent_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var libraryJson = """
            {
                "resourceType": "Library",
                "id": "empty-lib",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "http://ignixa.io/library-types",
                            "code": "deid-configuration"
                        }
                    ]
                }
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain("Library.content is required");
    }

    [Fact]
    public void GivenLibraryWithNonJsonContent_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var libraryJson = """
            {
                "resourceType": "Library",
                "id": "bad-content-lib",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "http://ignixa.io/library-types",
                            "code": "deid-configuration"
                        }
                    ]
                },
                "content": [
                    {
                        "contentType": "text/plain",
                        "data": "bm90LWpzb24="
                    }
                ]
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain("No application/json attachment found");
    }

    [Fact]
    public void GivenLibraryWithInvalidJsonData_WhenLoadingConfiguration_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var invalidJsonBytes = System.Text.Encoding.UTF8.GetBytes("not valid json");
        var base64 = Convert.ToBase64String(invalidJsonBytes);
        var libraryJson = $$"""
            {
                "resourceType": "Library",
                "id": "invalid-json-lib",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "http://ignixa.io/library-types",
                            "code": "deid-configuration"
                        }
                    ]
                },
                "content": [
                    {
                        "contentType": "application/json",
                        "data": "{{base64}}"
                    }
                ]
            }
            """;
        var library = ResourceJsonNode.Parse(libraryJson);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _loader.LoadFromLibrary(library))
            .Message.ShouldContain("Failed to deserialize");
    }

    [Fact]
    public void GivenCreateLibraryResource_WhenSerializing_ThenContainsExpectedMetadata()
    {
        // Arrange
        var options = new DeIdOptions { FhirVersion = "R4", Rules = [] };

        // Act
        var library = LibraryConfigurationLoader.CreateLibraryResource("deid-safe-harbor", "SAFE_HARBOR", options, version: "1.2.3");

        // Assert
        library.MutableNode["resourceType"]?.GetValue<string>().ShouldBe("Library");
        library.MutableNode["id"]?.GetValue<string>().ShouldBe("deid-safe-harbor");
        library.MutableNode["status"]?.GetValue<string>().ShouldBe("active");
        library.MutableNode["version"]?.GetValue<string>().ShouldBe("1.2.3");

        var typeNode = library.MutableNode["type"]?.AsObject();
        var typeCoding = typeNode?["coding"]?.AsArray()?.FirstOrDefault();
        typeCoding.ShouldNotBeNull();
        typeCoding["system"]?.GetValue<string>().ShouldBe(DartsConstants.LibraryTypeSystem);
        typeCoding["code"]?.GetValue<string>().ShouldBe(DartsConstants.LibraryTypeCode);

        var identifier = library.MutableNode["identifier"]?.AsArray()?.FirstOrDefault();
        identifier.ShouldNotBeNull();
        identifier["system"]?.GetValue<string>().ShouldBe("http://hl7.org/fhir/us/darts/CodeSystem/DARTSPolicyIdentifiers");
        identifier["value"]?.GetValue<string>().ShouldBe("SAFE_HARBOR");

        var content = library.MutableNode["content"]?.AsArray()?.FirstOrDefault();
        content.ShouldNotBeNull();
        content["contentType"]?.GetValue<string>().ShouldBe("application/json");
        content["data"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCreatedLibraryResource_WhenRoundTrippedThroughLoader_ThenOptionsArePreserved()
    {
        // Arrange
        var originalOptions = new DeIdOptions
        {
            FhirVersion = "R4",
            Rules =
            [
                new FhirPathRule { Path = "Patient.identifier", Method = "redact" }
            ],
            Parameters = new ParameterOptions
            {
                EnablePartialZipCodesForRedact = true
            }
        };
        var library = LibraryConfigurationLoader.CreateLibraryResource("roundtrip-lib", "TEST", originalOptions);

        // Act
        var loadedOptions = _loader.LoadFromLibrary(library);

        // Assert
        loadedOptions.FhirVersion.ShouldBe(originalOptions.FhirVersion);
        loadedOptions.Rules.Length.ShouldBe(originalOptions.Rules.Length);
        loadedOptions.Rules[0].Path.ShouldBe(originalOptions.Rules[0].Path);
        loadedOptions.Rules[0].Method.ShouldBe(originalOptions.Rules[0].Method);
        loadedOptions.Parameters.EnablePartialZipCodesForRedact.ShouldBe(originalOptions.Parameters.EnablePartialZipCodesForRedact);
    }
}
