// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Processors.Settings;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Processors.Settings
{
    public class SubstituteSettingTests
    {
        public static IEnumerable<object[]> GetSubstituteFhirRuleConfigs()
        {
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", null } }, null };
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", string.Empty } }, string.Empty };
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", "abc" } }, "abc" };
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address.city" }, { "method", "substitute" }, { "replaceWith", "**^^ŧ컴컴" } }, "**^^ŧ컴컴" };
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address" }, { "method", "substitute" }, { "replaceWith", "{}" } }, "{}" };
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address" }, { "method", "substitute" }, { "replaceWith", "{\"city\":\"abc\"}" } }, "{\"city\":\"abc\"}" };
        }

        public static IEnumerable<object[]> GetInvalidSubstituteFhirRuleConfigs()
        {
            yield return new object[] { new Dictionary<string, object>() { { "path", "Patient.address.city" }, { "method", "substitute" } } };
        }

        [Theory]
        [MemberData(nameof(GetSubstituteFhirRuleConfigs))]
        public void GivenASubstituteSetting_WhenCreate_ReplacementValueShouldBeParsedCorrectly(Dictionary<string, object> config, string expectedValue)
        {
            var substituteSetting = SubstituteSetting.CreateFromRuleSettings(config);
            Assert.Equal(expectedValue, substituteSetting.ReplaceWith);
        }

        [Theory]
        [MemberData(nameof(GetInvalidSubstituteFhirRuleConfigs))]
        public void GivenAInvalidSubstituteSetting_WhenValidate_ExceptionShouldBeThrown(Dictionary<string, object> config)
        {
            Assert.Throws<AnonymizerConfigurationException>(() => SubstituteSetting.ValidateRuleSettings(config));
        }
    }
}