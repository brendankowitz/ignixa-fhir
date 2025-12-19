// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.Configuration;
using Shouldly;

namespace Ignixa.Application.Features.Experimental.Tests.Configuration;

public class ExperimentalOptionsTests
{
    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenEnabledByDefault()
    {
        // Arrange & Act
        var options = new ExperimentalOptions();

        // Assert
        options.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenAllFeaturesEnabledByDefault()
    {
        // Arrange & Act
        var options = new ExperimentalOptions();

        // Assert
        options.Features.Mcp.Enabled.ShouldBeTrue();
        options.Features.Transform.Enabled.ShouldBeTrue();
        options.Features.Terminology.Enabled.ShouldBeTrue();
        options.Features.Summary.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenSummaryEnabledByDefault()
    {
        // Arrange & Act
        var options = new ExperimentalOptions();

        // Assert - Summary (IPS) is now implemented and enabled by default
        options.Features.Summary.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void GivenMcpOptions_WhenCreated_ThenDefaultTransportIsHttp()
    {
        // Arrange & Act
        var options = new McpExperimentalOptions();

        // Assert
        options.Transport.ShouldBe("http");
    }

    [Fact]
    public void GivenTransformOptions_WhenCreated_ThenDefaultTimeoutIs30Seconds()
    {
        // Arrange & Act
        var options = new TransformExperimentalOptions();

        // Assert
        options.TimeoutSeconds.ShouldBe(30);
    }

    [Fact]
    public void GivenTerminologyOptions_WhenCreated_ThenAutoImportDisabledByDefault()
    {
        // Arrange & Act
        var options = new TerminologyExperimentalOptions();

        // Assert
        options.EnableAutoImport.ShouldBeFalse();
    }

    [Fact]
    public void GivenSummaryOptions_WhenCreated_ThenMaxResourcesIs1000()
    {
        // Arrange & Act
        var options = new SummaryExperimentalOptions();

        // Assert
        options.MaxResources.ShouldBe(1000);
    }

    [Fact]
    public void GivenSummaryOptions_WhenCreated_ThenAllowedResourceTypesIsEmpty()
    {
        // Arrange & Act
        var options = new SummaryExperimentalOptions();

        // Assert
        options.AllowedResourceTypes.ShouldBeEmpty();
        options.AllowedResourceTypes.ShouldBeAssignableTo<ICollection<string>>();
    }

    [Fact]
    public void GivenSectionName_WhenAccessed_ThenReturnsExpectedValue()
    {
        // Assert
        ExperimentalOptions.SectionName.ShouldBe("Experimental");
    }
}
