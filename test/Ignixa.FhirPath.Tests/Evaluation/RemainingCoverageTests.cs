/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Additional tests to reach 80% coverage for FhirPath library.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class RemainingCoverageTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region FhirEvaluationContext Tests

    [Fact]
    public void GivenFhirEvaluationContext_WhenCreated_ThenHasEnvironmentVariables()
    {
        // Arrange & Act
        var context = new FhirEvaluationContext();

        // Assert
        Assert.NotNull(context);
    }

    [Fact]
    public void GivenFhirEvaluationContext_WhenSetVariable_ThenCanRetrieve()
    {
        // Arrange
        var element = CreateIntegerElement(42);

        // Act - use immutable pattern
        var context = new FhirEvaluationContext()
            .WithEnvironmentVariable("test", element);
        var result = context.GetEnvironmentVariable("test");

        // Assert
        Assert.Equal(element, result);
    }

    #endregion

    #region Select Function Tests (Iterator Coverage)

    [Fact]
    public void GivenMultipleItems_WhenSelect_ThenProjectsAllItems()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3).select($this * 2)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Value);
        Assert.Equal(4, result[1].Value);
        Assert.Equal(6, result[2].Value);
    }

    [Fact]
    public void GivenNestedPath_WhenSelect_ThenReturnsProjection()
    {
        // Arrange
        var patient = CreatePatientWithMultipleNames();
        var expr = _parser.Parse("name.select(given)");

        // Act
        var result = _evaluator.Evaluate(patient, expr).ToList();

        // Assert
        Assert.NotEmpty(result);
    }

    #endregion

    #region Where Function Tests (Iterator Coverage)

    [Fact]
    public void GivenMultipleItems_WhenWhere_ThenFiltersCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).where($this > 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(4, result[0].Value);
        Assert.Equal(5, result[1].Value);
    }

    [Fact]
    public void GivenComplexCondition_WhenWhere_ThenFiltersCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3 | 4).where($this > 1 and $this < 4)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Value);
        Assert.Equal(3, result[1].Value);
    }

    #endregion

    #region Repeat Function Tests (TypedElementEqualityComparer Coverage)

    [Fact]
    public void GivenNestedStructure_WhenRepeat_ThenReturnsAllDescendants()
    {
        // Arrange
        var patient = CreatePatientWithMultipleNames();
        var expr = _parser.Parse("repeat(children())");

        // Act
        var result = _evaluator.Evaluate(patient, expr).ToList();

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenEmptyProjection_WhenRepeat_ThenReturnsEmpty()
    {
        // Arrange
        // Repeat returns only projection results, not the original input
        // With empty projection {}, no results are produced
        var expr = _parser.Parse("(1 | 2).repeat({})");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert - Per FHIRPath spec, repeat only returns projection results
        Assert.Empty(result);
    }

    [Fact]
    public void GivenASmallIterationCap_WhenRepeatNeverConverges_ThenThrowsFhirPathEvaluationExceptionAtThatCap()
    {
        // Arrange - same constructing-projection hazard as the full-cost test below (repeat($this & 'x'),
        // the #433 hazard: every round concatenates a literal onto $this, so the output is never deep-equal
        // to anything already processed, and only an iteration or comparison cap can stop it) - but driven
        // through RepeatGuardLimits' test seam (#435) with only MaxIterations lowered. MaxComparisons stays
        // at its production default (15,000,000): at a 50-iteration cap this expression accumulates on the
        // order of a few thousand comparisons at most (see CollectionFunctions.Repeat's remarks for the
        // measured growth curve), nowhere near that default, so the iteration cap - not the comparison
        // budget - is what trips here. This isolates the iteration-cap code path in milliseconds instead of
        // the ~33 seconds proving it at the real 10,000-iteration scale used to cost.
        const int smallCap = 50;
        using var scope = new RepeatGuardLimits.Scope(maxIterations: smallCap);
        var expr = _parser.Parse("'a'.repeat($this & 'x')");
        var root = CreateIntegerElement(0);

        // Act
        var evaluate = () => _evaluator.Evaluate(root, expr).ToList();

        // Assert - parentheses are load-bearing: ShouldContain is ordinal substring containment, so a bare
        // "50" would also match a stray "150" or "500"; "(50)" is the whole formatted argument.
        var exception = Should.Throw<FhirPathEvaluationException>(evaluate);
        exception.Message.ShouldContain("repeat()");
        exception.Message.ShouldContain("maximum iteration limit");
        exception.Message.ShouldContain($"({smallCap})");
    }

    [Fact]
    public void GivenASmallComparisonBudget_WhenRepeatNeverConverges_ThenThrowsFhirPathEvaluationExceptionAtThatBudget()
    {
        // Arrange - the same hazard again, but with only MaxComparisons lowered via the #435 seam.
        // MaxIterations stays at its production default (10,000): at a 50-comparison budget this
        // expression has only dequeued on the order of a handful of times (see CollectionFunctions.Repeat's
        // remarks for the measured growth curve), nowhere near that default, so the comparison budget -
        // not the iteration cap - is what trips here. This isolates the comparison-budget code path
        // (added by #435) in milliseconds.
        const long smallBudget = 50;
        using var scope = new RepeatGuardLimits.Scope(maxComparisons: smallBudget);
        var expr = _parser.Parse("'a'.repeat($this & 'x')");
        var root = CreateIntegerElement(0);

        // Act
        var evaluate = () => _evaluator.Evaluate(root, expr).ToList();

        // Assert
        var exception = Should.Throw<FhirPathEvaluationException>(evaluate);
        exception.Message.ShouldContain("repeat()");
        exception.Message.ShouldContain("maximum comparison-count budget");
        exception.Message.ShouldContain($"({smallBudget})");
    }

    [Fact]
    public void GivenAConstructingProjection_WhenRepeatNeverConverges_ThenThrowsFhirPathEvaluationExceptionWithinTheComparisonBudget()
    {
        // Arrange - repeat($this & 'x') is the constructing-projection hazard from #433: every round
        // concatenates a literal onto $this, so the output is a fresh, longer string that is never
        // deep-equal to anything already processed. The dedup check that stops a *navigating* projection
        // over a finite tree (see the tests above) cannot stop one that *constructs* a new value each
        // round - only the iteration cap and (since #435) the comparison-count budget can.
        //
        // This is the one full-cost test kept end-to-end at production thresholds (#435): the two tests
        // above already prove each guard's code path and message in milliseconds via the test seam, so this
        // one exists solely to pin that the *real* defaults (10,000 iterations, 15,000,000 comparisons)
        // actually terminate this hazard, and in what shape. Before #435 this cost ~33 seconds and tripped
        // the iteration cap. It now costs a few seconds (per CollectionFunctions.Repeat's remarks, the
        // comparison budget reaches 15,000,000 at roughly a third of the iteration count the old iteration
        // cap alone needed) and trips the comparison budget instead - the iteration cap is never reached by
        // this expression at production scale any more, which is exactly the point of adding a cost bound
        // alongside a count bound.
        var expr = _parser.Parse("'a'.repeat($this & 'x')");
        var root = CreateIntegerElement(0);

        // Act
        var evaluate = () => _evaluator.Evaluate(root, expr).ToList();

        // Assert - the parentheses are load-bearing for the same ordinal-substring reason as above.
        var exception = Should.Throw<FhirPathEvaluationException>(evaluate);
        exception.Message.ShouldContain("repeat()");
        exception.Message.ShouldContain("maximum comparison-count budget");
        exception.Message.ShouldContain("(15000000)");
    }

    [Fact]
    public void GivenDeeplyNestedQuestionnaireItems_WhenRepeatItem_ThenReturnsEveryItemWithoutTrippingTheIterationGuard()
    {
        // Arrange - the control for the test above: a Questionnaire.item-shaped tree that *navigates*
        // rather than constructs. Branching 3 deep 6 yields 1,092 items. Per CollectionFunctions.Repeat's
        // remarks, this control test legitimately dequeues roughly 1,093 times (1,092-item tree with ~9×
        // headroom built into the 10,000-iteration cap), proving the cap does not reject real input.
        const int breadth = 3;
        const int depth = 6;
        var expectedItemCount = Enumerable.Range(1, depth).Sum(level => (int)Math.Pow(breadth, level));
        var questionnaire = CreateDeeplyNestedQuestionnaireItems(breadth, depth);
        var expr = _parser.Parse("repeat(item)");

        // Act
        var result = _evaluator.Evaluate(questionnaire, expr).ToList();

        // Assert
        result.Count.ShouldBe(expectedItemCount);
        result.Select(e => e.Children("linkId").Single().Value).Distinct().Count().ShouldBe(expectedItemCount);
    }

    #endregion

    #region IsDistinct Tests (TypedElementEqualityComparer Coverage)

    [Fact]
    public void GivenUniqueIntegers_WhenIsDistinct_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("(5 | 10 | 15).`isDistinct`()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenUniqueStrings_WhenIsDistinct_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("('a' | 'b' | 'c').`isDistinct`()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region PrimitiveElement Property Tests

    [Fact]
    public void GivenPrimitiveElement_WhenAccessName_ThenReturnsEmptyString()
    {
        // This tests the PrimitiveElement.Name property through evaluation
        var expr = _parser.Parse("42");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessLocation_ThenReturnsEmptyString()
    {
        // This tests the PrimitiveElement.Location property
        var expr = _parser.Parse("'test'");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Location);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessType_ThenReturnsNull()
    {
        // This tests the PrimitiveElement.Type property
        var expr = _parser.Parse("true");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Null(result.Type);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessChildren_ThenReturnsEmpty()
    {
        // This tests the PrimitiveElement.Children() method
        var expr = _parser.Parse("123");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Empty(result.Children());
    }

    #endregion

    #region ChildExpression Iterator Tests

    [Fact]
    public void GivenComplexResource_WhenNavigateChildren_ThenReturnsAllMatches()
    {
        // This exercises the ChildExpression iterator
        var patient = CreatePatientWithMultipleNames();
        var expr = _parser.Parse("name.given");

        var result = _evaluator.Evaluate(patient, expr).ToList();

        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenMultipleLevels_WhenNavigateDeep_ThenTraversesCorrectly()
    {
        // This exercises nested child navigation
        var patient = CreatePatientWithMultipleNames();
        var expr = _parser.Parse("name.given.first()");

        var result = _evaluator.Evaluate(patient, expr).ToList();

        Assert.NotEmpty(result);
    }

    #endregion

    #region RepeatAll Function Tests

    [Fact]
    public void GivenEmptyProjection_WhenRepeatAll_ThenReturnsEmpty()
    {
        // Arrange - repeatAll with empty projection returns empty
        var expr = _parser.Parse("(1 | 2).repeatAll({})");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenSingleStep_WhenRepeatAll_ThenReturnsResults()
    {
        // Arrange - Project to empty on first iteration
        // (1+1) yields 2, then (2+1) yields 3, etc.
        // This tests that repeatAll does iterate
        var expr = _parser.Parse("1.repeatAll({})");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert - Empty projection means no results
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenRepeatAll_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.repeatAll($this)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenSelfReferentialProjection_WhenRepeatAllExceedsIterationLimit_ThenThrowsFhirPathEvaluationException()
    {
        // Arrange
        var expr = _parser.Parse("1.repeatAll($this)");
        var root = CreateIntegerElement(0);

        // Act
        var evaluate = () => _evaluator.Evaluate(root, expr).ToList();

        // Assert
        var exception = Should.Throw<FhirPathEvaluationException>(evaluate);
        exception.Message.ShouldContain("maximum iteration limit");
    }

    #endregion

    #region Coalesce Function Tests

    [Fact]
    public void GivenFirstNonEmpty_WhenCoalesce_ThenReturnsFirst()
    {
        // Arrange
        var expr = _parser.Parse("coalesce(1, 2, 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].Value);
    }

    [Fact]
    public void GivenFirstEmpty_WhenCoalesce_ThenReturnsSecond()
    {
        // Arrange
        var expr = _parser.Parse("coalesce({}, 2, 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(2, result[0].Value);
    }

    [Fact]
    public void GivenAllEmpty_WhenCoalesce_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("coalesce({}, {}, {})");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenSingleArgument_WhenCoalesce_ThenReturnsIt()
    {
        // Arrange
        var expr = _parser.Parse("coalesce(42)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(42, result[0].Value);
    }

    [Fact]
    public void GivenCollection_WhenCoalesce_ThenReturnsFirstNonEmptyCollection()
    {
        // Arrange - First argument is a collection
        var expr = _parser.Parse("coalesce(1 | 2, 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Value);
        Assert.Equal(2, result[1].Value);
    }

    [Fact]
    public void GivenPatientWithName_WhenCoalesceNames_ThenReturnsFirstNonEmpty()
    {
        // Arrange - Simulates Patient.name.where(use='official'), Patient.name.where(use='usual'), etc.
        // Since we can't easily create a Patient with 'use' field, test the concept
        var expr = _parser.Parse("coalesce({}, 'fallback')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("fallback", result[0].Value);
    }

    #endregion

    #region Expression Base Class Tests

    [Fact]
    public void GivenExpression_WhenCompared_ThenUsesLocationInfo()
    {
        // This tests the Expression base class methods
        var expr1 = _parser.Parse("Patient");
        var expr2 = _parser.Parse("name");

        Assert.NotEqual(expr1.Location, expr2.Location);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private IElement CreatePatientWithMultipleNames()
    {
        var name1Children = new List<IElement>
        {
            new PrimitiveElement("John", "string", "given"),
            new PrimitiveElement("Smith", "string", "family")
        };

        var name2Children = new List<IElement>
        {
            new PrimitiveElement("Johnny", "string", "given"),
            new PrimitiveElement("Smith", "string", "family")
        };

        var children = new List<IElement>
        {
            new ComplexElement("name", "HumanName", name1Children),
            new ComplexElement("name", "HumanName", name2Children),
            new PrimitiveElement("male", "code", "gender")
        };

        return new ComplexElement("Patient", "Patient", children);
    }

    /// <summary>
    /// Builds a Questionnaire.item-shaped tree, <paramref name="breadth"/> items wide at every level down
    /// to <paramref name="depth"/> levels deep, each carrying a unique <c>linkId</c> so no two items are
    /// deep-equal - a real Questionnaire's items are distinguishable the same way, and a false dedup here
    /// would under-count the control this tree exists to provide.
    /// </summary>
    private IElement CreateDeeplyNestedQuestionnaireItems(int breadth, int depth)
    {
        IElement BuildItem(string linkId, int remainingDepth)
        {
            var children = new List<IElement>
            {
                new PrimitiveElement(linkId, "string", "linkId")
            };

            if (remainingDepth > 0)
            {
                for (int i = 0; i < breadth; i++)
                {
                    children.Add(BuildItem($"{linkId}.{i}", remainingDepth - 1));
                }
            }

            return new ComplexElement("item", "Questionnaire.item", children);
        }

        var rootItems = new List<IElement>();
        for (int i = 0; i < breadth; i++)
        {
            rootItems.Add(BuildItem($"{i}", depth - 1));
        }

        return new ComplexElement("Questionnaire", "Questionnaire", rootItems);
    }

    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type, string? name = null)
        {
            Value = value;
            InstanceType = type;
            Name = name ?? string.Empty;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    private class ComplexElement : IElement
    {
        private readonly List<IElement> _children;

        public ComplexElement(string name, string type, List<IElement> children)
        {
            Name = name;
            InstanceType = type;
            _children = children;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children;
            }

            return _children.Where(c => c.Name == name).ToList();
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
