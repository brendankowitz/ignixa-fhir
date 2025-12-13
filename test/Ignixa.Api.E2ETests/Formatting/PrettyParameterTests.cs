// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Serialization;

namespace Ignixa.Api.E2ETests.Formatting;

/// <summary>
/// E2E tests for the _pretty parameter support in FHIR endpoints.
/// Tests verify that JSON formatting is correctly controlled by the _pretty parameter.
/// </summary>
public class PrettyParameterE2ETests : CapabilityDrivenTestBase
{
    public PrettyParameterE2ETests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyTrue_ThenReturnsIndentedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata?_pretty=true");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Indented JSON should contain newlines and multiple spaces (indentation)
        content.Should().Contain("\n", "indented JSON should contain newlines");
        content.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithoutPrettyParameter_ThenReturnsMinifiedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Minified JSON should not contain newlines (except possibly in string values)
        // Check that the first 1000 characters don't have newlines (sufficient to verify minification)
        var sample = content.Length > 1000 ? content[..1000] : content;
        sample.Should().NotContain("\n", "minified JSON should not contain newlines");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyNoValue_ThenReturnsIndentedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act - just ?_pretty with no value (FHIR spec: presence implies true)
        var response = await Client.GetAsync("/metadata?_pretty");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Indented JSON should contain newlines and multiple spaces (indentation)
        content.Should().Contain("\n", "FHIR spec says ?_pretty without value should return indented JSON");
        content.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyFalse_ThenReturnsMinifiedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata?_pretty=false");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Minified JSON should not contain newlines
        var sample = content.Length > 1000 ? content[..1000] : content;
        sample.Should().NotContain("\n", "minified JSON should not contain newlines when _pretty=false");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyUpperCaseTrue_ThenReturnsIndentedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata?_pretty=TRUE");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Indented JSON should contain newlines and multiple spaces (indentation)
        content.Should().Contain("\n", "case-insensitive TRUE should return indented JSON");
        content.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyOne_ThenReturnsIndentedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata?_pretty=1");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Indented JSON should contain newlines and multiple spaces (indentation)
        content.Should().Contain("\n", "_pretty=1 should return indented JSON");
        content.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyZero_ThenReturnsMinifiedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata?_pretty=0");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Minified JSON should not contain newlines
        var sample = content.Length > 1000 ? content[..1000] : content;
        sample.Should().NotContain("\n", "_pretty=0 should return minified JSON");
    }

    [Fact]
    public async Task GivenMetadataEndpoint_WhenRequestedWithPrettyInvalidValue_ThenReturnsMinifiedJson()
    {
        // Arrange - no setup needed for /metadata

        // Act
        var response = await Client.GetAsync("/metadata?_pretty=invalid");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Minified JSON should not contain newlines (invalid values default to false)
        var sample = content.Length > 1000 ? content[..1000] : content;
        sample.Should().NotContain("\n", "invalid _pretty value should default to minified JSON");
    }

    [Fact]
    public async Task GivenStoredResource_WhenRequestedWithPrettyTrue_ThenReturnsIndentedJson()
    {
        // Arrange - Create a resource first (stores minified bytes in database)
        var patient = CreatePatient().WithTag(Guid.NewGuid().ToString()).Build();
        var created = await Harness.CreateResourceAsync(patient);

        // Act - GET with _pretty=true (should deserialize stored bytes and re-serialize with formatting)
        var response = await Client.GetAsync($"/Patient/{created.Id}?_pretty=true");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        content.Should().Contain("\n", "stored resource with _pretty=true should return indented JSON");
        content.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }

    [Fact]
    public async Task GivenStoredResource_WhenRequestedWithoutPretty_ThenReturnsMinifiedJson()
    {
        // Arrange - Create a resource first
        var patient = CreatePatient().WithTag(Guid.NewGuid().ToString()).Build();
        var created = await Harness.CreateResourceAsync(patient);

        // Act - GET without _pretty (should return stored bytes as-is, fast path)
        var response = await Client.GetAsync($"/Patient/{created.Id}");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        var sample = content.Length > 1000 ? content[..1000] : content;
        sample.Should().NotContain("\n", "stored resource without _pretty should return minified JSON");
    }

    [Fact]
    public async Task GivenStoredResource_WhenCreatedWithPrettyTrue_ThenReturnsIndentedJson()
    {
        // Arrange
        var patient = CreatePatient().WithTag(Guid.NewGuid().ToString()).Build();
        var json = patient.SerializeToString();

        // Act - POST with _pretty=true (should return formatted response)
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");
        var response = await Client.PostAsync("/Patient?_pretty=true", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        responseContent.Should().Contain("\n", "POST with _pretty=true should return indented JSON");
        responseContent.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }

    [Fact]
    public async Task GivenStoredResource_WhenUpdatedWithPrettyTrue_ThenReturnsIndentedJson()
    {
        // Arrange - Create a resource first
        var patient = CreatePatient().WithTag(Guid.NewGuid().ToString()).Build();
        var created = await Harness.CreateResourceAsync(patient);

        // Act - PUT with _pretty=true
        created.MutableNode["active"] = true;
        var json = created.SerializeToString();
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+json");
        var response = await Client.PutAsync($"/Patient/{created.Id}?_pretty=true", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        responseContent.Should().Contain("\n", "PUT with _pretty=true should return indented JSON");
        responseContent.Should().MatchRegex(@"\s{2,}", "indented JSON should contain multi-space indentation");
    }
}
