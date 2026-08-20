// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.Tests.Features.Transform;

/// <summary>
/// This class does not enforce an execution-time timeout despite its name - see the remarks on
/// <see cref="FhirPathEvaluatorWithTimeout"/> for why. These tests cover what it actually does:
/// successful evaluation, forwarding a caller's <see cref="CancellationToken"/> before evaluation
/// starts, and not losing evaluation-time errors to the lazy-enumerable trap described below.
/// </summary>
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
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        var element = new TestElement("Patient", "Patient");

        // Act
        var result = evaluatorWithTimeout.Evaluate("Patient", element);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(1);
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
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("evaluator");
    }

    #endregion

    [Fact]
    public async Task GivenAnExpressionThatThrowsDuringEvaluation_WhenEvaluateAsync_ThenTheAwaitThrowsRatherThanTheEnumeration()
    {
        // Arrange: '&' is a singleton operator, so a three-item left operand is an evaluation-time
        // error. The expression parses cleanly, so nothing fails before evaluation starts.
        var parser = new FhirPathParser();
        var cache = new FhirPathExpressionCache(parser, NullLogger<FhirPathExpressionCache>.Instance);
        var evaluatorWithTimeout = new FhirPathEvaluatorWithTimeout(
            cache,
            new FhirPathEvaluator(),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance);

        var element = new TestElement("Patient", "Patient");

        // Act & Assert: FhirPathEvaluator.Evaluate returns a lazy sequence, so this await used to
        // complete successfully and hand back a sequence that threw on first enumeration - outside the
        // Task, outside the timeout, and outside every catch in EvaluateAsync.
        await Should.ThrowAsync<FhirPathEvaluationException>(
            async () => await evaluatorWithTimeout.EvaluateAsync("(1 | 2 | 3) & 'b'", element, CancellationToken.None));
    }

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
        public IType Type => null!;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string name = null!) => [];
        public T Meta<T>() where T : class => null!;
    }

    #endregion
}
