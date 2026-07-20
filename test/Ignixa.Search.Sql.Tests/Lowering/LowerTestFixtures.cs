using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>Shared arrangement builders for lowering tests, mirroring the fixtures inlined in <see cref="LowerTests"/>.</summary>
public static class LowerTestFixtures
{
    /// <summary>A single string-typed leaf predicate ("name eq Smith") with its symbol table.</summary>
    public static (SearchParameterPredicateExpression Expression, SymbolTable Symbols) SingleStringPredicate()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        return (predicate, symbols);
    }

    /// <summary>A :not-modified predicate ("name:not eq Smith") wrapped in its SearchParameterExpression, with its symbol table.</summary>
    public static (SearchParameterExpression Wrapper, SearchParameterPredicateExpression Inner, SymbolTable Symbols) NotModifiedPredicate()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var inner = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new StringSearchValue("Smith"));
        var wrapper = new SearchParameterExpression(parameter, inner);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        return (wrapper, inner, symbols);
    }
}
