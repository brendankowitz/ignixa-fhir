using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class CompositeLoweringDispatcherTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short> { ["DocumentReference"] = 123 }));

    private static SearchParameterInfo CompositeParameter(string code)
        => new(code, code, SearchParamType.Composite, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    private static CompositeComponentExpression TokenComponentAt(int position, string paramCode, string tokenCode)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: tokenCode, text: null)));
    }

    private static CompositeComponentExpression NumberComponentAt(int position, string paramCode, decimal value)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new NumberSearchValue(value)));
    }

    private static CompositeComponentExpression StringComponentAt(int position, string paramCode, string text)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue(text)));
    }

    private static CompositeComponentExpression DateComponentAt(int position, string paramCode, DateTimeOffset value)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(value)));
    }

    private static CompositeComponentExpression QuantityComponentAt(int position, string paramCode, decimal value)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, value)));
    }

    private static CompositeComponentExpression ReferenceComponentAt(int position, string paramCode)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null,
                new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456")));
    }

    [Fact]
    public void GivenTwoTokenComponents_WhenDispatched_ThenRoutesToTokenTokenLoweringRule()
    {
        // Arrange
        var composite = CompositeParameter("code-value-concept");
        var components = new[]
        {
            TokenComponentAt(0, "code", "8480-6"),
            TokenComponentAt(1, "value-concept", "high"),
        };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 301), 104);

        // Assert
        cte.Table.TableName.ShouldBe("TokenTokenCompositeSearchParam");
    }

    [Fact]
    public void GivenAOutOfOrderTokenThenTwoNumberComponents_WhenDispatched_ThenOrdersByPositionBeforeRoutingToTokenNumberNumber()
    {
        // Arrange -- constructed out of Position order to prove the dispatcher sorts, not trusts input order
        var composite = CompositeParameter("component-code-value-number-number");
        var components = new[]
        {
            NumberComponentAt(2, "high", 10m),
            TokenComponentAt(0, "code", "8480-6"),
            NumberComponentAt(1, "low", 5m),
        };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 302), 104);

        // Assert
        cte.Table.TableName.ShouldBe("TokenNumberNumberCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        inner.Left.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe("8480-6");
    }

    [Fact]
    public void GivenAnUnsupportedComponentTypeCombination_WhenDispatched_ThenThrows()
    {
        // Arrange -- three token components has no composite table
        var composite = CompositeParameter("unsupported");
        var components = new[]
        {
            TokenComponentAt(0, "a", "1"),
            TokenComponentAt(1, "b", "2"),
            TokenComponentAt(2, "c", "3"),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 303), 104));
    }

    [Fact]
    public void GivenAComponentWrappingAMultiaryExpressionInsteadOfAPredicate_WhenDispatched_ThenThrowsRatherThanCrashing()
    {
        // Arrange -- a component with its own comma-separated alternatives; CompositeComponentExpression's own
        // doc comment notes WrappedExpression is "frequently a MultiaryExpression" in that case. A single-element
        // Or is enough to prove the shape mismatch -- MultiaryExpression's constructor rejects an empty list.
        var composite = CompositeParameter("code-value-concept");
        var codeParam = ComponentParameter("code");
        var alternativePredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1", text: null));
        var components = new[]
        {
            new CompositeComponentExpression(codeParam, 0, new MultiaryExpression(MultiaryOperator.Or, [alternativePredicate])),
            TokenComponentAt(1, "value-concept", "high"),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 301), 104));
    }

    [Fact]
    public void GivenATokenThenAStringComponent_WhenDispatched_ThenRoutesToTokenString()
    {
        // Arrange
        var composite = CompositeParameter("code-value-string");
        var components = new[] { TokenComponentAt(0, "code", "8480-6"), StringComponentAt(1, "value-string", "Elevated") };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 401), 104);

        // Assert
        cte.Table.TableName.ShouldBe("TokenStringCompositeSearchParam");
    }

    [Fact]
    public void GivenATokenThenAQuantityComponent_WhenDispatched_ThenRoutesToTokenQuantity()
    {
        // Arrange
        var composite = CompositeParameter("component-code-value-quantity");
        var components = new[] { TokenComponentAt(0, "component-code", "8480-6"), QuantityComponentAt(1, "component-value-quantity", 120m) };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 402), 104);

        // Assert
        cte.Table.TableName.ShouldBe("TokenQuantityCompositeSearchParam");
    }

    [Fact]
    public void GivenATokenThenADateComponent_WhenDispatched_ThenRoutesToTokenDateTime()
    {
        // Arrange
        var composite = CompositeParameter("code-value-date");
        var components = new[] { TokenComponentAt(0, "code", "8480-6"), DateComponentAt(1, "value-date", new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)) };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 403), 104);

        // Assert
        cte.Table.TableName.ShouldBe("TokenDateTimeCompositeSearchParam");
    }

    [Fact]
    public void GivenAReferenceThenATokenComponent_WhenDispatched_ThenRoutesToReferenceToken()
    {
        // Arrange
        var composite = CompositeParameter("relatesto");
        var components = new[] { ReferenceComponentAt(0, "target"), TokenComponentAt(1, "code", "replaces") };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 404), 104);

        // Assert
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");
    }

    [Fact]
    public void GivenATokenThenAReferenceComponent_WhenDispatched_ThenStillRoutesToReferenceToken()
    {
        // Arrange -- swapped order, proving the dispatcher's second arm for this type also works
        var composite = CompositeParameter("relatesto");
        var components = new[] { TokenComponentAt(0, "code", "replaces"), ReferenceComponentAt(1, "target") };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 404), 104);

        // Assert
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");
    }
}
