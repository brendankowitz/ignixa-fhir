using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
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

    [Fact]
    public void GivenAnUntypedReferenceWithAllUnresolvableTargets_WhenLowered_ThenStillEmitsTypeFilter()
    {
        // When every declared target resolves to the unmatchable sentinel (-1) the predicate must still
        // contain a ReferenceResourceTypeId constraint. Dropping sentinel targets would produce an empty
        // declared list, which falls through to the unconstrained id-only predicate — re-introducing the
        // false-positive behaviour the type-narrowing pass exists to prevent.
        var parameter = new SearchParameterInfo(
            "organization",
            "organization",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["UnknownFoo"]);

        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: "org-123"));

        // Store the declared target as the unmatchable sentinel, as Resolve does when the resolver
        // returns null for a type name it does not recognise.
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 210 },
            new Dictionary<string, short> { ["Patient"] = 103, ["UnknownFoo"] = SymbolTable.UnmatchableResourceTypeId });

        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, new LeafContext(symbols), 103);

        // Must be AND(Equal(ReferenceResourceTypeId, -1), Equal(ReferenceResourceId, ...)), not a bare
        // Equal(ReferenceResourceId) — confirming the sentinel is emitted rather than the list being collapsed.
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var typeConstraint = and.Left.ShouldBeOfType<Predicate.Equal>();
        typeConstraint.Column.Column.ShouldBe("ReferenceResourceTypeId");
        typeConstraint.Value.Value.ShouldBe(SymbolTable.UnmatchableResourceTypeId);
        and.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceId");
    }

    [Fact]
    public void GivenAnUntypedReferenceWithMultipleDeclaredTargets_WhenLowered_ThenEmitsIsNullArm()
    {
        // The shipping engine admits null-typed rows for multi-target parameters because a reference
        // indexed without type information is genuinely ambiguous when the parameter allows several types.
        var parameter = new SearchParameterInfo(
            "general-practitioner",
            "general-practitioner",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-general-practitioner"),
            targetResourceTypes: ["Organization", "Practitioner"]);

        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: "gp-456"));

        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 211 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 111, ["Practitioner"] = 114 });

        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, new LeafContext(symbols), 103);

        // Predicate: AND(OR(OR(Eq(typeId,111), Eq(typeId,114)), IsNull(typeId)), Eq(id))
        var outerAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var typeOr = outerAnd.Left.ShouldBeOfType<Predicate.Or>();
        typeOr.Right.ShouldBeOfType<Predicate.IsNull>().Column.Column.ShouldBe("ReferenceResourceTypeId");
        outerAnd.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceId");
    }

    [Fact]
    public void GivenAnUntypedReferenceWithSingleDeclaredTarget_WhenLowered_ThenOmitsIsNullArm()
    {
        // For a single-target parameter the reference type is unambiguous regardless of how it was
        // indexed; admitting null-typed rows would widen the match for no semantic gain.
        var parameter = new SearchParameterInfo(
            "organization",
            "organization",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);

        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: "org-123"));

        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 210 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 111 });

        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, new LeafContext(symbols), 103);

        // Predicate: AND(Equal(typeId, 111), Equal(id)) — no IS NULL arm anywhere.
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceTypeId");
        ContainsTypeIsNull(cte.Predicate).ShouldBeFalse("single-target must not admit null-typed rows.");
    }

    private static bool ContainsTypeIsNull(Predicate predicate) => predicate switch
    {
        Predicate.And and => ContainsTypeIsNull(and.Left) || ContainsTypeIsNull(and.Right),
        Predicate.Or or => ContainsTypeIsNull(or.Left) || ContainsTypeIsNull(or.Right),
        Predicate.IsNull isNull => isNull.Column.Column == "ReferenceResourceTypeId",
        _ => false,
    };

    private static bool ReferencesBaseUri(Predicate predicate) => predicate switch
    {
        Predicate.And and => ReferencesBaseUri(and.Left) || ReferencesBaseUri(and.Right),
        Predicate.Or or => ReferencesBaseUri(or.Left) || ReferencesBaseUri(or.Right),
        Predicate.IsNull isNull => isNull.Column.Column == "BaseUri",
        Predicate.Equal equal => equal.Column.Column == "BaseUri",
        _ => false,
    };

    [Fact]
    public void GivenAnUntypedReferenceValue_WhenLowered_ThenItIsNarrowedToTheParametersDeclaredTargetTypes()
    {
        var parameter = new SearchParameterInfo(
            "organization",
            "organization",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);

        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: "org-123"));

        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 210 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 111 });

        var plan = Lower.Run(
            predicate,
            symbols,
            "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null).Plan;

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("ReferenceResourceTypeId");
    }

    [Fact]
    public void GivenAnUntypedReferenceValueWithMultipleDeclaredTargets_WhenLowered_ThenAllTargetsAreOrdered()
    {
        // A parameter with two declared target types must OR both type ids into the predicate.
        var parameter = new SearchParameterInfo(
            "general-practitioner",
            "general-practitioner",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-general-practitioner"),
            targetResourceTypes: ["Organization", "Practitioner"]);

        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: "gp-456"));

        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 211 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 111, ["Practitioner"] = 114 });

        var plan = Lower.Run(
            predicate,
            symbols,
            "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null).Plan;

        var emitted = SqlBuilder.Run(plan);

        // Both type ids must be present as bound parameters.
        emitted.Sql.ShouldContain("ReferenceResourceTypeId");
        emitted.Parameters.Select(p => p.Value).ShouldContain((short)111);
        emitted.Parameters.Select(p => p.Value).ShouldContain((short)114);
    }

    [Fact]
    public void GivenAnUntypedReferenceValueOnParameterWithNoTargets_WhenLowered_ThenNoReferenceResourceTypeIdFilter()
    {
        // A parameter with no declared target types must not add a ReferenceResourceTypeId constraint.
        var parameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        // No targetResourceTypes — default is empty.

        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: "any-123"));

        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url!.ToString()] = 77 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, new LeafContext(symbols), 104);

        // With no declared targets, falls back to id-only — no ReferenceResourceTypeId column touched.
        var idEqual = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
    }
}
