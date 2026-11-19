// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// One test per amplification class of #531, each driven at small scale through a lowered limit so it stops in
/// milliseconds with <see cref="FhirPathResourceLimitExceededException"/> rather than exhausting memory or CPU.
/// The same shapes at their original attack sizes, against the default limits, are measured out of process.
/// </summary>
public class EvaluationBudgetTests
{
    private const string TenItems = "(1|2|3|4|5|6|7|8|9|10)";

    private const string TwentyItems = "(1|2|3|4|5|6|7|8|9|10|11|12|13|14|15|16|17|18|19|20)";

    /// <summary>Nine nested projections over ten items: 10^9 results of pure CPU and flat memory.</summary>
    private static readonly string CrossProduct =
        string.Concat(Enumerable.Repeat(TenItems + ".select(", 8)) + TenItems + new string(')', 8) + ".count()";

    private static readonly IElement Patient = ResourceJsonNode
        .Parse("""{"resourceType":"Patient","id":"p","name":[{"family":"Doe"}]}""")
        .ToElement(FhirVersion.R4.GetSchemaProvider());

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    public static TheoryData<string, string> StringAmplifiers => new()
    {
        // Geometric string growth through a self-referencing accumulator.
        { TwentyItems + ".aggregate($total & $total, 'aaaaaaaa')", "Operator '&'" },

        // The same through '+', which also concatenates Strings.
        { TwentyItems + ".aggregate($total + $total, 'aaaaaaaa')", "Operator '+'" },

        // escape('json') doubles a run of backslashes, so escaping the accumulator doubles it per item.
        { TwentyItems + ".aggregate($total.escape('json'), '\\\\')", "escape()" },

        // The same doubling through chained variable definitions.
        { DefineVariableChain(20) + ".select(%v20)", "Operator '&'" },

        // An empty pattern inserts the substitution between every character, so each nested call squares.
        { "'aaaaaaaaaa'.select($this.replace('', $this)).select($this.replace('', $this)).select($this.replace('', $this))", "replace()" },

        // The regex analogue: a pattern matching the empty string at every position.
        { "'aaaaaaaaaa'.select($this.replaceMatches('x*', $this)).select($this.replaceMatches('x*', $this))", "replaceMatches()" },

        // join() over a collection that is itself built to grow.
        { TwentyItems + ".select('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa').join('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')", "join()" },

        // encode() lengthens its input; hex doubles it.
        { "'" + new string('a', 600) + "'.encode('hex')", "encode()" },
    };

    public static TheoryData<string, string> CollectionAmplifiers => new()
    {
        // Geometric list growth: combine() doubles the accumulator per item.
        { TwentyItems + ".aggregate($total.combine($total), 1)", "combine()" },

        // repeatAll() caps dequeues, but every dequeue appends the whole projection to the result.
        { "1.repeatAll(" + TwentyItems + ")", "repeatAll()" },

        // A projection that yields a whole collection per item without evaluating a node per result.
        { "defineVariable('many', " + TwentyItems + ".select(" + TwentyItems + ")).select(%many).select(%many)", "select()" },

        // The pairing matrix behind '~' grows with the square of equal items.
        { TwentyItems + ".select('a') ~ " + TwentyItems + ".select('a')", "Operator '~'" },

        // descendants() of a collection the expression multiplied: forty copies of the resource's tree.
        { TwentyItems + ".select(%context).combine(" + TwentyItems + ".select(%context)).descendants()", "descendants()" },

        // split() and toChars() build a collection from one string.
        { "'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'.toChars()", "toChars()" },
        { "'a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a,a'.split(',')", "split()" },
    };

    /// <summary>
    /// Sixty distinct date-times, built cheaply. Temporals compare as instants and have no hash key, so every set
    /// operation over them stays pairwise - about 1,800 comparisons for sixty - and the step budget is what bounds it.
    /// </summary>
    private const string Temporals =
        $"{TwentyItems}.select(('2020-01-01T00:00:' & ($this + 10).toString()).toDateTime())" +
        $".combine({TwentyItems}.select(('2020-01-01T00:00:' & ($this + 30).toString()).toDateTime()))" +
        $".combine({TwentyItems}.select(('2020-01-01T00:01:' & ($this + 10).toString()).toDateTime()))";

