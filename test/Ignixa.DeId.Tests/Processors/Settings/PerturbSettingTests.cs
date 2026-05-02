// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Exceptions;
using Ignixa.DeId.Configuration.ProcessorSettings;

namespace Ignixa.DeId.Tests.Processors.Settings;

public class PerturbSettingTests
{
    public static IEnumerable<object[]> GetPerturbFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", 1 } }, 1d, 2, PerturbRangeType.Fixed];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", "1" }, { "roundTo", 28 } }, 1d, 28, PerturbRangeType.Fixed];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", 0.1 }, { "rangeType", "Proportional" } }, 0.1d, 2, PerturbRangeType.Proportional];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", 0.1 }, { "roundTo", 10 }, { "rangeType", "Proportional" } }, 0.1d, 10, PerturbRangeType.Proportional];
    }

    public static IEnumerable<object[]> GetInvalidPerturbFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", "test" } }];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", "-1" } }];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", "123" }, { "roundTo", "abc" } }];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", 1 }, { "roundTo", -200 } }];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", 1 }, { "roundTo", 29 } }];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "span", 0.1 }, { "rangeType", "Proportionaal" } }];
        yield return [new Dictionary<string, object> { { "path", "Condition.onset" }, { "method", "perturb" }, { "roundTo", 10 }, { "rangeType", "Proportional" } }];
    }

    [Theory]
    [MemberData(nameof(GetPerturbFhirRuleConfigs))]
    public void GivenAPerturbSetting_WhenCreate_ThenSettingsAreParsedCorrectly(
        Dictionary<string, object> config,
        double expectedSpan,
        int expectedRoundTo,
        PerturbRangeType expectedRangeType)
    {
        // Act
        var perturbSetting = PerturbSetting.CreateFromRuleSettings(config);

        // Assert
        perturbSetting.Span.ShouldBe(expectedSpan);
        perturbSetting.RoundTo.ShouldBe(expectedRoundTo);
        perturbSetting.RangeType.ShouldBe(expectedRangeType);
    }

    [Theory]
    [MemberData(nameof(GetInvalidPerturbFhirRuleConfigs))]
    public void GivenInvalidPerturbSetting_WhenValidate_ThenConfigurationExceptionIsThrown(Dictionary<string, object> config)
    {
        Should.Throw<ConfigurationException>(() => PerturbSetting.ValidateRuleSettings(config));
    }
}
