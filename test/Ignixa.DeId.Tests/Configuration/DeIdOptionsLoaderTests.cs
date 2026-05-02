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

    [Fact]
    public void GivenConfigWithoutRules_WhenLoadFromJson_ThenReturnsNoRulesError()
    {
        // Arrange
        var json = """{ "fhirVersion": "", "abc": "123" }""";

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("CONFIG_NO_RULES");
    }

    [Fact]
    public void GivenConfigWithEmptyRules_WhenLoadFromJson_ThenReturnsNoRulesError()
    {
        // Arrange
        var json = """{ "fhirVersion": "", "fhirPathRules": [] }""";

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
                    "replaceWith": "REDACTED"
                }
            ]
        }
        """;

        // Act
        var result = DeIdOptionsLoader.LoadFromJson(json);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Rules[0].Settings.ShouldNotBeNull();
        result.Value.Rules[0].Settings!["replaceWith"].ShouldBe("REDACTED");
    }

    [Fact]
    public void GivenConfigWithRulesButNoPathOrMethod_WhenLoadFromJson_ThenRulesAreSkipped()
    {
        // Arrange
        var json = """
        {
            "fhirVersion": "R4",
            "fhirPathRules": [
                { "path": "", "method": "redact" },
                { "path": "Patient.name", "method": "" },
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
}
