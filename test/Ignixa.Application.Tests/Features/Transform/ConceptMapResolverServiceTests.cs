// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Transform;

public class ConceptMapResolverServiceTests
{
    private readonly ITerminologyService _mockTerminologyService;
    private readonly ConceptMapResolverService _service;

    public ConceptMapResolverServiceTests()
    {
        _mockTerminologyService = Substitute.For<ITerminologyService>();
        _service = new ConceptMapResolverService(_mockTerminologyService, NullLogger<ConceptMapResolverService>.Instance);
    }

    #region GivenValidMapping_WhenTranslate_ThenReturnsTargetCode

    [Fact]
    public async Task GivenValidMapping_WhenTranslate_ThenReturnsTargetCode()
    {
        // Arrange
        const string sourceCode = "male";
        const string sourceSystem = "http://hl7.org/fhir/administrative-gender";
        const string mapUrl = "http://example.org/ConceptMap/gender-map";
        const string targetSystem = null;

        var translateResult = new TranslateResult(
            Result: true,
            Message: "Found 1 match",
            Matches: new List<TranslateMatch>
            {
                new TranslateMatch(
                    Equivalence: "equivalent",
                    Concept: new TranslateConcept(
                        System: "http://example.org/custom-gender",
                        Code: "M",
                        Display: "Male"),
                    Source: mapUrl,
                    Comment: null)
            });

        _mockTerminologyService.TranslateCodeAsync(
            Arg.Is<TranslateParameters>(p =>
                p.Code == sourceCode &&
                p.System == sourceSystem &&
                p.Url == mapUrl &&
                p.TargetSystem == targetSystem),
            Arg.Any<CancellationToken>())
            .Returns(translateResult);

        // Act
        var result = await _service.TranslateAsync(sourceCode, sourceSystem, mapUrl, targetSystem, CancellationToken.None);

        // Assert
        result.ShouldBe("M");
    }

    #endregion

    #region GivenMapNotFound_WhenTranslate_ThenReturnsNull

    [Fact]
    public async Task GivenMapNotFound_WhenTranslate_ThenReturnsNull()
    {
        // Arrange
        const string sourceCode = "male";
        const string sourceSystem = "http://hl7.org/fhir/administrative-gender";
        const string mapUrl = "http://example.org/ConceptMap/not-found";

        var translateResult = new TranslateResult(
            Result: false,
            Message: "No matches found",
            Matches: []);

        _mockTerminologyService.TranslateCodeAsync(
            Arg.Any<TranslateParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(translateResult);

        // Act
        var result = await _service.TranslateAsync(sourceCode, sourceSystem, mapUrl, null, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region GivenCodeNotInMap_WhenTranslate_ThenReturnsNull

    [Fact]
    public async Task GivenCodeNotInMap_WhenTranslate_ThenReturnsNull()
    {
        // Arrange
        const string sourceCode = "other";
        const string sourceSystem = "http://hl7.org/fhir/administrative-gender";
        const string mapUrl = "http://example.org/ConceptMap/gender-map";

        var translateResult = new TranslateResult(
            Result: false,
            Message: "No translation found",
            Matches: []);

        _mockTerminologyService.TranslateCodeAsync(
            Arg.Any<TranslateParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(translateResult);

        // Act
        var result = await _service.TranslateAsync(sourceCode, sourceSystem, mapUrl, null, CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region GivenTargetSystemFilter_WhenTranslate_ThenFiltersCorrectly

    [Fact]
    public async Task GivenTargetSystemFilter_WhenTranslate_ThenFiltersCorrectly()
    {
        // Arrange
        const string sourceCode = "male";
        const string sourceSystem = "http://hl7.org/fhir/administrative-gender";
        const string mapUrl = "http://example.org/ConceptMap/gender-map";
        const string targetSystem = "http://example.org/custom-gender";

        var translateResult = new TranslateResult(
            Result: true,
            Message: "Found 1 match",
            Matches: new List<TranslateMatch>
            {
                new TranslateMatch(
                    Equivalence: "equivalent",
                    Concept: new TranslateConcept(
                        System: targetSystem,
                        Code: "M",
                        Display: "Male"),
                    Source: mapUrl,
                    Comment: null)
            });

        _mockTerminologyService.TranslateCodeAsync(
            Arg.Is<TranslateParameters>(p =>
                p.Code == sourceCode &&
                p.System == sourceSystem &&
                p.Url == mapUrl &&
                p.TargetSystem == targetSystem),
            Arg.Any<CancellationToken>())
            .Returns(translateResult);

        // Act
        var result = await _service.TranslateAsync(sourceCode, sourceSystem, mapUrl, targetSystem, CancellationToken.None);

        // Assert
        result.ShouldBe("M");

        // Verify that ITerminologyService was called with correct targetSystem filter
        await _mockTerminologyService.Received(1).TranslateCodeAsync(
            Arg.Is<TranslateParameters>(p => p.TargetSystem == targetSystem),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GivenSynchronousCall_WhenTranslate_ThenWorks

    [Fact]
    public void GivenSynchronousCall_WhenTranslate_ThenWorks()
    {
        // Arrange
        const string sourceCode = "male";
        const string sourceSystem = "http://hl7.org/fhir/administrative-gender";
        const string mapUrl = "http://example.org/ConceptMap/gender-map";

        var translateResult = new TranslateResult(
            Result: true,
            Message: "Found 1 match",
            Matches: new List<TranslateMatch>
            {
                new TranslateMatch(
                    Equivalence: "equivalent",
                    Concept: new TranslateConcept(
                        System: "http://example.org/custom-gender",
                        Code: "M",
                        Display: null),
                    Source: mapUrl,
                    Comment: null)
            });

        _mockTerminologyService.TranslateCodeAsync(
            Arg.Any<TranslateParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(translateResult);

        // Act
        var result = _service.Translate(sourceCode, sourceSystem, mapUrl, null);

        // Assert
        result.ShouldBe("M");
    }

    #endregion

    #region GivenException_WhenTranslate_ThenThrowsInvalidOperationException

    [Fact]
    public async Task GivenException_WhenTranslate_ThenThrowsInvalidOperationException()
    {
        // Arrange
        const string sourceCode = "male";
        const string sourceSystem = "http://hl7.org/fhir/administrative-gender";
        const string mapUrl = "http://example.org/ConceptMap/broken";

        _mockTerminologyService.TranslateCodeAsync(
            Arg.Any<TranslateParameters>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TranslateResult>(new Exception("Database connection failed")));

        // Act
        var act = async () => await _service.TranslateAsync(sourceCode, sourceSystem, mapUrl, null, CancellationToken.None);

        // Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(act);
        ex.Message.ShouldStartWith($"ConceptMap translation failed for '{sourceSystem}#{sourceCode}' using map '{mapUrl}':");
    }

    #endregion
}
