// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Configuration.ProcessorSettings;

namespace Ignixa.Anonymizer.Tests.Processors.Settings;

public class SubstituteSettingTests
{
    public static IEnumerable<object[]> GetSubstituteFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", null! } }, null!];
        yield return [new Dictionary<string, object> { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", string.Empty } }, ""];
        yield return [new Dictionary<string, object> { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", "abc" } }, "abc"];
        yield return [new Dictionary<string, object> { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", "**^^" } }, "**^^"];
        yield return [new Dictionary<string, object> { { "path", "Patient.address" }, { "method", "substitute" }, { "replaceWith", "{}" } }, "{}"];
        yield return [new Dictionary<string, object> { { "path", "Patient.address" }, { "method", "substitute" }, { "replaceWith", "{\"city\":\"abc\"}" } }, "{\"city\":\"abc\"}"];
    }

    public static IEnumerable<object[]> GetInvalidSubstituteFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Patient.address.city" }, { "method", "substitute" } }];
    }

    [Theory]
    [MemberData(nameof(GetSubstituteFhirRuleConfigs))]
    public void GivenASubstituteSetting_WhenCreate_ThenReplacementValueIsParsedCorrectly(
        Dictionary<string, object> config,
        string expectedValue)
    {
        // Act
        var substituteSetting = SubstituteSetting.CreateFromRuleSettings(config);

        // Assert
        substituteSetting.ReplaceWith.ShouldBe(expectedValue);
    }

    [Theory]
    [MemberData(nameof(GetInvalidSubstituteFhirRuleConfigs))]
    public void GivenInvalidSubstituteSetting_WhenValidate_ThenConfigurationExceptionIsThrown(Dictionary<string, object> config)
    {
        Should.Throw<ConfigurationException>(() => SubstituteSetting.ValidateRuleSettings(config));
    }
}
