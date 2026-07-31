using Ignixa.Search.Indexing;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tests;

/// <summary>
/// A hand-maintained <see cref="SortKeyKind"/> to parameter-code table, guarded so a new kind cannot be
/// added without a decision, and cross-checked against <see cref="IntrinsicSearchParameters.Codes"/>.
/// </summary>
public class IntrinsicSortKeyAgreementTests
{
    /// <summary>
    /// Every <see cref="SortKeyKind"/> mapped to the code it is produced from, or to null when the kind is
    /// backed by a search-parameter table rather than by an intrinsic parameter.
    /// </summary>
    private static readonly Dictionary<SortKeyKind, string?> CodeByKind = new()
    {
        [SortKeyKind.String] = null,
        [SortKeyKind.Date] = null,
        [SortKeyKind.Aggregated] = null,
        [SortKeyKind.LastUpdated] = SearchParameterNames.LastUpdated,
        [SortKeyKind.ResourceType] = SearchParameterNames.ResourceType,
        [SortKeyKind.ResourceId] = SearchParameterNames.Id,
    };

    [Fact]
    public void GivenANewSortKeyKind_WhenClassified_ThenItMustBeAccountedForHere()
    {
        // Arrange, Act, Assert: a kind added without a decision about whether it is intrinsically backed
        // fails here rather than silently defaulting to "needs a SearchParamId".
        Enum.GetValues<SortKeyKind>().ShouldAllBe(kind => CodeByKind.ContainsKey(kind));
    }

    [Fact]
    public void GivenTheIntrinsicallyBackedSortKeyKinds_WhenComparedToTheCodes_ThenTheyAgree()
    {
        // Arrange: SortKeyKind draws the same distinction after lowering — a key backed by an intrinsic
        // parameter carries no SearchParamId.
        HashSet<string> kindCodes = CodeByKind.Values.OfType<string>().ToHashSet(StringComparer.Ordinal);

        // Act, Assert: set equality, so a code added to either side alone fails — and, unlike a count
        // comparison, so does mapping two kinds to the same code.
        kindCodes.SetEquals(IntrinsicSearchParameters.Codes).ShouldBeTrue(
            "A code was added to IntrinsicSearchParameters.Codes without a matching SortKeyKind, or vice versa.");
    }
}
