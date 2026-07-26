using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

/// <summary>
/// Proves <see cref="SearchCompiler.CompileFromOptionsAsync"/> forwards <see cref="SearchOptions.AccessConstraints"/>
/// into the compiler rather than silently dropping them -- the same fail-open defect this branch's own review
/// caught when it found the property "connected to nothing." The query and the constraint deliberately share
/// <c>statusParam</c> but bind different codes ("final" vs. "amended"): the query alone resolves the parameter
/// through <see cref="SearchCompiler.CompileFromOptionsAsync"/>'s Resolve stage (AccessConstraints are not
/// themselves symbol-collected -- the constraint's parameter only resolves because the query already
/// references it), and "amended" reaching the emitted SQL can only happen if the constraint's own predicate
/// was lowered.
/// </summary>
public class CompileFromOptionsTests
{
    private const short ObservationTypeId = 104;
    private const short StatusParamId = 220;

    [Fact]
    public async Task GivenSearchOptionsCarryingAccessConstraints_WhenCompilingFromOptions_ThenTheEmittedSqlEnforcesThem()
    {
        // Arrange -- Observation?status=final, plus an access constraint status=amended on Observation.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions { ResourceType = "Observation", Expression = expression };
        options.AccessConstraints = [new AccessConstraint("Observation", TokenPredicate(statusParam, "amended"))];

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- the constraint's own bound value ("amended") must reach the emitted SQL: the query
        // itself only ever binds "final", so "amended" appearing at all proves the constraint's predicate
        // was lowered, not merely accepted by the API. Ctes.Count > 1 proves it was intersected into the
        // match set, not lowered and discarded.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Parameters.ShouldContain(p => Equals(p.Value, "amended"));
        trace.CompiledPlan.ShouldNotBeNull();
        trace.CompiledPlan!.Ctes.Count.ShouldBeGreaterThan(1);
    }

    /// <summary>A wrapped token predicate ("&lt;param&gt; eq &lt;code&gt;"), the shape a real bound leaf takes -- mirrors AccessConstraintTests' TokenPredicate.</summary>
    private static Expression TokenPredicate(SearchParameterInfo parameter, string code)
        => new SearchParameterExpression(parameter, TokenPredicateLeaf(parameter, code));

    private static SearchParameterPredicateExpression TokenPredicateLeaf(SearchParameterInfo parameter, string code)
        => new(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null));

    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];

        public Dictionary<string, short> ResourceTypeIds { get; } = [];

        public Dictionary<string, int> SystemIds { get; } = [];

        public Dictionary<string, int> QuantityCodeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
            => Task.FromResult(SystemIds.TryGetValue(system, out var id) ? (int?)id : null);

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(QuantityCodeIds.TryGetValue(code, out var id) ? (int?)id : null);
    }
}
