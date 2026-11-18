/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins that the delegate compiler surfaces stack exhaustion instead of falling back (issue #530).
 */

using System.Runtime.ExceptionServices;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// <see cref="FhirPathDelegateCompiler.TryCompile"/> falls back to the interpreter when compilation fails,
/// but running out of stack is not a compilation failure.
/// </summary>
/// <remarks>
/// Falling back would hide the condition and hand the same tree to an interpreter that needs more stack,
/// not less. The tree here is deeper and longer than the parser's default limits allow, built with the
/// limits raised, because the limits exist precisely so that default-configured callers never reach this path.
/// </remarks>
public class DelegateCompilerStackExhaustionTests
{
    private const int ChainLength = 20_000;

    [Fact]
    public void GivenATreeTooDeepForTheThreadStack_WhenCompiling_ThenInsufficientExecutionStackPropagates()
    {
        // Arrange
        var parser = new FhirPathParser(new CompilationOptions
        {
            MaxExpressionLength = int.MaxValue,
            MaxExpressionDepth = int.MaxValue,
        });
        var chain = "a" + string.Concat(Enumerable.Repeat(".a", ChainLength));
        var ast = RunOnThread(() => parser.Parse(chain), maxStackSize: 256 * 1024 * 1024);
        var compiler = new FhirPathDelegateCompiler(new FhirPathEvaluator());

        // Act & Assert
        Should.Throw<InsufficientExecutionStackException>(
            () => RunOnThread(() => compiler.TryCompile(ast), maxStackSize: 1024 * 1024));
    }

    private static T RunOnThread<T>(Func<T> work, int maxStackSize)
    {
        T? result = default;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    // Re-thrown on the test thread below; an exception escaping a raw thread kills the host.
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            },
            maxStackSize);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }
}
