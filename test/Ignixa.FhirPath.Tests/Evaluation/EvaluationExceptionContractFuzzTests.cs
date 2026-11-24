/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins the evaluator's failure contract against generated expressions (#537).
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Evaluating a parsed expression over repeating data may fail only with
/// <see cref="FhirPathEvaluationException"/> (or a subclass), <see cref="FhirPathFunctionNotSupportedException"/>
/// or <see cref="NotSupportedException"/>. Hosts classify by type: anything else is read as an engine defect.
/// </summary>
/// <remarks>
/// The seed is fixed so a failure reproduces; the count is bounded to keep the suite fast.
/// </remarks>
public class EvaluationExceptionContractFuzzTests
{
    private const int Seed = 7777;
    private const int Iterations = 1200;

    [Fact]
    public void GivenGeneratedExpressionsOverRepeatingData_WhenEvaluated_ThenOnlyDocumentedExceptionsEscape()
    {
        // Arrange
        var generator = new FuzzExpressionGenerator(Seed);
        var parser = new FhirPathParser();
        var evaluator = new FhirPathEvaluator();
        var patient = FuzzPatient.Create();
        var context = new EvaluationContext { Resource = patient, RootResource = patient, Schema = FuzzPatient.Schema };
        var violations = new List<string>();
        var evaluated = 0;

        // Act
        for (var i = 0; i < Iterations; i++)
        {
            var expression = i % 2 == 0 ? generator.Chain(1 + (i % 3)) : generator.TokenSoup();
            if (!parser.TryParse(expression, out var ast, out _))
            {
                continue;
            }

            try
            {
                _ = evaluator.Evaluate(patient, ast!, context).Count();
                evaluated++;
            }
            catch (Exception ex) when (ex is FhirPathEvaluationException
                or FhirPathFunctionNotSupportedException
                or NotSupportedException)
            {
                // Documented failure: the expression is rejected, which is what this test allows.
            }
            catch (Exception ex)
            {
                violations.Add($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]} for: {expression}");
            }
        }

        // Assert
        violations.ShouldBeEmpty("Evaluation may fail only with the documented exception types.");
        evaluated.ShouldBeGreaterThan(Iterations / 10, "the generator must produce mostly evaluable expressions.");
    }
}
