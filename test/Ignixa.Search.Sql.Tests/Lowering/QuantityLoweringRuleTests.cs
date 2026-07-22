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

public class QuantityLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo parameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null,
        IReadOnlyDictionary<string, int?>? quantityCodeIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds,
            quantityCodeIds: quantityCodeIds));

    private static SearchParameterInfo Parameter()
        => new("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"));

    [Fact]
    public void GivenAnUnqualifiedQuantityValue_WhenLowered_ThenComparesLowAndHighValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue");
        and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue");
    }

    [Fact]
    public void GivenASystemOnlyQuantity_WhenLowered_ThenConjoinsNumericAndSystemIdPredicates()
    {
        // Arrange — system only, no code: numeric AND SystemId = resolved(system)
        var parameter = Parameter();
        var systemIds = new Dictionary<string, int?> { ["http://unitsofmeasure.org"] = 42 };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, systemIds), 103);

        // Assert — And(numeric, Equal(SystemId, 42))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var numeric = outer.Left.ShouldBeOfType<Predicate.And>();
        numeric.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue");
        numeric.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue");
        var sysEqual = outer.Right.ShouldBeOfType<Predicate.Equal>();
        sysEqual.Column.Column.ShouldBe("SystemId");
        sysEqual.Value.Value.ShouldBe(42);
    }

    [Fact]
    public void GivenACodeOnlyQuantity_WhenLowered_ThenConjoinsNumericAndQuantityCodeIdPredicateWithNoSystemIsNullConstraint()
    {
        // Arrange — value||code (system absent/null): constrains QuantityCodeId only; no SystemId IS NULL
        var parameter = Parameter();
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: "mg", 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, quantityCodeIds: quantityCodeIds), 103);

        // Assert — And(numeric, Equal(QuantityCodeId, 77)); Right is Equal not IsNull, proving no system null guard
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var numeric = outer.Left.ShouldBeOfType<Predicate.And>();
        numeric.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue");
        numeric.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue");
        var codeEqual = outer.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("QuantityCodeId");
        codeEqual.Value.Value.ShouldBe(77);
    }

    [Fact]
    public void GivenAFullyQualifiedQuantity_WhenLowered_ThenConjoinsNumericAndSystemIdAndQuantityCodeIdPredicates()
    {
        // Arrange — system|code: numeric AND SystemId = resolved(system) AND QuantityCodeId = resolved(code)
        var parameter = Parameter();
        var systemIds = new Dictionary<string, int?> { ["http://unitsofmeasure.org"] = 42 };
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, systemIds, quantityCodeIds), 103);

        // Assert — And(And(numeric, Equal(SystemId, 42)), Equal(QuantityCodeId, 77))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var middle = outer.Left.ShouldBeOfType<Predicate.And>();
        var numeric = middle.Left.ShouldBeOfType<Predicate.And>();
        numeric.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue");
        numeric.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue");
        var sysEqual = middle.Right.ShouldBeOfType<Predicate.Equal>();
        sysEqual.Column.Column.ShouldBe("SystemId");
        sysEqual.Value.Value.ShouldBe(42);
        var codeEqual = outer.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("QuantityCodeId");
        codeEqual.Value.Value.ShouldBe(77);
    }

    [Fact]
    public void GivenAnUnknownSystem_WhenLowered_ThenReturnsFalsePredicate()
    {
        // Arrange — non-empty system where resolver returned null (known miss)
        var parameter = Parameter();
        var systemIds = new Dictionary<string, int?> { ["http://unknown.org"] = null };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unknown.org", code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, systemIds), 103);

        // Assert
        cte.Predicate.ShouldBeOfType<Predicate.False>();
    }

    [Fact]
    public void GivenAnUnknownCode_WhenLowered_ThenReturnsFalsePredicate()
    {
        // Arrange — non-empty code where resolver returned null (known miss)
        var parameter = Parameter();
        var quantityCodeIds = new Dictionary<string, int?> { ["unknown-unit"] = null };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: "unknown-unit", 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, quantityCodeIds: quantityCodeIds), 103);

        // Assert
        cte.Predicate.ShouldBeOfType<Predicate.False>();
    }

    // :ap quantity — unqualified: same numeric approximation range as number :ap
    // 5.4m: tol=0.54 → LowValue >= 4.86, HighValue <= 5.94
    [Fact]
    public void GivenApComparator_WhenLoweredUnqualified_ThenBuildsApproximateNumericRange()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Ap, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(4.86m);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(5.94m);
    }

    // :ap quantity — fully qualified: numeric approximation with system+code identity still conjoins
    // in deterministic order: And(And(And(numeric_ap), Equal(SystemId)), Equal(QuantityCodeId))
    [Fact]
    public void GivenApComparator_WhenLoweredFullyQualified_ThenConjoinsApproximateRangeWithSystemAndCodeInDeterministicOrder()
    {
        // Arrange
        var parameter = Parameter();
        var systemIds = new Dictionary<string, int?> { ["http://unitsofmeasure.org"] = 42 };
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Ap, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, systemIds, quantityCodeIds), 103);

        // Assert — And(And(And(LowValue >= 4.86, HighValue <= 5.94), Equal(SystemId, 42)), Equal(QuantityCodeId, 77))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var middle = outer.Left.ShouldBeOfType<Predicate.And>();
        var numeric = middle.Left.ShouldBeOfType<Predicate.And>();
        var ge = numeric.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(4.86m);
        var le = numeric.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(5.94m);
        var sysEqual = middle.Right.ShouldBeOfType<Predicate.Equal>();
        sysEqual.Column.Column.ShouldBe("SystemId");
        sysEqual.Value.Value.ShouldBe(42);
        var codeEqual = outer.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("QuantityCodeId");
        codeEqual.Value.Value.ShouldBe(77);
    }
}
