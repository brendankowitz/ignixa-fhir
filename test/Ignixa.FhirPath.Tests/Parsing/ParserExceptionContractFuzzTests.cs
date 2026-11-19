/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins FhirPathParser.Parse's documented failure contract against generated input (#537).
 */

using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// <see cref="FhirPathParser.Parse"/> documents <see cref="FormatException"/> as its failure, and callers
/// classify by exception type, so no other type may escape for any input.
/// </summary>
/// <remarks>
/// The seed is fixed so a failure reproduces; the count is bounded to keep the suite fast. Token soup
/// reaches the literal and escape rules because its vocabulary includes out-of-range numbers and surrogate
/// escapes. <see cref="ArgumentException"/> is the documented outcome for a blank expression.
/// </remarks>
public class ParserExceptionContractFuzzTests
{
    private const int Seed = 20260101;
    private const int Iterations = 4000;

    [Fact]
    public void GivenGeneratedExpressions_WhenParsed_ThenOnlyDocumentedExceptionsEscape()
    {
        // Arrange
        var generator = new FuzzExpressionGenerator(Seed);
        var parser = new FhirPathParser();
        var violations = new List<string>();

        // Act
        for (var i = 0; i < Iterations; i++)
        {
            var expression = (i % 3) switch
            {
                0 => generator.TokenSoup(),
                1 => generator.MutatedSeed(),
                _ => generator.MutatedSoup(),
            };

            try
            {
                parser.Parse(expression);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Documented failure: the expression is rejected, which is what this test allows.
            }
            catch (Exception ex)
            {
                violations.Add($"{ex.GetType().Name} for: {expression}");
            }
        }

        // Assert
        violations.ShouldBeEmpty("Parse may throw only FormatException or ArgumentException.");
    }
}
