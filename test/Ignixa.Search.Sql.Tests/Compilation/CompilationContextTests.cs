using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class CompilationContextTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenAnEmptyResourceType_WhenCreatingTheContext_ThenItIsNormalizedToNullAndTheSearchIsSystemLevel()
    {
        var context = CompilationContext.Create(new SearchOptions(), string.Empty, new SearchPlanOptions(), ReferenceTime);

        context.TargetResourceType.ShouldBeNull();
        context.SystemLevelSearch.ShouldBeTrue();
    }

    [Fact]
    public void GivenResourceVersionTypesNone_WhenCreatingTheContext_ThenItThrows()
    {
        var searchOptions = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.None };

        Should.Throw<NotSupportedException>(
            () => CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime));
    }

    [Fact]
    public void GivenResourceVersionTypesLatest_WhenCreatingTheContext_ThenVisibilityIsNull()
    {
        var searchOptions = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.Latest };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldBeNull();
    }

    [Fact]
    public void GivenResourceVersionTypesHistory_WhenCreatingTheContext_ThenVisibilityIncludesHistory()
    {
        var searchOptions = new SearchOptions
        {
            ResourceVersionTypes = ResourceVersionTypes.Latest | ResourceVersionTypes.History,
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldNotBeNull();
        context.Visibility!.IncludeHistory.ShouldBeTrue();
        context.Visibility.IncludeDeleted.ShouldBeFalse();
    }

    [Fact]
    public void GivenOnlyAStartSurrogateId_WhenCreatingTheContext_ThenItThrows()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1 };

        Should.Throw<NotSupportedException>(
            () => CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime));
    }

    [Fact]
    public void GivenBothAnExplicitRangeAndSearchOptionsBounds_WhenCreatingTheContext_ThenTheExplicitRangeWins()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1, EndSurrogateId = 2 };
        var options = new SearchPlanOptions { SurrogateRange = (10, 20) };

        var context = CompilationContext.Create(searchOptions, "Patient", options, ReferenceTime);

        context.SurrogateRange.ShouldNotBeNull();
        context.SurrogateRange!.Start.Value.ShouldBe(10L);
        context.SurrogateRange.End.Value.ShouldBe(20L);
    }

    [Fact]
    public void GivenAnOperationExpression_WhenCreatingTheContext_ThenItReplacesTheSearchExpressionWithoutMutatingSearchOptions()
    {
        var param = new SearchParameterInfo("x", "x", SearchParamType.String, new Uri("http://example.com/x"));
        Expression searchExpression = Expression.MissingSearchParameter(param, isMissing: true);
        var searchOptions = new SearchOptions { Expression = searchExpression };
        Expression operationExpression = Expression.MissingSearchParameter(param, isMissing: false);
        var options = new SearchPlanOptions { OperationExpression = operationExpression };

        var context = CompilationContext.Create(searchOptions, "Patient", options, ReferenceTime);

        context.Expression.ShouldBeSameAs(operationExpression);
        searchOptions.Expression.ShouldBeSameAs(searchExpression);
    }
}
