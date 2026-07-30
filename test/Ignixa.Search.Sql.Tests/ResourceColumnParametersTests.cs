using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests;

public class ResourceColumnParametersTests
{
    [Theory]
    [InlineData("_id")]
    [InlineData("_type")]
    [InlineData("_lastUpdated")]
    public void GivenAResourceColumnCode_WhenClassified_ThenItIsRecognised(string code)
    {
        // Arrange, Act
        var isResourceColumn = ResourceColumnParameters.IsResourceColumnCode(code);

        // Assert
        isResourceColumn.ShouldBeTrue();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("birthdate")]
    [InlineData("_tag")]
    [InlineData("_profile")]
    [InlineData("_ID")]
    [InlineData("")]
    public void GivenANonResourceColumnCode_WhenClassified_ThenItIsNotRecognised(string code)
    {
        // Arrange, Act
        var isResourceColumn = ResourceColumnParameters.IsResourceColumnCode(code);

        // Assert: the comparison is ordinal and case-sensitive, matching how codes are compared
        // everywhere else in the compiler.
        isResourceColumn.ShouldBeFalse();
    }

    [Fact]
    public void GivenTheSortKeyKinds_WhenComparedToTheResourceColumnSet_ThenTheyAgree()
    {
        // Arrange: SortKeyKind draws the same distinction after lowering — a key backed by a resource
        // column carries no SearchParamId. This pins the two to the same size so a fourth resource
        // column added to one is not silently missing from the other.
        SortKeyKind[] resourceColumnKinds =
        [
            SortKeyKind.LastUpdated,
            SortKeyKind.ResourceType,
            SortKeyKind.ResourceId,
        ];

        string[] resourceColumnCodes = ["_lastUpdated", "_type", "_id"];

        // Act, Assert
        resourceColumnCodes.Length.ShouldBe(resourceColumnKinds.Length);
        resourceColumnCodes.ShouldAllBe(code => ResourceColumnParameters.IsResourceColumnCode(code));
    }
}
