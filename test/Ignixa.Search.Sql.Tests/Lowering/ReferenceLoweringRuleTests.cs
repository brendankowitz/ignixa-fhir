using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class ReferenceLoweringRuleTests
{
    [Fact]
    public void GivenALocalTypedReference_WhenLowered_ThenBaseUriIsNullAndTypeIdAndResourceId()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "123"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var context = new LeafContext(symbols);

        // Act
        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, context, 104);

        // Assert — BaseUri IS NULL AND TypeId = @p0 AND Id = @p1
        cte.SearchParamId.ShouldBe((short)77);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("ReferenceSearchParam");

        var outerAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var innerAnd = outerAnd.Left.ShouldBeOfType<Predicate.And>();

        // BaseUri IS NULL
        var baseUriIsNull = innerAnd.Left.ShouldBeOfType<Predicate.IsNull>();
        baseUriIsNull.Column.Table.ShouldBe("ReferenceSearchParam");
        baseUriIsNull.Column.Column.ShouldBe("BaseUri");

        // TypeId = @p0
        var typeEqual = innerAnd.Right.ShouldBeOfType<Predicate.Equal>();
        typeEqual.Column.Column.ShouldBe("ReferenceResourceTypeId");
        typeEqual.Value.Value.ShouldBe((short)103);

        // Id = @p1
        var idEqual = outerAnd.Right.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
        idEqual.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenALocalUntypedReference_WhenLowered_ThenBaseUriIsNullAndResourceIdOnly()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: string.Empty, resourceId: "123"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short>());
        var context = new LeafContext(symbols);

        // Act
        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, context, 104);

        // Assert — BaseUri IS NULL AND Id = @p0
        cte.ResourceTypeId.ShouldBe((short)104);

        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var baseUriIsNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        baseUriIsNull.Column.Column.ShouldBe("BaseUri");

        var idEqual = and.Right.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
        idEqual.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenAnExternalTypedReference_WhenLowered_ThenBaseUriEqualCollateBin2AndTypeIdAndResourceId()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.External, baseUri: new Uri("http://example.org/fhir/"), resourceType: "Patient", resourceId: "123"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var context = new LeafContext(symbols);

        // Act
        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, context, 104);

        // Assert — BaseUri = @p0 COLLATE Latin1_General_100_BIN2 AND TypeId = @p1 AND Id = @p2
        cte.Table.TableName.ShouldBe("ReferenceSearchParam");

        var outerAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var innerAnd = outerAnd.Left.ShouldBeOfType<Predicate.And>();

        // BaseUri = @p0 COLLATE Latin1_General_100_BIN2
        var baseUriEqual = innerAnd.Left.ShouldBeOfType<Predicate.Equal>();
        baseUriEqual.Column.Column.ShouldBe("BaseUri");
        baseUriEqual.Value.Value.ShouldBe("http://example.org/fhir/");
        baseUriEqual.Collation.ShouldBe("Latin1_General_100_BIN2");

        // TypeId = @p1
        var typeEqual = innerAnd.Right.ShouldBeOfType<Predicate.Equal>();
        typeEqual.Column.Column.ShouldBe("ReferenceResourceTypeId");
        typeEqual.Value.Value.ShouldBe((short)103);

        // Id = @p2
        var idEqual = outerAnd.Right.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
        idEqual.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenSameTypeAndIdWithDistinctBases_WhenLowered_ThenBaseUriDistinguishesThem()
    {
        // Arrange — two references with same type/id but different bases must produce different predicates.
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var context = new LeafContext(symbols);

        var localValue = new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "456");
        var externalValue = new ReferenceSearchValue(ReferenceKind.External, baseUri: new Uri("http://remote.org/"), resourceType: "Patient", resourceId: "456");

        var localPredicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, localValue);
        var externalPredicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, externalValue);

        // Act
        var localCte = ReferenceLoweringRule.Lower(localPredicate, localValue, context, 104);
        var externalCte = ReferenceLoweringRule.Lower(externalPredicate, externalValue, context, 104);

        // Assert — local begins with IsNull; external begins with Equal(BaseUri)
        var localOuterAnd = localCte.Predicate.ShouldBeOfType<Predicate.And>();
        var localInnerAnd = localOuterAnd.Left.ShouldBeOfType<Predicate.And>();
        localInnerAnd.Left.ShouldBeOfType<Predicate.IsNull>();

        var externalOuterAnd = externalCte.Predicate.ShouldBeOfType<Predicate.And>();
        var externalInnerAnd = externalOuterAnd.Left.ShouldBeOfType<Predicate.And>();
        var externalBaseUri = externalInnerAnd.Left.ShouldBeOfType<Predicate.Equal>();
        externalBaseUri.Value.Value.ShouldBe("http://remote.org/");
        externalBaseUri.Collation.ShouldBe("Latin1_General_100_BIN2");
    }

}
