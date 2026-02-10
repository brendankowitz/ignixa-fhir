// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Exceptions;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests
{
    public class AnonymizerConfigurationValidatorTests
    {
        private readonly AnonymizerConfigurationValidator _validator = new AnonymizerConfigurationValidator();

        public static IEnumerable<object[]> GetInvalidConfigs()
        {
            yield return new object[] { "./TestConfigurations/configuration-miss-rules.json" };
            yield return new object[] { "./TestConfigurations/configuration-invalid-fhirpath.json" };
            yield return new object[] { "./TestConfigurations/configuration-invalid-encryptkey.json" };
            yield return new object[] { "./TestConfigurations/configuration-miss-replacement.json" };
            yield return new object[] { "./TestConfigurations/configuration-perturb-wrong-rangetype.json" };
            yield return new object[] { "./TestConfigurations/configuration-perturb-miss-span.json" };
            yield return new object[] { "./TestConfigurations/configuration-perturb-negative-span.json" };
            yield return new object[] { "./TestConfigurations/configuration-perturb-wrong-roundTo.json" };
            yield return new object[] { "./TestConfigurations/configuration-perturb-negative-roundTo.json" };
            yield return new object[] { "./TestConfigurations/configuration-perturb-exceed-28-roundTo.json" };
            yield return new object[] { "./TestConfigurations/configuration-generalize-miss-cases.json" };
            yield return new object[] { "./TestConfigurations/configuration-generalize-fail-compiled-expression.json" };
            yield return new object[] { "./TestConfigurations/configuration-generalize-invalid-othervalues.json" };
        }

        [Theory]
        [MemberData(nameof(GetInvalidConfigs))]
        public void GivenAnInvalidConfig_WhenValidate_ExceptionShouldBeThrown(string configFilePath)
        {
            var content = File.ReadAllText(configFilePath);
            var _config = JsonSerializer.Deserialize<AnonymizerConfiguration>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Throws<AnonymizerConfigurationException>(() => _validator.Validate(_config));
        }
    }
}
