using Ignixa.Search.Indexing;
using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests;

/// <summary>
/// Pins the compiler's post-lowering sort-key classification to the storage-agnostic intrinsic set.
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
        string[] kindCodes = CodeByKind.Values.OfType<string>().ToArray();

        // Act, Assert: both directions, so a fourth code added to one side without the other fails.
        kindCodes.ShouldAllBe(code => IntrinsicSearchParameters.IsIntrinsicCode(code));
        IntrinsicSearchParameters.Codes.Count.ShouldBe(
            kindCodes.Length,
            "A code was added to IntrinsicSearchParameters.Codes without a matching SortKeyKind, or vice versa.");
    }
}
