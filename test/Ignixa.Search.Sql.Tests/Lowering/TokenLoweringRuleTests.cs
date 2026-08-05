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

public class TokenLoweringRuleTests
{
    /// <summary>
    /// The point an overflowing code splits at: the Code column's declared width, read from the catalog so
    /// these tests pin the relationship to the schema rather than one schema's current number. That the row
    /// generators really split here — rather than at a literal that happens to agree — is pinned from the
    /// writers' side by TokenCodeOverflowSplitPointTests in
    /// Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.
    /// </summary>
    private static readonly int InlineCodeWidth =
        Sql.Catalog.SqlCatalog.Default.Table("TokenSearchParam").Column("Code").MaxLength!.Value;

    private static LeafContext ContextResolving(
        SearchParameterInfo parameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds));

    private static SearchParameterInfo IdentifierParameter()
        => new("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));

    [Fact]
    public void GivenACodeOnlyToken_WhenLowered_ThenComparesCodeColumnOnly()
    {
        // Arrange — bare "code" (System is null): Code equality only, no system constraint
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 44), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)44);
        cte.ResourceTypeId.ShouldBe((short)103);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Code");
        equal.Value.Value.ShouldBe("true");
    }

    [Fact]
    public void GivenASystemMustBeAbsentToken_WhenLowered_ThenComparesSystemIdIsNullAndCode()
    {
        // Arrange — "|code" (System is empty string): SystemId IS NULL AND Code = @code
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, TokenSearchValue.Parse("|12345"));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var isNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        isNull.Column.Column.ShouldBe("SystemId");
        var codeEqual = and.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code");
        codeEqual.Value.Value.ShouldBe("12345");
    }

    [Fact]
    public void GivenASystemOnlyToken_WhenLowered_ThenComparesSystemIdOnly()
    {
        // Arrange — "system|" (non-empty System, empty Code): SystemId equality only
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org/mrn", code: "", text: null));
        var systemIds = new Dictionary<string, int?> { ["http://example.org/mrn"] = 77 };

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55, systemIds), 103);

        // Assert
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("SystemId");
        equal.Value.Value.ShouldBe(77);
    }

    [Fact]
    public void GivenASystemQualifiedToken_WhenLowered_ThenComparesSystemIdAndCode()
    {
        // Arrange — "system|code" (non-empty System, non-empty Code): both
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org/mrn", code: "12345", text: null));
        var systemIds = new Dictionary<string, int?> { ["http://example.org/mrn"] = 77 };

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55, systemIds), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var systemEqual = and.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId");
        systemEqual.Value.Value.ShouldBe(77);
        var codeEqual = and.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code");
        codeEqual.Value.Value.ShouldBe("12345");
    }

    [Fact]
    public void GivenAnUnknownSystem_WhenLowered_ThenReturnsFalsePredicate()
    {
        // Arrange — non-empty System where SystemId returns null: Predicate.False
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://unknown.org/system", code: "12345", text: null));
        var systemIds = new Dictionary<string, int?> { ["http://unknown.org/system"] = null };

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55, systemIds), 103);

        // Assert
        cte.Predicate.ShouldBeOfType<Predicate.False>();
    }

    // The token row generators (TokenSearchParameterRowGenerator and every Token* composite generator)
    // split at the Code column's declared width and write the REMAINDER to CodeOverflow — the opposite of
    // StringSearchParam, whose TextOverflow holds the whole value. The three boundary cases below pin
    // length-1 / length / length+1 against that split, exactly as StringLoweringRuleTests does for
    // TextOverflow.
    [Fact]
    public void GivenACodeOneBelowTheInlineWidth_WhenLowered_ThenComparesCodeColumnWithNoOverflowGuard()
    {
        // Arrange — strictly below the split, so a truncated prefix (always exactly the split width)
        // can never equal it; the guard would only cost sargability.
        var parameter = IdentifierParameter();
        var shortCode = new string('A', InlineCodeWidth - 1);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: shortCode, text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Table.ShouldBe("TokenSearchParam");
        equal.Column.Column.ShouldBe("Code");
        equal.Value.Value.ShouldBe(shortCode);
    }

    [Fact]
    public void GivenACodeAtTheInlineWidth_WhenLowered_ThenGuardsOnCodeOverflowIsNull()
    {
        // Arrange — exactly the split; without the IsNull guard an overflowed row whose truncated Code
        // prefix equals this value would false-positive match.
        var parameter = IdentifierParameter();
        var exactCode = new string('A', InlineCodeWidth);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: exactCode, text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert — And(IsNull(CodeOverflow), Equal(Code, value))
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var isNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        isNull.Column.Table.ShouldBe("TokenSearchParam");
        isNull.Column.Column.ShouldBe("CodeOverflow");
        var equal = and.Right.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Code");
        equal.Value.Value.ShouldBe(exactCode);
    }

    [Fact]
    public void GivenACodeOneAboveTheInlineWidth_WhenLowered_ThenComparesBothHalvesAgainstTheRemainderSplit()
    {
        // Arrange — exceeds the split, so the value exists only as (prefix, remainder) across two
        // columns; comparing either alone cannot match it.
        var parameter = IdentifierParameter();
        var longCode = new string('A', InlineCodeWidth) + "B";
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: longCode, text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert — And(Equal(Code, prefix), Equal(CodeOverflow, remainder))
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var codeEqual = and.Left.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code");
        codeEqual.Value.Value.ShouldBe(new string('A', InlineCodeWidth));
        var overflowEqual = and.Right.ShouldBeOfType<Predicate.Equal>();
        overflowEqual.Column.Column.ShouldBe("CodeOverflow");
        overflowEqual.Value.Value.ShouldBe("B");
    }

    [Fact]
    public void GivenAnOverflowedCode_WhenEvaluatedAgainstTheRowTheGeneratorWouldWrite_ThenItMatches()
    {
        // Arrange — the generator writes Code = the leading InlineCodeWidth characters and CodeOverflow =
        // the remainder. Before the overflow column was threaded through, the emitted predicate compared
        // the whole code against Code alone and such a row could never match.
        var parameter = IdentifierParameter();
        var longCode = new string('A', InlineCodeWidth) + new string('B', 40);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: longCode, text: null));
        var storedRow = new Dictionary<string, object>
        {
            ["Code"] = longCode[..InlineCodeWidth],
            ["CodeOverflow"] = longCode[InlineCodeWidth..],
        };
        var differentTailRow = new Dictionary<string, object>
        {
            ["Code"] = longCode[..InlineCodeWidth],
            ["CodeOverflow"] = new string('C', 40),
        };

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert — matches the row it was written from, and is not satisfied by a shared prefix alone
        PredicateRowEvaluator.Matches(cte.Predicate!, storedRow).ShouldBeTrue();
        PredicateRowEvaluator.Matches(cte.Predicate!, differentTailRow).ShouldBeFalse();
    }

    [Fact]
    public void GivenACodeAtTheInlineWidth_WhenEvaluatedAgainstAnOverflowedRowWithTheSamePrefix_ThenTheGuardRejectsIt()
    {
        // Arrange — the false positive the IsNull(CodeOverflow) guard exists to prevent
        var parameter = IdentifierParameter();
        var exactCode = new string('A', InlineCodeWidth);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: exactCode, text: null));
        var exactRow = new Dictionary<string, object> { ["Code"] = exactCode };
        var overflowedRow = new Dictionary<string, object>
        {
            ["Code"] = exactCode,
            ["CodeOverflow"] = "extra",
        };

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert
        PredicateRowEvaluator.Matches(cte.Predicate!, exactRow).ShouldBeTrue();
        PredicateRowEvaluator.Matches(cte.Predicate!, overflowedRow).ShouldBeFalse();
    }

    [Fact]
    public void GivenAnImplementedModifierFreeToken_WhenLowered_ThenLowersWithoutThrowing()
    {
        // Arrange — the unmodified form is the only one this rule implements
        var parameter = IdentifierParameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "12345", text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103);

        // Assert
        cte.Predicate.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe("12345");
    }

    public static TheoryData<SearchModifierCode> UnimplementedModifiers() => new()
    {
        SearchModifierCode.Text,
        SearchModifierCode.In,
        SearchModifierCode.NotIn,
        SearchModifierCode.OfType,
        SearchModifierCode.Above,
        SearchModifierCode.Below,
        SearchModifierCode.Identifier,
    };

    [Theory]
    [MemberData(nameof(UnimplementedModifiers))]
    public void GivenAnUnimplementedModifier_WhenLowered_ThenThrowsNamingTheModifier(SearchModifierCode modifier)
    {
        // Arrange — each of these needs a different table or a terminology expansion this compiler does
        // not perform; degrading them to plain equality would return wrong rows silently.
        var parameter = IdentifierParameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(modifier), new TokenSearchValue(system: null, code: "12345", text: null));

        // Act
        var exception = Should.Throw<NotSupportedException>(() =>
            TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55), 103));

        // Assert
        exception.Message.ShouldContain($":{modifier}");
    }

    [Fact]
    public void GivenATextOnlyToken_WhenLowered_ThenThrows()
    {
        // Arrange — text-only token (no system, no code): retain NotSupportedException
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: null, text: "foo"));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 44), 103));
    }
}
