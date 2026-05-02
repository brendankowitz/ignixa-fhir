// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Configuration;

namespace Ignixa.DeId.Tests.Configuration;

public class DeIdFhirPathRuleTests
{
    public static IEnumerable<object[]> GetFhirRuleConfigs()
    {
        yield return [new Dictionary<string, object> { { "path", "Patient.address" }, { "method", "Test" } }, "Patient", "address", "Patient.address", "Test"];
        yield return [new Dictionary<string, object> { { "path", "Patient.descendants().ofType(Address)" }, { "method", "Test" } }, "Patient", "descendants().ofType(Address)", "Patient.descendants().ofType(Address)", "Test"];
        yield return [new Dictionary<string, object> { { "path", "descendants().ofType(Address)" }, { "method", "Test" } }, "", "descendants().ofType(Address)", "descendants().ofType(Address)", "Test"];
        yield return [new Dictionary<string, object> { { "path", "descendants().where(name = 'telecom')" }, { "method", "Test" } }, "", "descendants().where(name = 'telecom')", "descendants().where(name = 'telecom')", "Test"];
        yield return [new Dictionary<string, object> { { "path", "Patient" }, { "method", "Test" } }, "Patient", "Patient", "Patient", "Test"];
        yield return [new Dictionary<string, object> { { "path", "Patient.abc.func(n=1).a.test('abc')" }, { "method", "Test" } }, "Patient", "abc.func(n=1).a.test('abc')", "Patient.abc.func(n=1).a.test('abc')", "Test"];
        yield return [new Dictionary<string, object> { { "path", "Resource" }, { "method", "Test" } }, "Resource", "Resource", "Resource", "Test"];
    }

    [Theory]
    [MemberData(nameof(GetFhirRuleConfigs))]
    public void GivenAFhirPath_WhenCreatePathRule_ThenFhirPathRuleIsCreatedCorrectly(
        Dictionary<string, object> config,
        string expectedResourceType,
        string expectedExpression,
        string expectedPath,
        string expectedMethod)
    {
        // Act
        var rule = DeIdFhirPathRule.CreateDeIdFhirPathRule(config);

        // Assert
        rule.Path.ShouldBe(expectedPath);
        rule.Method.ShouldBe(expectedMethod);
        rule.ResourceType.ShouldBe(expectedResourceType);
        rule.Expression.ShouldBe(expectedExpression);
    }
}
