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
        // Arrange — a null system (the segment was never supplied): constrains QuantityCodeId only, with
        // no SystemId IS NULL. Note this is NOT what "5.4||mg" parses to — that supplies an empty system,
        // covered by GivenAnEmptySystemQuantity_... below.
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
    public void GivenAnEmptySystemQuantity_WhenLowered_ThenEmitsSystemIdIsNull()
    {
        // Arrange — "5.4||mg": the system segment was supplied but empty, which constrains the stored
        // system to be ABSENT. Passing an empty string must not be conflated with passing null.
        var parameter = Parameter();
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, QuantitySearchValue.Parse("5.4||mg"));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, quantityCodeIds: quantityCodeIds), 103);

        // Assert — And(And(numeric, IsNull(SystemId)), Equal(QuantityCodeId, 77))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var middle = outer.Left.ShouldBeOfType<Predicate.And>();
        var numeric = middle.Left.ShouldBeOfType<Predicate.And>();
        numeric.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue");
        numeric.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue");
        var systemIsNull = middle.Right.ShouldBeOfType<Predicate.IsNull>();
        systemIsNull.Column.Table.ShouldBe("QuantitySearchParam");
        systemIsNull.Column.Column.ShouldBe("SystemId");
        var codeEqual = outer.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("QuantityCodeId");
        codeEqual.Value.Value.ShouldBe(77);
    }

    [Fact]
    public void GivenANeQuantityValue_WhenLowered_ThenNegatesTheEqContainment()
    {
        // Arrange — quantity had no ne coverage at any level, so nothing pinned that it reaches the same
        // NumericRangeComparison branch the number leaf does. eq is And(Low >= 5.35, High <= 5.45); ne is
        // its De Morgan negation.
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Ne, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var lt = or.Left.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("LowValue");
        lt.Value.Value.ShouldBe(5.35m);
        var gt = or.Right.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("HighValue");
        gt.Value.Value.ShouldBe(5.45m);
    }

    [Fact]
    public void GivenAFullyQualifiedNeQuantity_WhenLowered_ThenTheSystemAndCodeStayConjoinedOutsideTheNegation()
    {
        // Arrange — the ne negation applies to the numeric range only. Distributing it over SystemId or
        // QuantityCodeId would make "?value-quantity=ne5.4|http://unitsofmeasure.org|mg" match rows in
        // other units entirely, which is the failure mode an Or at the wrong level produces.
        var parameter = Parameter();
        var systemIds = new Dictionary<string, int?> { ["http://unitsofmeasure.org"] = 42 };
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Ne, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202, systemIds, quantityCodeIds), 103);

        // Assert — And(And(Or(numeric), Equal(SystemId, 42)), Equal(QuantityCodeId, 77))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var middle = outer.Left.ShouldBeOfType<Predicate.And>();
        middle.Left.ShouldBeOfType<Predicate.Or>();
        middle.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("SystemId");
        outer.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("QuantityCodeId");
    }

    [Fact]
    public void GivenAQuantityRow_WhenLoweredWithEqAndNe_ThenExactlyOneMatchesIt()
    {
        // Arrange — the partition property RangeComparatorSemanticsTests proves for number, asserted for
        // quantity so the shared comparator is pinned from both call sites. [5.0, 6.0] encloses the eq
        // window without being contained by it, the row that separates containment from overlap.
        var parameter = Parameter();
        var row = new Dictionary<string, object> { ["LowValue"] = 5.0m, ["HighValue"] = 6.0m };

        // Act
        var eq = QuantityLoweringRule.Lower(
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m)),
            new QuantitySearchValue(system: null!, code: null!, 5.4m), ContextResolving(parameter, 202), 103);
        var ne = QuantityLoweringRule.Lower(
            new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m)),
            new QuantitySearchValue(system: null!, code: null!, 5.4m), ContextResolving(parameter, 202), 103);

        // Assert
        PredicateRowEvaluator.Matches(eq.Predicate!, row).ShouldBeFalse();
        PredicateRowEvaluator.Matches(ne.Predicate!, row).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnEmptySystemQuantity_WhenLowered_ThenMatchesTheTokenPathsEmptySystemShape()
    {
        // Arrange — quantity's system follows the token pattern, so an empty system must lower to the
        // same IsNull(SystemId) node the token rule builds, not to a second convention.
        var quantityParameter = Parameter();
        var quantityPredicate = new SearchParameterPredicateExpression(
            quantityParameter, SearchComparator.Eq, modifier: null, QuantitySearchValue.Parse("5.4||mg"));
        var tokenParameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var tokenPredicate = new SearchParameterPredicateExpression(
            tokenParameter, SearchComparator.Eq, modifier: null, TokenSearchValue.Parse("|mg"));

        // Act
        var quantityCte = QuantityLoweringRule.Lower(
            quantityPredicate,
            (QuantitySearchValue)quantityPredicate.Value,
            ContextResolving(quantityParameter, 202, quantityCodeIds: new Dictionary<string, int?> { ["mg"] = 77 }),
            103);
        var tokenCte = TokenLoweringRule.Lower(
            tokenPredicate,
            (TokenSearchValue)tokenPredicate.Value,
            new LeafContext(new SymbolTable(
                new Dictionary<string, short> { [tokenParameter.Url.ToString()] = 55 },
                new Dictionary<string, short>())),
            103);

        // Assert — both express "the stored system is absent" as IsNull over their own SystemId column
        var quantitySystem = quantityCte.Predicate
            .ShouldBeOfType<Predicate.And>().Left
            .ShouldBeOfType<Predicate.And>().Right
            .ShouldBeOfType<Predicate.IsNull>();
        var tokenSystem = tokenCte.Predicate
            .ShouldBeOfType<Predicate.And>().Left
            .ShouldBeOfType<Predicate.IsNull>();
        quantitySystem.Column.Column.ShouldBe("SystemId");
        tokenSystem.Column.Column.ShouldBe("SystemId");
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

    // :ap quantity — unqualified: same numeric approximation overlap as number :ap
    // 5.4m: tol=0.54 → LowValue <= 5.94, HighValue >= 4.86
    [Fact]
    public void GivenApComparator_WhenLoweredUnqualified_ThenBuildsApproximateNumericOverlap()
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
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(5.94m);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(4.86m);
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

        // Assert — And(And(And(LowValue <= 5.94, HighValue >= 4.86), Equal(SystemId, 42)), Equal(QuantityCodeId, 77))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var middle = outer.Left.ShouldBeOfType<Predicate.And>();
        var numeric = middle.Left.ShouldBeOfType<Predicate.And>();
        var le = numeric.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(5.94m);
        var ge = numeric.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(4.86m);
        var sysEqual = middle.Right.ShouldBeOfType<Predicate.Equal>();
        sysEqual.Column.Column.ShouldBe("SystemId");
        sysEqual.Value.Value.ShouldBe(42);
        var codeEqual = outer.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("QuantityCodeId");
        codeEqual.Value.Value.ShouldBe(77);
    }
}
