// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Tests.TestHelpers;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Element equality recurses into children. A host can build trees far deeper than any parsed resource, so the
/// recursion has to fail catchably rather than overflow the stack.
/// </summary>
public class DeepElementEqualityTests
{
    [Fact]
    public void GivenElementsNestedDeeperThanTheStackAllows_WhenCompared_ThenInsufficientExecutionStackIsThrown()
    {
        // Arrange
        var left = TreeElement.Chain(100_000, "x");
        var right = TreeElement.Chain(100_000, "x");

        // Act & Assert
        Should.Throw<InsufficientExecutionStackException>(
            () => StackSizedThread.Run(() => FunctionHelpers.AreElementsEqual(left, right), maxStackSize: 1024 * 1024));
    }

    [Fact]
    public void GivenModeratelyNestedElements_WhenCompared_ThenTheyCompareByValue()
    {
        // Arrange
        var left = TreeElement.Chain(200, "x");
        var same = TreeElement.Chain(200, "x");
        var different = TreeElement.Chain(200, "y");

        // Act
        var equal = StackSizedThread.Run(() => FunctionHelpers.AreElementsEqual(left, same), maxStackSize: 1024 * 1024);
        var unequal = StackSizedThread.Run(() => FunctionHelpers.AreElementsEqual(left, different), maxStackSize: 1024 * 1024);

        // Assert
        equal.ShouldBeTrue();
        unequal.ShouldBeFalse();
    }
}