    /// <summary>
    /// A budget the temporals fit when only built (see the control test), and any pairwise operation over them
    /// does not. Measured: building them costs 1,986 steps, and the cheapest operation (isDistinct(),
    /// subsetOf()) 3,816 including the build.
    /// </summary>
    private const long PairwiseBudget = 3_500;

    public static TheoryData<string> PairwiseSetOperations => new()
    {
        "%q.distinct()",
        "%q.isDistinct()",
        "%q | %q",
        "%q.intersect(%q)",
        "%q.exclude(%q)",
        "%q.subsetOf(%q)",
        "%q.supersetOf(%q)",
        "%q.union(%q)",
        "%q.repeat($this)",
        "%q ~ %q",
        "%q.select($this in %q)",
    };

    [Theory]
    [MemberData(nameof(StringAmplifiers))]
    public void GivenALoweredStringLimit_WhenAnExpressionGrowsAString_ThenEvaluationStopsBeforeBuildingIt(string expression, string site)
    {
        // Arrange
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxStringLength = 1_000 });

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(expression, context));

        // Assert
        exception.Message.ShouldContain(site);
        exception.Message.ShouldContain("1000");
    }

    [Theory]
    [MemberData(nameof(CollectionAmplifiers))]
    public void GivenALoweredCollectionLimit_WhenAnExpressionGrowsACollection_ThenEvaluationStopsAtTheLimit(string expression, string site)
    {
        // Arrange
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxCollectionSize = 100 });

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(expression, context));

        // Assert
        exception.Message.ShouldContain(site);
    }

    [Fact]
    public void GivenALoweredStepBudget_WhenNestedSelectsBuildACrossProduct_ThenEvaluationStopsAtTheBudget()
    {
        // Arrange - the case nothing else catches: lazy evaluation materialises nothing, so memory stays flat.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = 10_000 });

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(CrossProduct, context));

        // Assert
        exception.Message.ShouldContain("step budget of 10000");
    }

    [Fact]
    public void GivenThePairwiseBudget_WhenTheTemporalsAreOnlyBuilt_ThenTheyFit()
    {
        // Arrange - the control that makes the test below about the operation rather than the operands.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = PairwiseBudget });

        // Act
        var result = Evaluate(WithTemporals("%q.count()"), context);

        // Assert
        result.Single().Value.ShouldBe(60);
    }

    [Theory]
    [MemberData(nameof(PairwiseSetOperations))]
    public void GivenALoweredStepBudget_WhenASetOperationComparesUnkeyedItemsPairwise_ThenEvaluationStopsAtTheBudget(string expression)
    {
        // Arrange
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = PairwiseBudget });

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(WithTemporals(expression), context))
            .Message.ShouldContain("step budget");
    }

    [Theory]
    [MemberData(nameof(PairwiseSetOperations))]
    public void GivenTheDefaultLimits_WhenASetOperationComparesUnkeyedItemsPairwise_ThenItCompletes(string expression)
    {
        // Arrange - the same expressions fit comfortably in the default budget.
        var context = new EvaluationContext();

        // Act & Assert
        Should.NotThrow(() => Evaluate(WithTemporals(expression), context));
    }

    [Fact]
    public void GivenALoweredStepBudget_WhenRepeatConstructsUnkeyedValues_ThenEvaluationStopsAtTheBudget()
    {
        // Arrange - dates have no hash key, so repeat() compares each new one with all processed so far: the
        // expensive-comparison hazard, now charged to the step budget as well as to repeat()'s own limit.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = 2_000 });

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate("@2020-01-01.repeat($this + 1 day)", context))
            .Message.ShouldContain("step budget");
    }

    [Fact]
    public void GivenALoweredStepBudget_WhenNestedAllCallsEvaluateNodesWithoutYieldingOrComparing_ThenEvaluationStopsAtTheBudget()
    {
        // Arrange - all() yields nothing and compares nothing, and reading a variable builds nothing, so only the
        // per-node charge in EvaluateExpression can see the 10^6 criteria evaluations.
        var nested = $"defineVariable('t', {TenItems}).select(" + string.Concat(Enumerable.Repeat("%t.all(", 6)) + "true" + new string(')', 7);
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = 10_000 });

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(nested, context))
            .Message.ShouldContain("step budget");
    }

    [Theory]
    [InlineData("distinct()")]
    [InlineData("select($this = %context.name.family).count()")]
    [InlineData("select($this < %context.name.family).count()")]
    [InlineData("sort().count()")]
    public void GivenALongStringFromTheResource_WhenAMultipliedCollectionOfItIsDedupedOrCompared_ThenTheTextIsCharged(string operation)
    {
        // Arrange - guard (new behaviour, not a regression): strings read from the resource were hashed and compared for a single step each, so
        // twenty references to a 160,000-character family name cost almost nothing; at attack scale (half a million
        // references to a 10 MB string) that is tens of minutes of CPU. Each now costs a step per 16 characters.
        var patient = ResourceJsonNode
            .Parse($$"""{"resourceType":"Patient","id":"p","name":[{"family":"{{new string('a', 160_000)}}"}]}""")
            .ToElement(FhirVersion.R4.GetSchemaProvider());
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = 100_000 });
        var expression = _parser.Parse($"{TwentyItems}.select(%context.name.family).{operation}");

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => _evaluator.Evaluate(patient, expression, context).ToList())
            .Message.ShouldContain("step budget");
    }

    [Fact]
    public void GivenLongStringsOnBothSides_WhenEquivalenceMatchesEveryPair_ThenEachPairsNormalisationIsCharged()
    {
        // Arrange - guard (new behaviour, not a regression): ~ normalises the whitespace of both Strings for every pair it tries, but only each
        // operand was charged once. A trailing space makes every pair equivalent, so all 400 pairs of twenty
        // references a side to a 160,000-character name are normalised: about 600,000 steps for building and
        // reading the operands, and 8,000,000 for the normalising.
        var patient = ResourceJsonNode
            .Parse($$"""{"resourceType":"Patient","id":"p","name":[{"family":"{{new string('a', 160_000)}}"}]}""")
            .ToElement(FhirVersion.R4.GetSchemaProvider());
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = 2_000_000 });
        var expression = _parser.Parse(
            $"{TwentyItems}.select(%context.name.family) ~ {TwentyItems}.select(%context.name.family & ' ')");

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => _evaluator.Evaluate(patient, expression, context).ToList())
            .Message.ShouldContain("step budget");
    }

    [Fact]
    public void GivenAShortTimeout_WhenNestedSelectsBuildACrossProduct_ThenEvaluationStopsAtTheTimeout()
    {
        // Arrange
        // The step budget is raised far past what 20 ms can spend, so only the timeout can stop the evaluation.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = long.MaxValue / 4, Timeout = TimeSpan.FromMilliseconds(20) });

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(CrossProduct, context));

        // Assert
        exception.Message.ShouldContain("time limit");
    }

    [Fact]
    public void GivenACancelledToken_WhenEvaluating_ThenTheInterpreterThrowsOperationCanceledException()
    {
        // Arrange
        var context = new EvaluationContext { CancellationToken = new CancellationToken(canceled: true) };

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => Evaluate("name.family", context));
    }

    [Fact]
    public void GivenATokenCancelledMidEvaluation_WhenEvaluating_ThenTheInterpreterStopsTheRunningExpression()
    {
        // Arrange
        // The step budget is raised far past what 20 ms can spend, so only the cancellation can stop the evaluation.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var context = new EvaluationContext
        {
            Limits = FhirPathEvaluationLimits.Default with { MaxSteps = long.MaxValue / 4 },
            CancellationToken = cancellation.Token,
        };

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => Evaluate(CrossProduct, context));
    }

    [Fact]
    public void GivenACancelledToken_WhenSelectRunsACompiledDelegate_ThenItThrowsOperationCanceledException()
    {
        // Arrange - 'name.family' is within the delegate compiler's forms, so this runs the compiled path.
        var context = new EvaluationContext { CancellationToken = new CancellationToken(canceled: true) };

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => Patient.Select("name.family", context).ToList());
    }

    [Fact]
    public void GivenATokenCancelledWhileACompiledWhereRuns_WhenSelect_ThenTheNextItemObservesIt()
    {
        // Arrange - the first item's children cancel the token as they are read, so only the per-item check inside
        // the compiled where() can stop the second.
        using var cancellation = new CancellationTokenSource();
        var root = new CancellingElement(cancellation, itemCount: 3);
        var context = new EvaluationContext { CancellationToken = cancellation.Token };

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => root.Select("item.where(code = 'x')", context).ToList());
    }

    [Fact]
    public void GivenOneContextReusedForTwoEvaluations_WhenEachFitsTheBudget_ThenNeitherIsChargedForTheOther()
    {
        // Arrange - every Evaluate call meters itself from scratch. The expression costs 415 steps, so a shared
        // count would exhaust 700 on the second call.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxSteps = 700 });
        var expression = TwentyItems + ".select($this + 1).count()";

        // Act
        var first = Evaluate(expression, context);
        var second = Evaluate(expression, context);

        // Assert
        first.Single().Value.ShouldBe(20);
        second.Single().Value.ShouldBe(20);
    }

    [Fact]
    public void GivenALoweredCaptureInputLimit_WhenReplaceMatchesSubstitutesAGroup_ThenItRefusesTheLongInput()
    {
        // Arrange - the linear engine does not time its capture phase, so the input is bounded before matching.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxRegexCaptureInputLength = 50 });
        var input = new string('a', 51);

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(
            () => Evaluate($"'{input}'.replaceMatches('(a)', '$1')", context));

        // Assert
        exception.Message.ShouldContain("replaceMatches()");
    }

    [Fact]
    public void GivenALoweredCaptureInputLimit_WhenReplaceMatchesSubstitutesNoGroup_ThenTheLimitDoesNotApply()
    {
        // Arrange - without a group reference no captures are computed, so the long input is accepted.
        var context = WithLimits(FhirPathEvaluationLimits.Default with { MaxRegexCaptureInputLength = 50 });
        var input = new string('a', 51);

        // Act
        var result = Evaluate($"'{input}'.replaceMatches('(a)', 'b')", context);

        // Assert
        result.Single().Value.ShouldBe(new string('b', 51));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void GivenANonPositiveStepBudget_WhenConstructingLimits_ThenThrowsArgumentOutOfRangeException(long maxSteps)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => FhirPathEvaluationLimits.Default with { MaxSteps = maxSteps });
    }

    [Fact]
    public void GivenANonPositiveTimeout_WhenConstructingLimits_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => FhirPathEvaluationLimits.Default with { Timeout = TimeSpan.Zero });
    }

    private static EvaluationContext WithLimits(FhirPathEvaluationLimits limits) => new() { Limits = limits };

    private static string DefineVariableChain(int length)
    {
        var chain = "defineVariable('v0', 'aaaaaaaa')";
        for (var i = 1; i <= length; i++)
        {
            chain += $".defineVariable('v{i}', %v{i - 1} & %v{i - 1})";
        }

        return chain;
    }

    private static string WithTemporals(string expression) => $"defineVariable('q', {Temporals}).select({expression})";

    private List<IElement> Evaluate(string expression, EvaluationContext context) =>
        _evaluator.Evaluate(Patient, _parser.Parse(expression), context).ToList();

    /// <summary>
    /// A root with <c>item</c> children whose <c>code</c> is read by the compiled <c>where()</c>; reading the first
    /// item's code cancels the token.
    /// </summary>
    private sealed class CancellingElement(CancellationTokenSource cancellation, int itemCount) : IElement
    {
        public string Name => "root";
        public string InstanceType => "Root";
        public object? Value => null;
        public string Location => "root";
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name is null or "item"
                ? Enumerable.Range(0, itemCount).Select(index => (IElement)new Item(cancellation, index)).ToList()
                : [];

        public T? Meta<T>() where T : class => null;

        private sealed class Item(CancellationTokenSource cancellation, int index) : IElement
        {
            public string Name => "item";
            public string InstanceType => "Item";
            public object? Value => null;
            public string Location => $"root.item[{index}]";
            public IType? Type => null;
            public bool HasPrimitiveValue => false;

            public IReadOnlyList<IElement> Children(string? name = null)
            {
                cancellation.Cancel();
                return [];
            }

            public T? Meta<T>() where T : class => null;
        }
    }
}
