/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins that the analyzer surfaces stack exhaustion instead of returning partial types (issue #530).
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;
using Ignixa.FhirPath.Tests.TestHelpers;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// <see cref="FhirPathAnalyzer.Analyze(Expressions.Expression, string)"/> does not swallow running out of stack.
/// </summary>
/// <remarks>
/// Its type-collection pass otherwise ignores failures to return partial types; doing that for stack
/// exhaustion would report a tree the thread cannot walk as merely under-typed. The tree is deeper than the
/// parser's default limits allow, built with the limits raised, because default-configured callers never
/// reach this path.
/// </remarks>
public class AnalyzerStackExhaustionTests
{
    private const int ChainLength = 20_000;

    [Fact]
    public void GivenATreeTooDeepForTheThreadStack_WhenAnalyzing_ThenInsufficientExecutionStackPropagates()
    {
        // Arrange
        var parser = new FhirPathParser(new CompilationOptions
        {
            MaxExpressionLength = int.MaxValue,
            MaxExpressionDepth = int.MaxValue,
        });
        var chain = "name" + string.Concat(Enumerable.Repeat(".family", ChainLength));
        var ast = StackSizedThread.Run(() => parser.Parse(chain), maxStackSize: 256 * 1024 * 1024);
        var analyzer = new FhirPathAnalyzer(FhirVersion.R4.GetSchemaProvider());

        // Act & Assert
        Should.Throw<InsufficientExecutionStackException>(
            () => StackSizedThread.Run(() => analyzer.Analyze(ast, "Patient"), maxStackSize: 1024 * 1024));
    }
}
