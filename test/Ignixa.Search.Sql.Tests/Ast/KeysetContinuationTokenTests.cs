using System.Text;
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
        var token = KeysetContinuationToken.Encode(
            new KeysetPosition(["Adams"], 103, 5000L, SortPhase.Valued));

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var position);

        // Assert
        decoded.ShouldBeTrue();
        position!.BoundaryValues.ShouldBe(["Adams"]);
        position.BoundaryResourceTypeId.ShouldBe(103);
        position.BoundarySurrogateId.ShouldBe(5000L);
        position.Phase.ShouldBe(SortPhase.Valued);
    }

    [Fact]
    public void GivenMultipleBoundaryValues_WhenEncodedThenDecoded_ThenRoundTripsExactly()
    {
        // Arrange
        var token = KeysetContinuationToken.Encode(
            new KeysetPosition(["Zorro", "2000-01-01T00:00:00.0000000"], 103, 9000L, SortPhase.Valued));

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var position);

        // Assert
        decoded.ShouldBeTrue();
        position!.BoundaryValues.ShouldBe(["Zorro", "2000-01-01T00:00:00.0000000"]);
        position.BoundaryResourceTypeId.ShouldBe(103);
        position.BoundarySurrogateId.ShouldBe(9000L);
    }

    [Fact]
    public void GivenAMissingPrimaryPosition_WhenEncodedThenDecoded_ThenTheSegmentSurvivesTheRoundTrip()
    {
        // Arrange -- the whole point of carrying the phase: a caller resuming from this token alone must land
        // back in the missing segment, not restart the valued one.
        var token = KeysetContinuationToken.Encode(
            new KeysetPosition([], 103, 7000L, SortPhase.MissingPrimary));

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var position);

        // Assert
        decoded.ShouldBeTrue();
        position!.Phase.ShouldBe(SortPhase.MissingPrimary);
        position.BoundaryValues.ShouldBeEmpty();
        position.BoundarySurrogateId.ShouldBe(7000L);
    }

    [Fact]
    public void GivenBothPhasesAtTheSameBoundary_WhenEncoded_ThenTheTokensDiffer()
    {
        // A boundary is meaningless without its segment, so the encoding must not collapse the two.
        var valued = KeysetContinuationToken.Encode(new KeysetPosition(["Adams"], 103, 5000L, SortPhase.Valued));
        var missing = KeysetContinuationToken.Encode(new KeysetPosition(["Adams"], 103, 5000L, SortPhase.MissingPrimary));

        valued.ShouldNotBe(missing);
    }

    [Fact]
    public void GivenATokenMintedBeforeThePhaseWasCarried_WhenDecoded_ThenItIsRefusedRatherThanDefaulted()
    {
        // Arrange -- a pre-cutover token has no Phase field. Deserialisation would give Valued, which is a
        // real segment, so the client would silently resume in the wrong one. Refusing restarts it at page 1.
        var stale = Base64Json("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":5000}""");

        // Act
        var decoded = KeysetContinuationToken.TryDecode(stale, out var position);

        // Assert
        decoded.ShouldBeFalse();
        position.ShouldBeNull();
    }

    [Fact]
    public void GivenACraftedTokenWithAnOutOfRangePhase_WhenDecoded_ThenItIsRefused()
    {
        // Arrange -- a token is client input, so the phase is attacker-controlled. Any value but MissingPrimary
        // reads the valued segment, so an unchecked cast would hand back rows the client has already paged.
        var crafted = Base64Json("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":5000,"Phase":7}""");

        // Act
        var decoded = KeysetContinuationToken.TryDecode(crafted, out var position);

        // Assert
        decoded.ShouldBeFalse();
        position.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("dGhpcyBpcyBub3QgSlNPTg==")]
    public void GivenAMalformedToken_WhenDecoded_ThenReturnsFalseWithoutThrowing(string malformed)
    {
        // Act
        var decoded = KeysetContinuationToken.TryDecode(malformed, out var position);

        // Assert
        decoded.ShouldBeFalse();
        position.ShouldBeNull();
    }

    private static string Base64Json(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}