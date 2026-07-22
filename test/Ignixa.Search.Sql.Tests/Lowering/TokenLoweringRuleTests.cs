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

public class TokenLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo parameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds));

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
