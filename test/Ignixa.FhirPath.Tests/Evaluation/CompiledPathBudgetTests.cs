// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// <see cref="TypedElementExtensions.Select"/> runs expressions the delegate compiler accepts as compiled delegates.
/// Each call meters its own evaluation under the context's limits and token, as the interpreter does.
/// </summary>
public class CompiledPathBudgetTests
{
    // 'where(... = ...)' is within the delegate compiler's forms; comparing two 20,000-character Strings charges
    // the budget for the text both sides read.
    private const string CompiledComparison = "component.where(code.text = code.text)";

    private static readonly IElement Observation = ResourceJsonNode
        .Parse("{\"resourceType\":\"Observation\",\"id\":\"o\",\"status\":\"final\",\"code\":{\"text\":\"x\"},\"component\":[{\"code\":{\"text\":\"" + new string('a', 20_000) + "\"}}]}")
        .ToElement(FhirVersion.R4.GetSchemaProvider());

    // Plain navigation, which the delegate compiler accepts; 5,000 children cross several cancellation checks.
    private const string CompiledNavigation = "name.given";

    private static readonly FhirPathDelegateCompiler Compiler = new(new FhirPathEvaluator());

    private static readonly IElement PatientWithManyGivenNames = ResourceJsonNode
        .Parse("{\"resourceType\":\"Patient\",\"id\":\"p\",\"name\":[{\"given\":["
            + string.Join(",", Enumerable.Range(0, 5_000).Select(i => $"\"g{i}\""))
            + "]}]}")
        .ToElement(FhirVersion.R4.GetSchemaProvider());

    [Fact]
    public void GivenOneContextReusedForManyCompiledEvaluations_WhenEachIsCheap_ThenNoneCarriesAnotherCallsCount()
    {
        // Arrange
        var context = new EvaluationContext();

        // Act & Assert: together the calls charge far more than one evaluation's budget, each alone very little.
        for (var call = 0; call < 10_000; call++)
        {
            Observation.Select(CompiledComparison, context).Count().ShouldBe(1);
        }
    }

    [Fact]
    public void GivenAContextWithALowStepLimit_WhenACompiledExpressionExceedsIt_ThenTheContextsLimitApplies()
    {
        // Arrange
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 100 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => Observation.Select(CompiledComparison, context).ToList());
    }

    [Fact]
    public void GivenALowStepLimit_WhenACompiledPathNavigatesMoreChildrenThanItAllows_ThenTheLimitApplies()
    {
        // Arrange: the interpreter charges a step per navigated child, so the compiled spelling must too.
        Compiler.TryCompile(new FhirPathParser().Parse(CompiledNavigation)).ShouldNotBeNull();
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 100 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => PatientWithManyGivenNames.Select(CompiledNavigation, context).ToList());
    }

    [Fact]
    public void GivenACompiledPathOverManyChildren_WhenCancelledMidEnumeration_ThenEnumerationStops()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var results = PatientWithManyGivenNames.Select(CompiledNavigation, new EvaluationContext { CancellationToken = cancellation.Token });

        // Act
        cancellation.Cancel();

        // Assert
        Should.Throw<OperationCanceledException>(() => results.ToList());
    }

    [Fact]
    public void GivenOneContextSharedAcrossThreads_WhenEvaluatingCompiledExpressions_ThenEveryCallAnswers()
    {
        // Arrange
        var context = new EvaluationContext();

        // Act
        var counts = Enumerable.Range(0, 2_000)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => Observation.Select(CompiledComparison, context).Count())
            .ToList();

        // Assert
        counts.ShouldAllBe(count => count == 1);
    }
}
