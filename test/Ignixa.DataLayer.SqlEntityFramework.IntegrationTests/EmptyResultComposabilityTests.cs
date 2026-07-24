// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Search;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Pins that the empty result a query generator returns for an unmatchable search stays composable with the
/// set operators the search modifiers lower to.
/// </summary>
/// <remarks>
/// <para>
/// The generators used to return <c>Enumerable.Empty&lt;long&gt;().AsQueryable()</c>, an empty set carrying the
/// in-memory <c>EnumerableQuery</c> provider. Enumerating it directly works, so a test asserting only "the
/// result is empty" passes -- and every test did. <c>:not</c> lowers to <c>Except</c> and comma-OR to
/// <c>Union</c>, and EF rejects a set operation whose second source is an empty inline root: "Empty
/// collections are not supported as inline query roots." Composition itself succeeds; the throw lands when
/// the query is compiled, which for a search is after the 200 and the bundle header are already written, so
/// the client saw a truncated body rather than an error.
/// </para>
/// <para>
/// The assertion is therefore translation, not enumeration, and the provider must be SqlServer -- the
/// in-memory provider performs no SQL translation and would report success either way. No connection is
/// opened: <c>ToQueryString</c> compiles the query and returns SQL without one.
/// </para>
/// </remarks>
public sealed class EmptyResultComposabilityTests : IDisposable
{
    private readonly FhirDbContext _context;

    public EmptyResultComposabilityTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer("Server=localhost;Database=NeverOpened;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        _context = new FhirDbContext(options);
    }

    public static TheoryData<string> SetOperators() => new()
    {
        "Except",
        "Union",
        "Intersect",
        "Concat",
    };

    [Theory]
    [MemberData(nameof(SetOperators))]
    public void GivenAnEmptyResultSet_WhenCombinedWithASetOperator_ThenTheQueryTranslates(string setOperator)
    {
        // Arrange
        var matched = _context.Resources.Where(r => !r.IsHistory).Select(r => r.ResourceSurrogateId);
        var empty = _context.EmptyResourceIds();

        // Act
        var combined = setOperator switch
        {
            "Except" => matched.Except(empty),
            "Union" => matched.Union(empty),
            "Intersect" => matched.Intersect(empty),
            "Concat" => matched.Concat(empty),
            _ => throw new ArgumentOutOfRangeException(nameof(setOperator)),
        };

        // Assert
        Should.NotThrow(() => combined.ToQueryString(), $"an unmatchable search must survive {setOperator}");
    }

    [Fact]
    public void GivenAnEmptyResultSetAsTheReceiver_WhenCombinedWithASetOperator_ThenTheQueryTranslates()
    {
        // Arrange — Union is order-independent in SQL but not in EF's inline-root check, so pin both sides.
        var matched = _context.Resources.Where(r => !r.IsHistory).Select(r => r.ResourceSurrogateId);

        // Act
        var combined = _context.EmptyResourceIds().Union(matched);

        // Assert
        Should.NotThrow(() => combined.ToQueryString());
    }

    [Fact]
    public void GivenAnEmptyResultSet_WhenTranslated_ThenItSelectsNoRows()
    {
        // Arrange & Act
        var sql = _context.EmptyResourceIds().ToQueryString();

        // Assert — the predicate must be unsatisfiable rather than absent, or "matches nothing" would
        // silently become "matches everything" the moment it is composed into a wider query.
        sql.ShouldContain("WHERE");
    }

    [Fact]
    public void GivenTheInMemoryEmptyQueryable_WhenCombinedWithExcept_ThenItFailsToTranslate()
    {
        // Arrange — the shape the generators used to return. Pinned so the reason the helper exists stays
        // visible: this is what "just return an empty queryable" actually does to a composed search.
        var matched = _context.Resources.Where(r => !r.IsHistory).Select(r => r.ResourceSurrogateId);
        var inMemoryEmpty = Enumerable.Empty<long>().AsQueryable();

        // Act
        var combined = matched.Except(inMemoryEmpty);

        // Assert
        Should.Throw<InvalidOperationException>(() => combined.ToQueryString());
    }

    public void Dispose() => _context.Dispose();
}
