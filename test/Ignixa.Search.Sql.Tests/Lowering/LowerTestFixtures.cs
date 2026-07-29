using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>Shared arrangement builders for lowering tests, mirroring the fixtures inlined in <see cref="LowerTests"/>.</summary>
internal static class LowerTestFixtures
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

    /// <summary>An OR of two composite alternatives ("code-value-concept=A$1,B$2") wrapped in its SearchParameterExpression, with its symbol table.</summary>
    public static (SearchParameterExpression Wrapper, Expression Alternative1, Expression Alternative2, SymbolTable Symbols) OrOfCompositeAlternatives()
    {
        var compositeParameter = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParameter = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParameter = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        MultiaryExpression Alternative(string code, string value) => new(
            MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParameter, 0,
                    new SearchParameterPredicateExpression(codeParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null))),
                new CompositeComponentExpression(valueParameter, 1,
                    new SearchParameterPredicateExpression(valueParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: value, text: null))),
            ]);

        var alternative1 = Alternative("A", "1");
        var alternative2 = Alternative("B", "2");
        var wrapper = new SearchParameterExpression(compositeParameter, new MultiaryExpression(MultiaryOperator.Or, [alternative1, alternative2]));

        var symbols = new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = 301 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        return (wrapper, alternative1, alternative2, symbols);
    }
}
