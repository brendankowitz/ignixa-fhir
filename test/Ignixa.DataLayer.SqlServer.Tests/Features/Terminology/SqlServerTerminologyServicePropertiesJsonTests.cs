// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.Terminology;

/// <summary>
/// Pins <c>SqlServerTerminologyService.ParsePropertiesJson</c>'s handling of stored
/// <c>CodeSystem.concept.property</c>/<c>designation</c> JSON. Regression guards for two bugs found during
/// PR review: a <c>designation.use</c> Coding (an object, not a string) previously threw
/// <see cref="InvalidOperationException"/> uncaught by the method's <c>catch (JsonException)</c>, and
/// <c>concept.property.value[x]</c>'s type-suffixed key (e.g. <c>valueCode</c>) was never read because the
/// old code looked for a literal <c>"value"</c> key that FHIR never emits.
/// </summary>
public class SqlServerTerminologyServicePropertiesJsonTests
{
    private static SqlServerTerminologyService CreateService() =>
        new(
            Substitute.For<ISqlExecutionService>(),
            systemPartitionId: 0,
            NullLogger<SqlServerTerminologyService>.Instance);

    [Fact]
    public void GivenADesignationWithACodingUse_WhenParsingPropertiesJson_ThenTheUseCodeIsReturnedWithoutThrowing()
    {
        const string json = """
            {
                "designation": [
                    {
                        "language": "en",
                        "use": { "system": "http://snomed.info/sct", "code": "900000000000013009", "display": "Synonym" },
                        "value": "Heart attack"
                    }
                ]
            }
            """;

        var (_, designations) = CreateService().ParsePropertiesJson(json);

        designations.ShouldNotBeNull();
        designations!.Count.ShouldBe(1);
        designations[0].Language.ShouldBe("en");
        designations[0].Use.ShouldBe("900000000000013009");
        designations[0].Value.ShouldBe("Heart attack");
    }

    [Fact]
    public void GivenADesignationWithAStringUse_WhenParsingPropertiesJson_ThenUseIsNull()
    {
        const string json = """
            {
                "designation": [
                    { "language": "en", "use": "not-a-coding", "value": "Heart attack" }
                ]
            }
            """;

        var (_, designations) = CreateService().ParsePropertiesJson(json);

        designations.ShouldNotBeNull();
        designations![0].Use.ShouldBeNull();
        designations[0].Value.ShouldBe("Heart attack");
    }

    [Fact]
    public void GivenAConceptPropertyWithAValueCode_WhenParsingPropertiesJson_ThenTheTypedValueIsReturned()
    {
        const string json = """
            {
                "property": [
                    { "code": "status", "valueCode": "active" }
                ]
            }
            """;

        var (properties, _) = CreateService().ParsePropertiesJson(json);

        properties.ShouldNotBeNull();
        properties!.Count.ShouldBe(1);
        properties[0].Code.ShouldBe("status");
        properties[0].Value.ShouldBe("active");
    }

    [Fact]
    public void GivenAConceptPropertyWithAValueCoding_WhenParsingPropertiesJson_ThenTheCodingCodeIsReturned()
    {
        const string json = """
            {
                "property": [
                    { "code": "parent", "valueCoding": { "system": "http://example.org/cs", "code": "123" } }
                ]
            }
            """;

        var (properties, _) = CreateService().ParsePropertiesJson(json);

        properties.ShouldNotBeNull();
        properties![0].Value.ShouldBe("123");
    }

    [Fact]
    public void GivenAConceptPropertyWithAValueBoolean_WhenParsingPropertiesJson_ThenTheBooleanIsReturnedAsAString()
    {
        const string json = """
            {
                "property": [
                    { "code": "inactive", "valueBoolean": true }
                ]
            }
            """;

        var (properties, _) = CreateService().ParsePropertiesJson(json);

        properties.ShouldNotBeNull();
        properties![0].Value.ShouldBe("True");
    }
}
