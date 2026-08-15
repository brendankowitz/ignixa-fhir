/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath defineVariable() function (FHIRPath 2.0).
 * Tests variable definition, scoping, and usage in subsequent expressions.
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class DefineVariableFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenDefineVariable_WhenReferencingVariable_ThenReturnsDefinedValue()
    {
        // Use a chain: defineVariable sets x=5, then .select accesses %x
        var expr = _parser.Parse("5.defineVariable('x').select(%x + 10)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // 5 is passed through defineVariable, then select adds 10 -> 15
        Assert.Single(result);
        Assert.Equal(15, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsingInWhereClause_ThenFiltersCorrectly()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).where($this > %threshold)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Value);
        Assert.Equal(4, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariableWithExpression_WhenReferencingVariable_ThenEvaluatesExpression()
    {
        // Chain: defineVariable evaluates 5*2=10, stores as 'doubled', then select accesses it
        var expr = _parser.Parse("1.defineVariable('doubled', 5 * 2).select(%doubled)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal(10, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenVariableIsString_ThenStoresStringValue()
    {
        // Chain: defineVariable stores 'test' as 'name', then select accesses it
        var expr = _parser.Parse("'hello'.defineVariable('name', 'test').select(%name)");
        var root = CreateStringElement("root");

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal("test", result[0].Value);
    }

    [Fact]
    public void GivenDefineVariableChained_WhenMultipleVariables_ThenBothAvailable()
    {
        // Chain: defineVariable a, then defineVariable b, then select uses both
        var expr = _parser.Parse("1.defineVariable('a', 10).defineVariable('b', 20).select(%a + %b)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // %a=10, %b=20, so result is 30
        Assert.Single(result);
        Assert.Equal(30, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariableWithCollection_WhenReferencingVariable_ThenReturnsCollection()
    {
        // Chain: defineVariable stores collection, then select accesses count
        var expr = _parser.Parse("1.defineVariable('items', 1 | 2 | 3).select(%items.count())");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // %items has 3 elements
        Assert.Single(result);
        Assert.Equal(3, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariableInvalidArgumentCount_WhenEvaluating_ThenThrowsException()
    {
        // Test with 0 arguments - should throw
        var expr = _parser.Parse("defineVariable()");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() =>
            _evaluator.Evaluate(root, expr).ToList()
        );

        Assert.Contains("1 or 2 arguments", exception.Message, StringComparison.Ordinal);

        // Test with 3 arguments - should also throw
        var expr3 = _parser.Parse("defineVariable('x', 1, 2)");
        var exception3 = Assert.Throws<FhirPathEvaluationException>(() =>
            _evaluator.Evaluate(root, expr3).ToList()
        );

        Assert.Contains("1 or 2 arguments", exception3.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariableNonStringName_WhenEvaluating_ThenThrowsException()
    {
        var expr = _parser.Parse("defineVariable(5, 10)");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() =>
            _evaluator.Evaluate(root, expr).ToList()
        );

        Assert.Contains("defineVariable requires a string as the first argument", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedInSelect_ThenVariableAvailable()
    {
        var expr = _parser.Parse("(1 | 2 | 3).defineVariable('multiplier', 2).select($this * %multiplier)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Value);
        Assert.Equal(4, result[1].Value);
        Assert.Equal(6, result[2].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenReturnsOriginalFocus_ThenFocusUnchanged()
    {
        var expr = _parser.Parse("(5 | 10).defineVariable('x', 100)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(5, result[0].Value);
        Assert.Equal(10, result[1].Value);
    }

    #region Complex Scoping Tests

    [Fact]
    public void GivenDefineVariable_WhenUsedInNestedSelect_ThenVariableAccessible()
    {
        // defineVariable value is accessible in chained select
        var expr = _parser.Parse("1.defineVariable('x', 100).select(%x + 1)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // %x = 100, so result is 101
        Assert.Single(result);
        Assert.Equal(101, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedAcrossChainedOperations_ThenVariableAccessible()
    {
        // Variable defined early is accessible in later operations
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).where($this > %threshold).select($this * 10)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // where filters to 3, 4 -> select multiplies to 30, 40
        Assert.Equal(2, result.Count);
        Assert.Equal(30, result[0].Value);
        Assert.Equal(40, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenRedefinedInChain_ThenSignalsError()
    {
        // Official test dvRedefiningVariableThrowsError (defineVariable('v1').defineVariable('v1').select(%v1)):
        // redefining a name already defined earlier in the same chain is an error, not an overwrite.
        var expr = _parser.Parse("1.defineVariable('x', 10).defineVariable('x', 20).select(%x)");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());
        Assert.Contains("%x", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedInAggregate_ThenVariableAccessibleInAccumulator()
    {
        // defineVariable should be accessible within aggregate expression
        var expr = _parser.Parse("(1 | 2 | 3).defineVariable('base', 100).aggregate($total + $this + %base, 0)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        // Iteration 0: 0 + 1 + 100 = 101
        // Iteration 1: 101 + 2 + 100 = 203
        // Iteration 2: 203 + 3 + 100 = 306
        Assert.Equal(306, result.Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenMultipleVariablesChained_ThenAllAccessible()
    {
        // Multiple chained defineVariable calls, all accessible
        var expr = _parser.Parse("1.defineVariable('a', 10).defineVariable('b', 20).defineVariable('c', %a + %b).select(%c)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal(30, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedInIif_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).select(iif($this > %threshold, 'high', 'low'))");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(4, result.Count);
        Assert.Equal("low", result[0].Value);  // 1 <= 2
        Assert.Equal("low", result[1].Value);  // 2 <= 2
        Assert.Equal("high", result[2].Value); // 3 > 2
        Assert.Equal("high", result[3].Value); // 4 > 2
    }

    [Fact]
    public void GivenDefineVariable_WhenOneArgument_ThenUsesFocusAsValue()
    {
        // Single argument form: defineVariable('name') uses current focus as value
        // Then chain to access it
        var expr = _parser.Parse("(5 | 10 | 15).defineVariable('items').select(%items.sum())");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // For each item, %items.sum() = 30 (sum of original collection)
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal(30, r.Value));
    }

    [Fact]
    public void GivenDefineVariable_WhenReferencingUndefinedVariable_ThenSignalsError()
    {
        // Official test defineVariable10 (select(%fam.given)): a reference to a name nothing defines is an
        // error. Returning empty instead would make an undefined variable indistinguishable from one that
        // is defined and holds nothing.
        var expr = _parser.Parse("%undefined");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("undefined", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedWithExists_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).defineVariable('target', 3).exists($this = %target)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedWithAll_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(5 | 10 | 15).defineVariable('min', 4).all($this > %min)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Variable Scoping Tests - Forward Chain Only

    [Fact]
    public void GivenDefineVariable_WhenDefinedInChain_ThenVisibleDownstream()
    {
        // Variables defined in chain are visible to subsequent operations
        var expr = _parser.Parse("1.defineVariable('x', 10).select(%x + $this)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // %x = 10, $this = 1, so result = 11
        Assert.Single(result);
        Assert.Equal(11, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenDefinedInUnionBranch_ThenNotVisibleInOtherBranch()
    {
        // Per FHIRPath spec: Variables are only visible in direct chains, not sibling union branches.
        // Official test dvUsageOutsideScopeThrows: the sibling branch does not merely see empty, it sees a
        // name nothing has defined, which is an error.
        var expr = _parser.Parse("defineVariable('x', 5) | %x + 10");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("x", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariable_WhenDefinedBeforeUnion_ThenVisibleInBothBranches()
    {
        // Variables defined BEFORE the union ARE visible in both branches
        // because the variable is in scope when the union is evaluated
        var expr = _parser.Parse("1.defineVariable('x', 5).select((%x + 1) | (%x + 2))");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // %x = 5, union of (6, 7)
        Assert.Equal(2, result.Count);
        Assert.Equal(6, result[0].Value);  // 5 + 1
        Assert.Equal(7, result[1].Value);  // 5 + 2
    }

    [Fact]
    public void GivenDefineVariable_WhenNotYetDefined_ThenSignalsErrorBeforeDefinition()
    {
        // Each union branch is isolated, so the middle branch's define never reaches the outer ones - and a
        // reference to a name that is not in scope is an error rather than an empty collection.
        var expr = _parser.Parse("%notYetDefined | defineVariable('notYetDefined', 42) | %notYetDefined");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("notYetDefined", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariable_WhenDefinedInsideWhere_ThenVisibleInSameWhere()
    {
        // Variable defined inside where() is visible in the same where clause
        var expr = _parser.Parse("(1 | 2 | 3).where(defineVariable('limit', 2).count() > 0 and $this > %limit)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // Note: defineVariable returns focus (which is the single item being tested)
        // %limit = 2, so items > 2 are: 3
        Assert.Single(result);
        Assert.Equal(3, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenSameNameDefinedInSiblingArguments_ThenBothResolveLocally()
    {
        // The redefinition rule is scoped to the invocation chain, so sibling arguments of one call may each
        // define the same name - official test dvParametersDontColide, which this replaces the removed
        // overwrite-in-chain duplicate with (that case is now GivenDefineVariable_WhenRedefinedInChain_ThenSignalsError).
        var expr = _parser.Parse("'aaa'.replace(defineVariable('param', 'aaa').select(%param), defineVariable('param', 'bbb').select(%param))");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal("bbb", result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenDefinedInSelectChain_ThenVisibleInSelectProjection()
    {
        // Variable defined in select() chain is visible in the same chain
        var expr = _parser.Parse("(1 | 2 | 3).select(defineVariable('doubled', $this * 2).select(%doubled + 100).first())");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // For each item: defineVariable sets doubled, chain to select %doubled + 100
        // Item 1: doubled=2, result 102
        // Item 2: doubled=4, result 104
        // Item 3: doubled=6, result 106
        Assert.Equal(3, result.Count);
        Assert.Equal(102, result[0].Value);
        Assert.Equal(104, result[1].Value);
        Assert.Equal(106, result[2].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenChainedWithMultipleFunctions_ThenVisibleThroughoutChain()
    {
        // Variable is visible through entire chain after definition
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).defineVariable('mid', 3).where($this != %mid).select($this + %mid)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // Filter: keep items != 3: (1, 2, 4, 5)
        // Project: add 3 to each: (4, 5, 7, 8)
        Assert.Equal(4, result.Count);
        Assert.Equal(4, result[0].Value);   // 1 + 3
        Assert.Equal(5, result[1].Value);   // 2 + 3
        Assert.Equal(7, result[2].Value);   // 4 + 3
        Assert.Equal(8, result[3].Value);   // 5 + 3
    }

    #endregion

    private static IElement CreateIntegerElement(int value)
    {
        return new TestElement(value, "integer");
    }

    private static IElement CreateStringElement(string value)
    {
        return new TestElement(value, "string");
    }

    private class TestElement(object value, string type) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => type;
        public object Value => value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
