using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
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

/// <summary>
/// Pins <c>identifier:of-type=[system]|[code]|[value]</c> down to one TokenSearchParam source whose
/// predicates are conjoined, which is what makes the three conditions describe the same Identifier. The
/// row-evaluation tests below are the ones that actually prove it: a shape assertion alone would pass just
/// as well for three independent sources intersected, the shape that returns wrong rows.
/// </summary>
public class OfTypeLoweringRuleTests
{
    private const string V2IdentifierType = "http://terminology.hl7.org/CodeSystem/v2-0203";

    /// <summary>
    /// Read from the catalog, not written as a literal, for the reason
    /// <see cref="TokenLoweringRuleTests"/> spells out: the split point is the Code column's declared width,
    /// and a literal that drifted from the DDL would search for a prefix no row stores.
    /// </summary>
    private static readonly int InlineCodeWidth =
        Sql.Catalog.SqlCatalog.Default.Table("TokenSearchParam").Column("Code").MaxLength!.Value;

    private static SearchParameterInfo IdentifierParameter()
        => new("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));

    private static LeafContext ContextResolving(
        SearchParameterInfo parameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds));

    private static CteDefinition.ParamSource Lower(
        string? typeSystem,
        string typeCode,
        string identifierValue,
        IReadOnlyDictionary<string, int?>? systemIds = null)
    {
        var parameter = IdentifierParameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new OfTypeTokenSearchValue(identifierValue, typeSystem, typeCode));

