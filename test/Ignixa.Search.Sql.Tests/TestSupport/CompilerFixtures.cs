using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>Pre-wired <see cref="SearchSqlCompiler"/> instances for the facade tests.</summary>
internal static class CompilerFixtures
{
    /// <summary>A resolver that knows <c>Patient</c> and the <c>name</c> search parameter.</summary>
    public static FakeSymbolResolver PatientResolver()
    {
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[PlanFixtures.NameParameter.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        return resolver;
    }

    /// <summary>A compiler that compiles <c>Patient?name:exact=Smith</c> successfully.</summary>
    public static SearchSqlCompiler ForPatient()
        => new(PatientResolver(), PatientOptionsBuilder());

    /// <summary>A compiler whose resolver finds nothing, so every parameter comes back unresolved.</summary>
    public static SearchSqlCompiler WithUnresolvableParameters()
        => new(new FakeSymbolResolver(), PatientOptionsBuilder());

    /// <summary>A compiler whose options builder throws a <see cref="BadSearchRequestException"/> from the build stage.</summary>
    public static SearchSqlCompiler WithThrowingOptionsBuilder()
    {
        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder
            .Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<ISchema?>(), Arg.Any<IList<ParameterTrace>?>())
            .Throws(new BadSearchRequestException("Unparseable search value."));

        return new SearchSqlCompiler(PatientResolver(), builder);
    }

    private static FakeSearchOptionsBuilder PatientOptionsBuilder()
    {
        var predicate = new SearchParameterPredicateExpression(
            PlanFixtures.NameParameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var expression = new SearchParameterExpression(PlanFixtures.NameParameter, predicate);

        return new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name:exact", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);
    }
}
