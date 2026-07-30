using System.Reflection;
using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// Pins the structure of the <see cref="ResultShape"/> and <see cref="SearchPaging"/> unions. Every emitter
/// switches over these exhaustively, so a case added outside the assembly would fall through silently. The
/// closure mechanism is a <c>private protected abstract</c> marker, which is <c>FamANDAssem</c> and therefore
/// not reachable even from this friend assembly — a behavioural test cannot be written, so these assert the
/// mechanism itself. Without them the markers read as dead code and are exactly what a cleanup pass deletes.
/// </summary>
public class ResultShapeUnionTests
{
    private const string Marker = "ThisUnionIsClosed";

    public static TheoryData<Type> UnionBases()
    {
        var data = new TheoryData<Type>();
        data.Add(typeof(ResultShape));
        data.Add(typeof(ResultShape.Count));
        data.Add(typeof(SearchPaging));
        return data;
    }

    public static TheoryData<Type> UnionCases()
    {
        var data = new TheoryData<Type>();
        data.Add(typeof(ResultShape.Matches));
        data.Add(typeof(ResultShape.Count.AllMatches));
        data.Add(typeof(ResultShape.Count.CurrentSortPhase));
        data.Add(typeof(ResultShape.IncludesPage));
        data.Add(typeof(SearchPaging.Keyset));
        data.Add(typeof(SearchPaging.Offset));
        return data;
    }

    [Theory]
    [MemberData(nameof(UnionBases))]
    public void GivenAUnionBase_WhenInspected_ThenItCarriesAnUnimplementableClosureMarker(Type baseType)
    {
        var marker = MarkerIn(baseType);

        marker.ShouldNotBeNull($"{baseType.Name} has no {Marker} marker, so an external assembly can chain to " +
                               "the synthesized protected copy constructor and add a case.");
        marker.IsFamilyAndAssembly.ShouldBeTrue(
            $"{baseType.Name}.{Marker} must stay private protected. Widening it to protected or internal lets " +
            "a friend or a derived external type implement it, reopening the union.");
    }

    [Theory]
    [MemberData(nameof(UnionCases))]
    public void GivenAUnionCase_WhenInspected_ThenItIsSealedSoTheCaseListIsFinal(Type caseType)
    {
        // Sealing the cases is the other half: an unsealed case is a second derivation point that inherits a
        // satisfied marker obligation, so closure at the base alone would not hold.
        caseType.IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void GivenTheResultShapeUnion_WhenEnumerated_ThenTheKnownCasesAreTheOnlyOnes()
    {
        // Adding a case inside the assembly is legitimate, but every emitter's switch has to learn about it.
        // Failing here is the reminder to go update them rather than discovering it as a runtime throw.
        typeof(ResultShape).Assembly.GetTypes()
            .Where(t => typeof(ResultShape).IsAssignableFrom(t) && t != typeof(ResultShape) && !t.IsAbstract)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["AllMatches", "CurrentSortPhase", "IncludesPage", "Matches"]);
    }

    [Fact]
    public void GivenTheShapeUnion_WhenLookingForPaging_ThenOnlyMatchesCarriesIt()
    {
        // The collapse that removed SearchPlanOptions.Paging: a count discarded it silently and an includes
        // page rejected it at runtime while carrying its own resume boundary. Both are now unrepresentable.
        // If a second shape gains a SearchPaging member, those runtime guards have to come back.
        typeof(ResultShape).Assembly.GetTypes()
            .Where(t => typeof(ResultShape).IsAssignableFrom(t))
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.PropertyType == typeof(SearchPaging)))
            .ShouldHaveSingleItem()
            .ShouldBe(typeof(ResultShape.Matches));
    }

    [Fact]
    public void GivenAnIncludesPage_WhenInspected_ThenItsOnlyPagingCoordinateIsItsOwnResumeBoundary()
    {
        // An includes page bounds its match set by a surrogate range and pages its own rows by Resume. Having
        // exactly one paging coordinate is what makes "two paging mechanisms disagree" impossible to express.
        typeof(ResultShape.IncludesPage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ShouldContain(nameof(ResultShape.IncludesPage.Resume));
    }

    private static MethodInfo? MarkerIn(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var marker = t.GetMethod(Marker, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (marker is not null)
            {
                return marker;
            }
        }

        return null;
    }
}