        return OfTypeLoweringRule.Lower(predicate, (OfTypeTokenSearchValue)predicate.Value, ContextResolving(parameter, 55, systemIds), 103);
    }

    [Fact]
    public void GivenAnOfTypeSearchWithATypeSystem_WhenLowered_ThenOneSourceConjoinsSystemTypeCodeAndValue()
    {
        // Arrange
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };

        // Act
        var cte = Lower(V2IdentifierType, "MR", "12345", systemIds);

        // Assert -- And(IdentifierTypeSystemId, And(IdentifierTypeCode, Code)) over a single ParamSource
        cte.Table.TableName.ShouldBe("TokenSearchParam");
        cte.SearchParamId.ShouldBe((short)55);
        cte.ResourceTypeId.ShouldBe((short)103);

        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var systemEqual = outer.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Table.ShouldBe("TokenSearchParam");
        systemEqual.Column.Column.ShouldBe("IdentifierTypeSystemId");
        systemEqual.Value.Value.ShouldBe(77);

        var inner = outer.Right.ShouldBeOfType<Predicate.And>();
        var typeCodeEqual = inner.Left.ShouldBeOfType<Predicate.Equal>();
        typeCodeEqual.Column.Column.ShouldBe("IdentifierTypeCode");
        typeCodeEqual.Value.Value.ShouldBe("MR");

        var valueEqual = inner.Right.ShouldBeOfType<Predicate.Equal>();
        valueEqual.Column.Column.ShouldBe("Code");
        valueEqual.Value.Value.ShouldBe("12345");
    }

    [Fact]
    public void GivenAnOfTypeSearchWithoutATypeSystem_WhenLowered_ThenConstrainsTypeCodeAndValueOnly()
    {
        // Arrange -- the |MR|12345 form. FHIR allows the type system to be omitted.

        // Act
        var cte = Lower(typeSystem: null, "MR", "12345");

        // Assert -- no IdentifierTypeSystemId arm at all
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var typeCodeEqual = and.Left.ShouldBeOfType<Predicate.Equal>();
        typeCodeEqual.Column.Column.ShouldBe("IdentifierTypeCode");
        typeCodeEqual.Value.Value.ShouldBe("MR");

        var valueEqual = and.Right.ShouldBeOfType<Predicate.Equal>();
        valueEqual.Column.Column.ShouldBe("Code");
        valueEqual.Value.Value.ShouldBe("12345");
    }

    [Fact]
    public void GivenAnOfTypeSearch_WhenLowered_ThenTypeCodeUsesCaseSensitiveCollation()
    {
        // Arrange
        var cte = Lower(typeSystem: null, "MR", "12345");

        // Act
        var typeCodeEqual = cte.Predicate.ShouldBeOfType<Predicate.And>().Left.ShouldBeOfType<Predicate.Equal>();

        // Assert
        typeCodeEqual.Column.Column.ShouldBe("IdentifierTypeCode");
        typeCodeEqual.Collation.ShouldBe("Latin1_General_100_CS_AS");
    }

    [Fact]
    public void GivenAnOfTypeSearchWithoutATypeSystem_WhenEvaluatedAgainstRowsFromAnySystem_ThenTheSystemIsUnconstrained()
    {
        // Arrange -- two rows with the same type code and value but different (and absent) type systems.
        // Shape assertions cannot tell "no system arm" from "an arm that happens to match"; this can.
        var matchingRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 999,
            ["IdentifierTypeCode"] = "MR",
            ["Code"] = "12345",
        };
        var otherSystemRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 4,
            ["IdentifierTypeCode"] = "MR",
            ["Code"] = "12345",
        };

        // Act
        var cte = Lower(typeSystem: null, "MR", "12345");

        // Assert
        PredicateRowEvaluator.Matches(cte.Predicate!, matchingRow).ShouldBeTrue();
        PredicateRowEvaluator.Matches(cte.Predicate!, otherSystemRow).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnUnknownTypeSystem_WhenLowered_ThenReturnsFalsePredicate()
    {
        // Arrange -- the resolver knows the string but no row uses it, the same three-state miss
        // TokenColumnEquality.Build turns into Predicate.False rather than an id comparison against null.
        var systemIds = new Dictionary<string, int?> { ["http://unknown.example/id-types"] = null };

        // Act
        var cte = Lower("http://unknown.example/id-types", "MR", "12345", systemIds);

        // Assert
        cte.Predicate.ShouldBeOfType<Predicate.False>();
        PredicateRowEvaluator.Matches(
            cte.Predicate!,
            new Dictionary<string, object> { ["IdentifierTypeCode"] = "MR", ["Code"] = "12345" }).ShouldBeFalse();
    }

    [Fact]
    public void GivenAnIdentifierValueLongerThanTheInlineWidth_WhenLowered_ThenComparesBothHalvesOfTheSplit()
    {
        // Arrange -- an overflowing value exists only as (prefix, remainder) across Code + CodeOverflow;
        // comparing the whole value against Code alone could never match the row the writer produced.
        var longValue = new string('A', InlineCodeWidth) + new string('B', 40);
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };

        // Act
        var cte = Lower(V2IdentifierType, "MR", longValue, systemIds);

        // Assert
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Right.ShouldBeOfType<Predicate.And>();
        var valueAnd = inner.Right.ShouldBeOfType<Predicate.And>();

        var codeEqual = valueAnd.Left.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code");
        codeEqual.Value.Value.ShouldBe(longValue[..InlineCodeWidth]);

        var overflowEqual = valueAnd.Right.ShouldBeOfType<Predicate.Equal>();
        overflowEqual.Column.Column.ShouldBe("CodeOverflow");
        overflowEqual.Value.Value.ShouldBe(longValue[InlineCodeWidth..]);
    }

    [Fact]
    public void GivenAnOverflowingIdentifierValue_WhenEvaluatedAgainstTheRowTheWriterWouldProduce_ThenItMatchesButASharedPrefixDoesNot()
    {
        // Arrange
        var longValue = new string('A', InlineCodeWidth) + new string('B', 40);
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };
        var storedRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 77,
            ["IdentifierTypeCode"] = "MR",
            ["Code"] = longValue[..InlineCodeWidth],
            ["CodeOverflow"] = longValue[InlineCodeWidth..],
        };
        var sharedPrefixRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 77,
            ["IdentifierTypeCode"] = "MR",
            ["Code"] = longValue[..InlineCodeWidth],
            ["CodeOverflow"] = new string('C', 40),
        };

        // Act
        var cte = Lower(V2IdentifierType, "MR", longValue, systemIds);

        // Assert
        PredicateRowEvaluator.Matches(cte.Predicate!, storedRow).ShouldBeTrue();
        PredicateRowEvaluator.Matches(cte.Predicate!, sharedPrefixRow).ShouldBeFalse();
    }

    [Fact]
    public void GivenARowWithTheRightValueButTheWrongTypeCode_WhenEvaluated_ThenItDoesNotMatch()
    {
        // Arrange -- the case :of-type exists for. Without the IdentifierTypeCode conjunct this row
        // matches, and identifier:of-type degrades into a plain identifier search.
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };
        var wrongTypeRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 77,
            ["IdentifierTypeCode"] = "SS",
            ["Code"] = "12345",
        };

        // Act
        var cte = Lower(V2IdentifierType, "MR", "12345", systemIds);

        // Assert
        PredicateRowEvaluator.Matches(cte.Predicate!, wrongTypeRow).ShouldBeFalse();
    }

    [Fact]
    public void GivenARowWithTheRightTypeCodeButTheWrongValue_WhenEvaluated_ThenItDoesNotMatch()
    {
        // Arrange
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };
        var wrongValueRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 77,
            ["IdentifierTypeCode"] = "MR",
            ["Code"] = "67890",
        };

        // Act
        var cte = Lower(V2IdentifierType, "MR", "12345", systemIds);

        // Assert
        PredicateRowEvaluator.Matches(cte.Predicate!, wrongValueRow).ShouldBeFalse();
    }

    [Fact]
    public void GivenARowFromADifferentTypeSystem_WhenEvaluated_ThenItDoesNotMatch()
    {
        // Arrange -- same type code "MR" in a local code system is not the v2-0203 "MR"
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };
        var otherSystemRow = new Dictionary<string, object>
        {
            ["IdentifierTypeSystemId"] = 4,
            ["IdentifierTypeCode"] = "MR",
            ["Code"] = "12345",
        };

        // Act
        var cte = Lower(V2IdentifierType, "MR", "12345", systemIds);

        // Assert
        PredicateRowEvaluator.Matches(cte.Predicate!, otherSystemRow).ShouldBeFalse();
    }

    [Fact]
    public void GivenAnOfTypePredicate_WhenDispatched_ThenItReachesTheOfTypeRuleRatherThanTheUnsupportedArm()
    {
        // Arrange -- the dispatcher's default arm used to swallow OfTypeTokenSearchValue into a
        // NotSupportedException, which the API surfaced as a 400.
        var parameter = IdentifierParameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter,
            SearchComparator.Eq,
            modifier: null,
            new OfTypeTokenSearchValue("12345", V2IdentifierType, "MR"));
        var systemIds = new Dictionary<string, int?> { [V2IdentifierType] = 77 };

        // Act
        var cte = LeafLoweringDispatcher.Lower(predicate, ContextResolving(parameter, 55, systemIds), 103);

        // Assert
        cte.Table.TableName.ShouldBe("TokenSearchParam");
        cte.Predicate.ShouldBeOfType<Predicate.And>();
    }
}
