using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class KeysetContinuationTokenTests
{
    [Fact]
    public void GivenASingleBoundaryValue_WhenEncodedThenDecoded_ThenRoundTripsExactly()
    {
        // Arrange
        var token = KeysetContinuationToken.Encode(["Adams"], resourceTypeId: 103, surrogateId: 5000L);

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeTrue();
        boundaryValues.ShouldBe(["Adams"]);
        resourceTypeId.ShouldBe(103);
        surrogateId.ShouldBe(5000L);
    }

    [Fact]
    public void GivenMultipleBoundaryValues_WhenEncodedThenDecoded_ThenRoundTripsExactly()
    {
        // Arrange
        var token = KeysetContinuationToken.Encode(["Zorro", "2000-01-01T00:00:00.0000000"], resourceTypeId: 103, surrogateId: 9000L);

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeTrue();
        boundaryValues.ShouldBe(["Zorro", "2000-01-01T00:00:00.0000000"]);
        resourceTypeId.ShouldBe(103);
        surrogateId.ShouldBe(9000L);
    }

    [Fact]
    public void GivenAZeroBoundaryValueToken_WhenEncodedThenDecoded_ThenRoundTripsAnEmptyList()
    {
        // Arrange -- a MissingPrimary-phase first page has no boundary values at all.
        var token = KeysetContinuationToken.Encode([], resourceTypeId: 103, surrogateId: 7000L);

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeTrue();
        boundaryValues.ShouldBeEmpty();
        resourceTypeId.ShouldBe(103);
        surrogateId.ShouldBe(7000L);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("dGhpcyBpcyBub3QgSlNPTg==")]
    public void GivenAMalformedToken_WhenDecoded_ThenReturnsFalseWithoutThrowing(string malformed)
    {
        // Act
        var decoded = KeysetContinuationToken.TryDecode(malformed, out _, out _, out _);

        // Assert
        decoded.ShouldBeFalse();
    }
}
