// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Configuration;

namespace Ignixa.DeId.Tests.Configuration;

public class DeIdOptionsLoaderTests
{
    [Fact]
    public void GivenNonExistentFile_WhenLoadFromFile_ThenReturnsConfigNotFound()
    {
        // Act
        var result = DeIdOptionsLoader.LoadFromFile("does-not-exist.json");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("CONFIG_NOT_FOUND");
    }

    [Fact]
    public void GivenValidConfigJson_WhenLoadFromJson_ThenReturnsOptions()
    {
        // Arrange
        var json = """
        {
            "fhirVersion": "R4",
            "fhirPathRules": [
                { "path": "Patient.name", "method": "redact" }
            ]
        }
        """;

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.FhirVersion.ShouldBe("R4");
        result.Value.Rules.Length.ShouldBe(1);
        result.Value.Rules[0].Path.ShouldBe("Patient.name");
        result.Value.Rules[0].Method.ShouldBe("REDACT");
    }

    [Fact]
    public void GivenInvalidJson_WhenLoadFromJson_ThenReturnsJsonError()
    {
        // Act
        var result = DeIdOptionsLoader.LoadFromJson("{not valid json}");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("CONFIG_JSON_ERROR");
    }

    [Fact]
    public void GivenNullJson_WhenLoadFromJson_ThenReturnsParseError()
    {
        // Act
        var result = DeIdOptionsLoader.LoadFromJson("null");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("CONFIG_PARSE_ERROR");
    }

    [Theory]
    [InlineData("""{ "fhirVersion": "", "abc": "123" }""")]
    [InlineData("""{ "fhirVersion": "", "fhirPathRules": [] }""")]
    [InlineData("""{ "fhirVersion": "", "fhirPathRules": null }""")]
    public void GivenConfigWithoutRules_WhenLoadFromJson_ThenReturnsNoRulesError(string json)
    {
        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("CONFIG_NO_RULES");
    }

    [Fact]
    public void GivenConfigWithParameters_WhenLoadFromJson_ThenParametersAreParsed()
    {
        // Arrange
        var json = """
        {
            "fhirVersion": "R4",
            "fhirPathRules": [
                { "path": "Patient.name", "method": "redact" }
            ],
            "parameters": {
                "dateShiftKey": "test-key",
                "cryptoHashKey": "hash-key",
                "enablePartialDatesForRedact": true,
                "enablePartialAgesForRedact": true,
                "enablePartialZipCodesForRedact": true
            }
        }
        """;

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Parameters.ShouldNotBeNull();
        result.Value.Parameters!.DateShiftKey.ShouldBe("test-key");
        result.Value.Parameters.CryptoHashKey.ShouldBe("hash-key");
        result.Value.Parameters.EnablePartialDatesForRedact.ShouldBeTrue();
        result.Value.Parameters.EnablePartialAgesForRedact.ShouldBeTrue();
        result.Value.Parameters.EnablePartialZipCodesForRedact.ShouldBeTrue();
    }

    [Fact]
    public void GivenConfigWithProcessingOptions_WhenLoadFromJson_ThenProcessingOptionsAreParsed()
    {
        // Arrange
        var json = """
        {
            "fhirVersion": "R4",
            "fhirPathRules": [
                { "path": "Patient.name", "method": "redact" }
            ],
            "processing": {
                "processingErrors": "LogAndContinue",
                "isPrettyOutput": true
            }
        }
        """;

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Processing.ShouldNotBeNull();
        result.Value.Processing!.ErrorHandling.ShouldBe(ErrorHandlingMode.LogAndContinue);
        result.Value.Processing.IsPrettyOutput.ShouldBeTrue();
    }

    [Fact]
    public void GivenConfigWithRuleSettings_WhenLoadFromJson_ThenSettingsArePassedThrough()
    {
        // Arrange
        var json = """
        {
            "fhirVersion": "R4",
            "fhirPathRules": [
                {
                    "path": "Patient.name",
                    "method": "substitute",
                    "resourceType": "Patient",
                    "replaceWith": "REDACTED",
                    "roundTo": 2,
                    "span": 0.25,
                    "enabled": true,
                    "cases": { "true": "value" },
                    "unused": null
                }
            ]
        }
        """;

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Rules[0].Settings.ShouldNotBeNull();
        result.Value.Rules[0].ResourceType.ShouldBe("Patient");
        result.Value.Rules[0].Settings!["replaceWith"].ShouldBe("REDACTED");
        result.Value.Rules[0].Settings!["roundTo"].ShouldBeOfType<int>().ShouldBe(2);
        result.Value.Rules[0].Settings!["span"].ShouldBeOfType<double>().ShouldBe(0.25);
        result.Value.Rules[0].Settings!["enabled"].ShouldBeOfType<bool>().ShouldBeTrue();
        result.Value.Rules[0].Settings!["cases"].ShouldBe("""{"true":"value"}""");
        result.Value.Rules[0].Settings!.ContainsKey("unused").ShouldBeFalse();
    }

    [Fact]
    public void GivenConfigWithRulesButNoPathOrMethod_WhenLoadFromJson_ThenRulesAreSkipped()
    {
        // Arrange
        var json = """
        {
            "fhirVersion": "R4",
            "fhirPathRules": [
                null,
                {},
                { "method": "redact" },
                { "path": "Patient.name" },
                { "path": "", "method": "redact" },
                { "path": "Patient.name", "method": "" },
                { "path": " ", "method": "redact" },
                { "path": "Patient.name", "method": " " },
                { "path": "Patient.address", "method": "keep" }
            ]
        }
        """;

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Rules.Length.ShouldBe(1);
        result.Value.Rules[0].Path.ShouldBe("Patient.address");
    }

    [Fact]
    public void GivenMissingFhirVersion_WhenLoadFromJson_ThenVersionDefaultsToEmpty()
    {
        var result = DeIdOptionsLoader.LoadFromJson(
            """{"fhirPathRules":[{"path":"Patient.name","method":"redact"}]}""");

        result.IsSuccess.ShouldBeTrue();
        result.Value.FhirVersion.ShouldBeEmpty();
        result.Value.Parameters.ShouldBeNull();
        result.Value.Processing.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"fhirPathRules":{}}""")]
    [InlineData("""{"fhirPathRules":[{"path":1,"method":"redact"}]}""")]
    public void GivenWrongConfigurationShape_WhenLoadFromJson_ThenReturnsParseError(string json)
    {
        var result = DeIdOptionsLoader.LoadFromJson(json);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("CONFIG_PARSE_ERROR");
        result.Error.Exception.ShouldBeOfType<InvalidOperationException>();
    }
}
