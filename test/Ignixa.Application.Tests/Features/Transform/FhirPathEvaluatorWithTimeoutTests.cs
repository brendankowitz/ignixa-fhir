// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Tests.TestDoubles;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Transform;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.Tests.Features.Transform;

/// <summary>
/// Covers what <see cref="FhirPathEvaluatorWithTimeout"/> guarantees: successful evaluation, a time limit that stops
/// an expression while it runs (#531), cancellation, and evaluation-time errors surfacing from the call rather than
/// from a later enumeration.
/// </summary>
public class FhirPathEvaluatorWithTimeoutTests
{
    /// <summary>
    /// Nine nested <c>select()</c>s over ten items: 10^9 projections of pure CPU and flat memory, the shape no size
    /// limit can see. Only the clock or the step budget stops it.
    /// </summary>
    private const string CrossProduct =
        "(1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10).select(" +
        "(1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10).select(" +
        "(1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10))))))))).count()";

    [Fact]
    public void GivenSimpleExpression_WhenEvaluate_ThenSucceeds()
    {
        // Arrange
        var evaluatorWithTimeout = CreateEvaluator(TimeSpan.FromSeconds(30));
        var element = new TestElement("Patient", "Patient");

        // Act
        var result = evaluatorWithTimeout.Evaluate("Patient", element, CancellationToken.None);

        // Assert
        result.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAnUnboundedCrossProduct_WhenEvaluateUnderAShortTimeout_ThenItStopsWithAResourceLimitAtTheTimeout()
    {
        // Arrange - regression for #531: the class used to time only the queueing of a Task.Run, so a runaway
        // expression ran to completion whatever the timeout said.
        var evaluatorWithTimeout = CreateEvaluator(TimeSpan.FromMilliseconds(50));
        var element = new TestElement("Patient", "Patient");

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(
            () => evaluatorWithTimeout.Evaluate(CrossProduct, element, CancellationToken.None));

        // Assert
        exception.Message.ShouldContain("time limit");
    }

    [Fact]
    public void GivenACancelledToken_WhenEvaluate_ThenThrowsOperationCanceledException()
    {
        // Arrange
        var evaluatorWithTimeout = CreateEvaluator(TimeSpan.FromSeconds(30));
        var element = new TestElement("Patient", "Patient");

        // Act & Assert
        Should.Throw<OperationCanceledException>(
            () => evaluatorWithTimeout.Evaluate("Patient", element, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void GivenATokenCancelledDuringEvaluation_WhenEvaluate_ThenTheRunningExpressionStops()
    {
        // Arrange - regression for #531: the token used to be observed only before evaluation started.
        var evaluatorWithTimeout = CreateEvaluator(TimeSpan.FromSeconds(30));
        var element = new TestElement("Patient", "Patient");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        Should.Throw<OperationCanceledException>(
            () => evaluatorWithTimeout.Evaluate(CrossProduct, element, cancellation.Token));
    }

    [Fact]
    public void GivenANonPositiveTimeout_WhenConstructing_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => CreateEvaluator(TimeSpan.Zero));
    }

    [Fact]
    public void GivenNullCache_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var act = () => new FhirPathEvaluatorWithTimeout(
            null!,
            new FhirPathEvaluator(),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance,
            TimeSpan.FromSeconds(30));

        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("expressionCache");
    }

    [Fact]
    public void GivenNullEvaluator_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Arrange
        var cache = new FhirPathExpressionCache(new FhirPathParser(), NullLogger<FhirPathExpressionCache>.Instance);

        // Act & Assert
        var act = () => new FhirPathEvaluatorWithTimeout(
            cache,
            null!,
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance,
            TimeSpan.FromSeconds(30));

        Should.Throw<ArgumentNullException>(act).ParamName.ShouldBe("evaluator");
    }

    [Fact]
    public void GivenAnExpressionThatThrowsDuringEvaluation_WhenEvaluate_ThenTheCallThrowsRatherThanTheEnumeration()
    {
        // Arrange: '&' is a singleton operator, so a three-item left operand is an evaluation-time
        // error. The expression parses cleanly, so nothing fails before evaluation starts.
        var evaluatorWithTimeout = CreateEvaluator(TimeSpan.FromSeconds(30));
        var element = new TestElement("Patient", "Patient");

        // Act & Assert: FhirPathEvaluator.Evaluate returns a lazy sequence, so an unmaterialised result would
        // throw on first enumeration - outside the time limit and every catch in Evaluate.
        Should.Throw<FhirPathEvaluationException>(
            () => evaluatorWithTimeout.Evaluate("(1 | 2 | 3) & 'b'", element, CancellationToken.None));
    }

    private static FhirPathEvaluatorWithTimeout CreateEvaluator(TimeSpan timeout) =>
        new(
            new FhirPathExpressionCache(new FhirPathParser(), NullLogger<FhirPathExpressionCache>.Instance),
            new FhirPathEvaluator(),
            NullLogger<FhirPathEvaluatorWithTimeout>.Instance,
            timeout);

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

    [Fact]
    public void GivenFailingExpressionWithLineBreaksAndLongText_WhenEvaluate_ThenLogCarriesSingleLineTruncatedText()
    {
        // Arrange
        var logger = new CapturingLogger<FhirPathEvaluatorWithTimeout>();
        var evaluator = new FhirPathEvaluatorWithTimeout(
            new FhirPathExpressionCache(new FhirPathParser(), NullLogger<FhirPathExpressionCache>.Instance),
            new FhirPathEvaluator(),
            logger,
            TimeSpan.FromSeconds(30));
        var expression = "(1 | 2 | 3)\r\n& '" + new string('x', 5000) + "'";

        // Act
        Should.Throw<Exception>(() => evaluator.Evaluate(expression, new TestElement("Patient", "Patient"), CancellationToken.None));

        // Assert
        logger.Messages.ShouldNotBeEmpty();
        logger.Messages.ShouldAllBe(m => m.Length < 600 && !m.Contains('\r') && !m.Contains('\n'));
    }
}
