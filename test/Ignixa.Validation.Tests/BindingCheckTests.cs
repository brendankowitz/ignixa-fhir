// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using NSubstitute;
using System.IO;
using System.Text;
using Xunit;

namespace Ignixa.Validation.Tests;

public class BindingCheckTests
{
    [Fact]
    public async Task ExtensibleBinding_SpecDepth_SkipsWarning()
    {
        // Arrange
        var terminology = Substitute.For<ITerminologyService>();
        terminology.ValidateBindingAsync(
                Arg.Any<string>(),
                Arg.Any<BindingStrength>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BindingValidationResult(
                IsValid: true,
                Strength: BindingStrength.Extensible,
                Severity: IssueSeverity.Warning,
                Message: "not in ValueSet",
                SuggestedDisplay: null));

        var bindingCheck = new BindingCheck(
            elementPath: "gender",
            valueSetUrl: "http://example.org/vs",
            bindingStrength: "Extensible",
            terminologyService: terminology);

        var json = """
        {
          "resourceType":"Patient",
          "gender":"custom"
        }
        """;
        var node = (await JsonSourceNodeFactory.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)))).ToSourceNavigator();

        var settings = new ValidationSettings
        {
            Depth = ValidationDepth.Spec,
            TerminologyService = terminology
        };

        // Act
        var result = bindingCheck.Validate(node.ToElement(TestSchemaProvider.GetR4Schema()), settings, new ValidationState());

        // Assert
        result.IsValid.Should().BeTrue("extensible bindings are skipped in Spec depth");
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtensibleBinding_FullDepth_ReturnsWarning()
    {
        // Arrange
        var terminology = Substitute.For<ITerminologyService>();
        terminology.ValidateBindingAsync(
                Arg.Any<string>(),
                Arg.Any<BindingStrength>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BindingValidationResult(
                IsValid: true,
                Strength: BindingStrength.Extensible,
                Severity: IssueSeverity.Warning,
                Message: "not in ValueSet",
                SuggestedDisplay: null));

        var bindingCheck = new BindingCheck(
            elementPath: "gender",
            valueSetUrl: "http://example.org/vs",
            bindingStrength: "Extensible",
            terminologyService: terminology);

        var json = """
        {
          "resourceType":"Patient",
          "gender":"custom"
        }
        """;
        var node = (await JsonSourceNodeFactory.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)))).ToSourceNavigator();

        var settings = new ValidationSettings
        {
            Depth = ValidationDepth.Full,
            TerminologyService = terminology
        };

        // Act
        var result = bindingCheck.Validate(node.ToElement(TestSchemaProvider.GetR4Schema()), settings, new ValidationState());

        // Assert
        result.IsValid.Should().BeTrue("extensible bindings in Full depth should warn, not fail");
        result.Issues.Should().ContainSingle(i => i.Severity == IssueSeverity.Warning);
    }
}
