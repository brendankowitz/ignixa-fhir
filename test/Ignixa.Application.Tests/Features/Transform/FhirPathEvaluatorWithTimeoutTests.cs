// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Application.Operations.Features.Transform;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.Tests.Features.Transform;

public class FhirPathEvaluatorWithTimeoutTests
{
    #region Successful Evaluation Tests

    [Fact]
    public async Task GivenSimpleExpression_WhenEvaluateAsync_ThenSucceeds()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var evaluator = new FhirPathEvaluator();
        var evaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            cache,
            evaluator,
            TimeSpan.FromSeconds(5),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        var element = new TestElement("Patient", "Patient");

        // Act
        var result = await evaluatorWithTimeout.EvaluateAsync("Patient", element, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(1);
    }

    [Fact]
    public void GivenSimpleExpression_WhenEvaluateSynchronously_ThenSucceeds()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var evaluator = new FhirPathEvaluator();
        var evaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            cache,
            evaluator,
            TimeSpan.FromSeconds(5),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        var element = new TestElement("Patient", "Patient");

        // Act
        var result = evaluatorWithTimeout.Evaluate("Patient", element);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(1);
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task GivenShortTimeout_WhenComplexExpression_ThenThrowsTimeoutException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var evaluator = new FhirPathEvaluator();

        // Use very short timeout to simulate timeout condition
        var evaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            cache,
            evaluator,
            TimeSpan.FromMilliseconds(1), // 1ms timeout - very aggressive
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        var element = new TestElement("Patient", "Patient");

        // Act & Assert
        // With 1ms timeout, this should reliably timeout on most systems
        var act = async () => await evaluatorWithTimeout.EvaluateAsync("Patient", element, CancellationToken.None);

        var exception = await Should.ThrowAsync<TimeoutException>(act);
        exception.Message.ShouldContain("FHIRPath expression timed out");
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task GivenCancellationToken_WhenCancelled_ThenThrowsOperationCanceledException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var evaluator = new FhirPathEvaluator();
        var evaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            cache,
            evaluator,
            TimeSpan.FromSeconds(10),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        var element = new TestElement("Patient", "Patient");
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await evaluatorWithTimeout.EvaluateAsync("Patient", element, cts.Token);
        await Should.ThrowAsync<OperationCanceledException>(act);
    }

    #endregion

    #region Constructor Validation Tests

    [Fact]
    public void GivenNullCache_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var act = () => new FhirPathEvaluatorWithTimeout(
            null!,
            new FhirPathEvaluator(),
            TimeSpan.FromSeconds(5),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("expressionCache");
    }

    [Fact]
    public void GivenNullEvaluator_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => new FhirPathEvaluatorWithTimeout(
            cache,
            null!,
            TimeSpan.FromSeconds(5),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("evaluator");
    }

    [Fact]
    public void GivenZeroTimeout_WhenConstructing_ThenThrowsArgumentException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => new FhirPathEvaluatorWithTimeout(
            cache,
            new FhirPathEvaluator(),
            TimeSpan.Zero,
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("timeout");
    }

    [Fact]
    public void GivenNegativeTimeout_WhenConstructing_ThenThrowsArgumentException()
    {
        // Arrange
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => new FhirPathEvaluatorWithTimeout(
            cache,
            new FhirPathEvaluator(),
            TimeSpan.FromSeconds(-1),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("timeout");
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Simple test implementation of IElement for unit testing.
    /// </summary>
    private class TestElement : IElement
    {
        public TestElement(string name, string instanceType)
        {
            Name = name;
            InstanceType = instanceType;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object Value => null!;
        public string Location => string.Empty;
        public IType Type => null;

        public IReadOnlyList<IElement> Children(string name = null) => [];
        public T Meta<T>() where T : class => null;
    }

    #endregion
}
