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
    public void GivenAnUntypedReference_WhenLowered_ThenConstrainsResourceIdOnly()
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

        // Assert — Id = @p0, with no BaseUri constraint. A value the parser could not resolve to a
        // resource type carries the whole input as its id, so there is nothing else to constrain on;
        // adding BaseUri IS NULL here would exclude every externally-based row for no stated reason.
        cte.ResourceTypeId.ShouldBe((short)104);

        var idEqual = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
        idEqual.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenAnExternalTypedReference_WhenLowered_ThenBaseUriEqualAndTypeIdAndResourceId()
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

        // Assert — BaseUri = @p0 AND TypeId = @p1 AND Id = @p2
        cte.Table.TableName.ShouldBe("ReferenceSearchParam");

        var outerAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var innerAnd = outerAnd.Left.ShouldBeOfType<Predicate.And>();

        // BaseUri = @p0
        var baseUriEqual = innerAnd.Left.ShouldBeOfType<Predicate.Equal>();
        baseUriEqual.Column.Column.ShouldBe("BaseUri");
        baseUriEqual.Value.Value.ShouldBe("http://example.org/fhir/");
        baseUriEqual.Collation.ShouldBeNull();

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
        externalBaseUri.Collation.ShouldBeNull();
    }

    [Fact]
    public void GivenAnInternalOrExternalReference_WhenLowered_ThenEmitsNoBaseUriPredicate()
    {
        // Arrange -- a bare relative search value such as "subject=Patient/123". The spec requires that a
        // relative reference match a stored row whether or not that row carries a base, so this must NOT
        // constrain BaseUri at all. Emitting "BaseUri IS NULL" here -- as a strict local/external XOR
        // would -- silently excludes every externally-based row.
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var value = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: "Patient", resourceId: "123");
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value);
        var context = new LeafContext(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103 }));

        // Act
        var cte = ReferenceLoweringRule.Lower(predicate, value, context, 104);

        // Assert -- exactly Type AND Id, with no BaseUri arm anywhere in the tree.
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceTypeId");
        and.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceId");
        ReferencesBaseUri(cte.Predicate).ShouldBeFalse("InternalOrExternal must leave BaseUri unconstrained.");
    }

    private static bool ReferencesBaseUri(Predicate predicate) => predicate switch
    {
        Predicate.And and => ReferencesBaseUri(and.Left) || ReferencesBaseUri(and.Right),
        Predicate.Or or => ReferencesBaseUri(or.Left) || ReferencesBaseUri(or.Right),
        Predicate.IsNull isNull => isNull.Column.Column == "BaseUri",
        Predicate.Equal equal => equal.Column.Column == "BaseUri",
        _ => false,
    };
}
