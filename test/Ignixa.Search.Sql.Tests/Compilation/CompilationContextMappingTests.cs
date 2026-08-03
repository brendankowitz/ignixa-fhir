using Ignixa.Search.Models;
using Ignixa.Search.Sql.Compilation;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class CompilationContextMappingTests
{
    [Fact]
    public void GivenEverySearchOptionsProperty_WhenCreatingCompilationContext_ThenEachIsMappedOrExplicitlyExcluded()
    {
        var classified = CompilationContextMapping.Mapped
            .Concat(CompilationContextMapping.NotApplicable.Keys)
            .ToHashSet(StringComparer.Ordinal);

        typeof(SearchOptions).GetProperties()
            .Select(p => p.Name)
            .Where(name => !classified.Contains(name))
            .ShouldBeEmpty(
                "every SearchOptions property must be mapped into CompilationContext or listed in " +
                "CompilationContextMapping.NotApplicable with a stated reason");
    }

    [Fact]
    public void GivenTheMappingTable_WhenReadingIt_ThenNoPropertyIsBothMappedAndNotApplicable()
    {
        CompilationContextMapping.Mapped
            .Where(CompilationContextMapping.NotApplicable.ContainsKey)
            .ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheMappingTable_WhenReadingIt_ThenEveryClassifiedNameIsARealSearchOptionsProperty()
    {
        var real = typeof(SearchOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        CompilationContextMapping.Mapped
            .Concat(CompilationContextMapping.NotApplicable.Keys)
            .Where(name => !real.Contains(name))
            .ShouldBeEmpty("a stale entry hides a real gap");
    }

    [Fact]
    public void GivenTheNotApplicableTable_WhenReadingIt_ThenEveryReasonIsStated()
    {
        CompilationContextMapping.NotApplicable
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .ShouldBeEmpty();
    }
}
