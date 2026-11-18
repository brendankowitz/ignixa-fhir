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
using Ignixa.FhirPath.Tests.Evaluation;
using Ignixa.FhirPath.Tests.TestHelpers;
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
/// The over-limit inputs are deliberately only a little over the default limits, below the depths that
/// crashed the process before the fix: the same inputs, parsed by the code before the fix, are accepted
/// rather than crashing the host. (This suite itself does not compile there, because it uses the
/// <see cref="CompilationOptions"/> limits it pins.) The process-killing sizes are proven out of process.
/// </para>
/// </remarks>
public class ExpressionSizeLimitTests
{
    private const int OverNesting = CompilationOptions.DefaultMaxNestingDepth + 4;
    private const int OverChain = CompilationOptions.DefaultMaxExpressionDepth + 44;
    private const int Nesting = CompilationOptions.DefaultMaxNestingDepth;

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

    public static TheoryData<string, string, int> AtLimitShapes => new()
    {
        { "nested parentheses", Repeat("(", Nesting) + "1" + Repeat(")", Nesting), 1 },
        { "nested function arguments", Repeat("iif(true,", Nesting) + "1" + Repeat(",2)", Nesting), 1 },
        { "nested select", "name.select(" + Repeat("select(", Nesting - 1) + "true" + Repeat(")", Nesting), 1 },
        { "nested instance selectors", NestedExtensions(Nesting), 1 },
        { "alternating function arguments and instance selectors", AlternatingIifAndExtensions(Nesting), 1 },
        { "binary-operator chain at the depth limit", "1" + Repeat("+1", CompilationOptions.DefaultMaxExpressionDepth - 1), 1 },
    };

    [Theory]
    [MemberData(nameof(AtLimitShapes))]
    public void GivenAnExpressionAtTheDefaultLimits_WhenParsedAndEvaluatedOnAOneMegabyteStack_ThenItSucceeds(
        string shape,
        string expression,
        int expectedCount)
    {
        // Arrange: a limit only protects the process if everything it admits also runs, so the worst shapes
        // measured for stack use per bracket level are parsed and evaluated on a 1 MB stack - smaller than
        // the 1.5 MB thread-pool default - right at the limit.
        var subject = ResourceJsonNode.Parse("""{"resourceType":"Patient","name":[{"family":"x"}]}""")
            .ToElement(FhirVersion.R4.GetSchemaProvider());

        // Act
        var results = StackSizedThread.Run(
            () =>
            {
                var parsed = new FhirPathParser().Parse(expression);
                return new FhirPathEvaluator().Evaluate(subject, parsed, InstanceCreationTestContext.For(subject)).ToList();
            },
            maxStackSize: 1024 * 1024);

        // Assert
        results.Count.ShouldBe(expectedCount, shape);
    }

    [Fact]
    public void GivenNestingBeyondTheThreadStack_WhenParsedWithTheNestingLimitRaised_ThenParseThrowsFormatException()
    {
        // Arrange: with the pre-scan disabled, the grammar's own stack check is all that stands between this
        // input and a stack overflow that would kill the test host.
        var parser = new FhirPathParser(new CompilationOptions { MaxNestingDepth = int.MaxValue });
        var expression = Repeat("iif(true,", 250) + "1" + Repeat(",2)", 250);

        // Act
        var exception = Should.Throw<FormatException>(
            () => StackSizedThread.Run(() => parser.Parse(expression), maxStackSize: 1024 * 1024));

        // Assert
        exception.InnerException.ShouldBeOfType<InsufficientExecutionStackException>();
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
        // Arrange
        Func<CompilationOptions>[] constructions =
        [
            () => new CompilationOptions { MaxExpressionLength = limit },
            () => new CompilationOptions { MaxNestingDepth = limit },
            () => new CompilationOptions { MaxExpressionDepth = limit },
        ];

        // Act
        var failures = constructions.Select(construct => Record.Exception(construct)).ToList();

        // Assert
        failures.ShouldAllBe(failure => failure is ArgumentOutOfRangeException);
    }

    // Extension.extension is itself an Extension, so instance selectors can nest to any depth.
    private static string NestedExtensions(int depth) =>
        Repeat("Extension{extension: ", depth - 1) + "Extension{url: 'u'}" + Repeat("}", depth - 1);

    private static string AlternatingIifAndExtensions(int depth)
    {
        var open = new StringBuilder();
        var close = new StringBuilder();
        for (var level = 0; level < depth - 1; level++)
        {
            var isIif = level % 2 == 0;
            open.Append(isIif ? "iif(true, " : "Extension{extension: ");
            close.Insert(0, isIif ? ", {})" : "}");
        }

        return open + "Extension{url: 'u'}" + close;
    }

    private static string Repeat(string value, int count) => new StringBuilder(value.Length * count).Insert(0, value, count).ToString();
}
