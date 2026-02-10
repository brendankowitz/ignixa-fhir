// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Exceptions;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.AnonymizerConfigurations;

public class VersionValidatorTests
{
    private readonly AnonymizerConfigurationValidator _validator = new AnonymizerConfigurationValidator();

    public static IEnumerable<object[]> GetConfigsWithInvalidFhirVersion()
    {
        yield return new object[] { "./TestConfigurationsVersion/configuration-invalid-version.json" };
    }

    public static IEnumerable<object[]> GetConfigsWithValidFhirVersion()
    {
        // All FHIR versions are now valid since we support R4, R4B, R5, R6, and STU3
        yield return new object[] { "./TestConfigurationsVersion/configuration-R4-version.json" };
        yield return new object[] { "./TestConfigurationsVersion/configuration-Stu3-version.json" };
        yield return new object[] { "./TestConfigurationsVersion/configuration-empty-version.json" };
        yield return new object[] { "./TestConfigurationsVersion/configuration-null-version.json" };
    }

    [Theory]
    [MemberData(nameof(GetConfigsWithInvalidFhirVersion))]
    public void GivenAnInvalidVersion_WhenCreateAnonymizerConfigurationManager_ExceptionShouldBeThrown(string configFilePath)
    {
        var content = File.ReadAllText(configFilePath);
        var _config = JsonSerializer.Deserialize<AnonymizerConfiguration>(content);
        Assert.Throws<AnonymizerConfigurationException>(() => _validator.Validate(_config));
    }

    [Theory]
    [MemberData(nameof(GetConfigsWithValidFhirVersion))]
    public void GivenAValidVersion_WhenCreateAnonymizerConfigurationManager_ConfigurationShouldBeLoaded(string configFilePath)
    {
        var configurationManager = AnonymizerConfigurationManager.CreateFromConfigurationFile(configFilePath);
        var fhirRules = configurationManager.FhirPathRules;
        Assert.True(fhirRules.Any());
        fhirRules = configurationManager.FhirPathRules;
        Assert.Single(configurationManager.FhirPathRules.Where(r => "Patient".Equals(r.ResourceType)));
        Assert.Single(configurationManager.FhirPathRules.Where(r => "TestResource".Equals(r.ResourceType)));
        Assert.Single(configurationManager.FhirPathRules.Where(r => string.IsNullOrEmpty(r.ResourceType)));
        Assert.Single(configurationManager.FhirPathRules.Where(r => "Resource".Equals(r.ResourceType)));
        Assert.Single(configurationManager.FhirPathRules.Where(r => "Device".Equals(r.ResourceType)));

        var parameters = configurationManager.GetParameterConfiguration();
        Assert.True(!string.IsNullOrEmpty(parameters.DateShiftKey));
    }
}
