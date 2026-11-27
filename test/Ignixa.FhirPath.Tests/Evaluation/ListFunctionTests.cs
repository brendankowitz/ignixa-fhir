// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Visitors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class ListFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private static IElement CreateRoot() =>
        ResourceJsonNode.Parse("""{ "resourceType": "Patient", "id": "list-test", "active": true }""")
            .ToElement(FhirVersion.R4.GetSchemaProvider());

    [Theory]
    [InlineData("list(1, 2, 3, 4)", new[] { 1, 2, 3, 4 })]
    [InlineData("list(1, list(2, 3), 4)", new[] { 1, 2, 3, 4 })]
    [InlineData("list({}, list(1, 2), list(3, 4))", new[] { 1, 2, 3, 4 })]
    [InlineData("list(list(2, 1), list(2, 3))", new[] { 2, 1, 2, 3 })]
    [InlineData("list(3, 1 | 2, 3)", new[] { 3, 1, 2, 3 })]
    [InlineData("list()", new int[0])]
    [InlineData("list({})", new int[0])]
    [InlineData("list({}, {})", new int[0])]
    public void GivenArguments_WhenList_ThenItemsAreAppendedInOrderWithDuplicates(string expression, int[] expected)
    {
        // Arrange
        var root = CreateRoot();

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse(expression)).Select(e => e.Value).ToList();

        // Assert
        result.ShouldBe(expected.Cast<object?>().ToList());
    }

    [Fact]
    public void GivenAFocus_WhenListIsCalledAtRoot_ThenTheInputCollectionIsNotUsed()
    {
        // Arrange
        var root = CreateRoot();

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse("list(1, 2)")).Select(e => e.Value).ToList();

        // Assert
        result.ShouldBe([1, 2]);
    }

    [Fact]
    public void GivenListResult_WhenChained_ThenFollowingFunctionsSeeAllItems()
    {
        // Arrange
        var root = CreateRoot();

        // Act
        var sorted = _evaluator.Evaluate(root, _parser.Parse("list(3, 1, 2, 1).sort()")).Select(e => e.Value).ToList();
        var filtered = _evaluator.Evaluate(root, _parser.Parse("list(1, 2, 3, 4).where($this > 2)")).Select(e => e.Value).ToList();
        var count = _evaluator.Evaluate(root, _parser.Parse("list(1, list(1, 1)).count()")).Select(e => e.Value).ToList();

        // Assert
        sorted.ShouldBe([1, 1, 2, 3]);
        filtered.ShouldBe([3, 4]);
        count.ShouldBe([3]);
    }

    [Fact]
    public void GivenListAsCombineArgument_WhenEvaluated_ThenItIsTheDocumentedFluentAlternative()
    {
        // Arrange
        var root = CreateRoot();

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse("(5 | 6).combine(list(2, 2))")).Select(e => e.Value).ToList();

        // Assert
        result.ShouldBe([5, 6, 2, 2]);
    }

    [Fact]
    public void GivenListInsideScopedFunction_WhenEvaluated_ThenArgumentsAreEvaluatedAgainstThis()
    {
        // Arrange
        var root = CreateRoot();

        // Act
        var result = _evaluator.Evaluate(root, _parser.Parse("(1 | 2).select(list($this, $this * 10))")).Select(e => e.Value).ToList();

        // Assert
        result.ShouldBe([1, 10, 2, 20]);
    }

    [Theory]
    [InlineData("(1 | 2).list(3)")]
    [InlineData("1.list()")]
    public void GivenFluentInvocation_WhenList_ThenEvaluationFails(string expression)
    {
        // Arrange
        var root = CreateRoot();
        var parsed = _parser.Parse(expression);

        // Act / Assert
        Should.Throw<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, parsed).ToList())
            .Message.ShouldContain("fluent");
    }

    [Fact]
    public void GivenFluentInvocation_WhenAnalyzed_ThenAnErrorIsReported()
    {
        // Arrange
        var schema = FhirVersion.R4.GetSchemaProvider();

        // Act
        var fluent = new FhirPathAnalyzer(schema).Analyze("name.list(family)", "Patient");
        var root = new FhirPathAnalyzer(schema).Analyze("list(name.family, name.given)", "Patient");

        // Assert
        fluent.Issues.ShouldContain(i => i.Severity == ValidationIssueSeverity.Error && i.Message.Contains("fluent"));
        root.Issues.ShouldNotContain(i => i.Severity == ValidationIssueSeverity.Error);
    }
}
