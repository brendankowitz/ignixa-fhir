/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins the parser's expression length, nesting and depth limits (issue #530).
 */

using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// The parser rejects expressions whose size would exhaust the thread stack in a later stage.
/// </summary>
/// <remarks>
/// <para>
/// Every stage after tokenizing recurses over the expression, and a .NET stack overflow terminates the
/// process rather than throwing. Bracket nesting overflows inside the Superpower grammar; long flat chains
/// (<c>a.a.a</c>, <c>1+1+1</c>) parse iteratively but overflow the analyzer or evaluator. The parser
/// therefore bounds length, bracket nesting and tree depth up front and reports a <see cref="FormatException"/>.
/// </para>
/// <para>
/// The over-limit inputs here are deliberately only a little over the default limits, below the depths that
/// crashed the process before the fix, so this suite can run against the base commit without killing the
/// test host - and fails there, because the base commit accepts them. The process-killing sizes are proven
/// out of process, not here.
/// </para>
/// </remarks>
public class ExpressionSizeLimitTests
{
    private const int OverNesting = CompilationOptions.DefaultMaxNestingDepth + 4;
    private const int OverChain = CompilationOptions.DefaultMaxExpressionDepth + 44;

    public static TheoryData<string, string> OverLimitShapes => new()
    {
        { "nested parentheses", Repeat("(", OverNesting) + "1" + Repeat(")", OverNesting) },
        { "nested function arguments", Repeat("iif(true,", OverNesting) + "1" + Repeat(",2)", OverNesting) },
        { "nested indexers", Repeat("a[", OverNesting) + "0" + Repeat("]", OverNesting) },
        { "long member chain", "a" + Repeat(".a", OverChain) },
        { "long binary-operator chain", "1" + Repeat("+1", OverChain) },
        { "long indexer chain", "a" + Repeat("[0]", OverChain) },
        { "long chained function calls", "true" + Repeat(".not()", OverChain) },
        { "over-long text", "'" + new string('x', CompilationOptions.DefaultMaxExpressionLength) + "'" },
    };

    [Theory]
    [MemberData(nameof(OverLimitShapes))]
    public void GivenAnOverLimitExpression_WhenParsed_ThenParseThrowsFormatExceptionAndTryParseReturnsFalse(
        string shape,
        string expression)
    {
        // Arrange
        var parser = new FhirPathParser();

        // Act
        var parseException = Should.Throw<FormatException>(() => parser.Parse(expression), shape);
        var parsed = parser.TryParse(expression, out var result, out var errorMessage);

        // Assert
        parseException.Message.ShouldContain("maximum", Case.Insensitive, shape);
        parsed.ShouldBeFalse(shape);
        result.ShouldBeNull(shape);
        errorMessage.ShouldBe(parseException.Message, shape);
    }

    [Fact]
    public void GivenOverNestedExpressionWithTrivia_WhenParsedPreservingTrivia_ThenParseThrowsFormatException()
    {
        // Arrange: the trivia tokenizer emits whitespace and comment tokens the pre-scan must see past.
        var parser = new FhirPathParser(CompilationOptions.Debug);
        var expression = Repeat("( /* c */ ", OverNesting) + "1" + Repeat(" )", OverNesting);

        // Act & Assert
        Should.Throw<FormatException>(() => parser.Parse(expression));
    }

    [Fact]
    public void GivenExpressionsAtEachDefaultLimit_WhenParsedAndEvaluated_ThenTheySucceed()
    {
        // Arrange: exactly at each limit, which must stay legal - and evaluable, since a limit only helps
        // if everything it admits runs without exhausting the stack.
        var nesting = CompilationOptions.DefaultMaxNestingDepth;
        var atNestingLimit = Repeat("(", nesting) + "1" + Repeat(")", nesting);
        var termsAtDepthLimit = CompilationOptions.DefaultMaxExpressionDepth;
        var atDepthLimit = "1" + Repeat("+1", termsAtDepthLimit - 1);
        var parser = new FhirPathParser();
        var subject = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""").ToElement(FhirVersion.R4.GetSchemaProvider());

        // Act
        var nestedValue = new FhirPathEvaluator().Evaluate(subject, parser.Parse(atNestingLimit)).Single().Value;
        var deepValue = new FhirPathEvaluator().Evaluate(subject, parser.Parse(atDepthLimit)).Single().Value;

        // Assert
        nestedValue.ShouldBe(1);
        deepValue.ShouldBe(termsAtDepthLimit);
    }

    [Fact]
    public void GivenConfiguredLimits_WhenParsed_ThenTheConfiguredLimitsApplyInsteadOfTheDefaults()
    {
        // Arrange
        var parser = new FhirPathParser(new CompilationOptions
        {
            MaxExpressionLength = 20,
            MaxNestingDepth = 2,
            MaxExpressionDepth = 4,
        });

        // Act & Assert
        parser.Parse("((1))").ShouldNotBeNull();
        Should.Throw<FormatException>(() => parser.Parse("(((1)))"));
        parser.Parse("a.b.c.d").ShouldNotBeNull();
        Should.Throw<FormatException>(() => parser.Parse("a.b.c.d.e"));
        Should.Throw<FormatException>(() => parser.Parse("'" + new string('x', 19) + "'"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenANonPositiveLimit_WhenConstructingOptions_ThenThrowsArgumentOutOfRange(int limit)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CompilationOptions { MaxExpressionLength = limit });
        Should.Throw<ArgumentOutOfRangeException>(() => new CompilationOptions { MaxNestingDepth = limit });
        Should.Throw<ArgumentOutOfRangeException>(() => new CompilationOptions { MaxExpressionDepth = limit });
    }

    private static string Repeat(string value, int count) => new StringBuilder(value.Length * count).Insert(0, value, count).ToString();
}
