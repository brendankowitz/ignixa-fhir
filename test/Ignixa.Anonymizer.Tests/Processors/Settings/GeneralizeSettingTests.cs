// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Configuration.ProcessorSettings;

namespace Ignixa.Anonymizer.Tests.Processors.Settings;

public class GeneralizeSettingTests
{
    public static IEnumerable<object[]> GetGeneralizeFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<=@2010-01-01 and $this>=@2010-01-01\": \"10\"}" } }, "{\n  \"$this<=@2010-01-01 and $this>=@2010-01-01\": \"10\"\n}", "Redact"];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<=10 and $this>=0\": \"10\"}" }, { "otherValues", "Keep" } }, "{\n  \"$this<=10 and $this>=0\": \"10\"\n}", "Keep"];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<=10 and $this>=0\": \"10\"}" }, { "otherValues", "Redact" } }, "{\n  \"$this<=10 and $this>=0\": \"10\"\n}", "Redact"];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this = @2015-01-01T00:00\": \"@2015-01-01T00:00:00Z\"}" } }, "{\n  \"$this = @2015-01-01T00:00\": \"@2015-01-01T00:00:00Z\"\n}", "Redact"];
    }

    public static IEnumerable<object[]> GetInvalidGeneralizeFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<=10 add $this>=0\": \"10\"}" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<=10 and $this>=0\": \"10 add\"}" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this sub 1\": \"10\"}" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<10\"+ \"10++\"}" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"$this<10\": \"10\"}" }, { "otherValues", "unknown" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "cases", "{\"\": \"\"}" }, { "otherValues", "Redact" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "method", "generalize" }, { "otherValues", "Keep" } }];
        yield return [new Dictionary<string, object> { { "path", "Patient.birthDate" }, { "cases", "{\"$this<10\": \"10\"}" }, { "otherValues", "Keep" } }];
        yield return [new Dictionary<string, object> { { "method", "generalize" }, { "cases", "{\"$this<10\": \"10\"}" }, { "otherValues", "Keep" } }];
        yield return [new Dictionary<string, object>()];
        yield return [null!];
    }

    [Theory]
    [MemberData(nameof(GetGeneralizeFhirRuleConfigs))]
    public void GivenAGeneralizeSetting_WhenCreate_ThenSettingsAreParsedCorrectly(
        Dictionary<string, object> config,
        string expectedCases,
        string expectedOtherValues)
    {
        // Act
        var generalizeSetting = GeneralizeSetting.CreateFromRuleSettings(config);

        // Assert
        var actualCases = generalizeSetting.Cases.ToString()
            .Replace("\r\n", "\n")
            .Replace("\\u003C", "<")
            .Replace("\\u003E", ">")
            .Replace("\\u003c", "<")
            .Replace("\\u003e", ">");
        var normalizedExpected = expectedCases.Replace("\r\n", "\n");
        actualCases.ShouldBe(normalizedExpected);
        generalizeSetting.OtherValues.ToString().ShouldBe(expectedOtherValues);
    }

    [Theory]
    [MemberData(nameof(GetInvalidGeneralizeFhirRuleConfigs))]
    public void GivenInvalidGeneralizeSetting_WhenValidate_ThenConfigurationExceptionIsThrown(Dictionary<string, object> config)
    {
        Should.Throw<ConfigurationException>(() => GeneralizeSetting.ValidateRuleSettings(config));
    }
}
