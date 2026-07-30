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
        position.BoundaryResourceTypeId.ShouldBe((short)103);
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
        position.BoundaryResourceTypeId.ShouldBe((short)103);
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

    [Fact]
    public void GivenAKnownPosition_WhenEncoded_ThenTheWireFormatIsExactlyTheGoldenVector()
    {
        // One private TokenState serves both Encode and TryDecode, so a renamed field round-trips through
        // itself and every other test still passes -- while tokens minted by the previous build decode with
        // that field silently zeroed. Only a literal vector pins the format across versions.
        var token = KeysetContinuationToken.Encode(
            new KeysetPosition(["Adams"], 103, 5000L, SortPhase.Valued));

        Encoding.UTF8.GetString(Convert.FromBase64String(token))
            .ShouldBe("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":5000,"Phase":0}""");
    }

    [Fact]
    public void GivenTheGoldenVector_WhenDecoded_ThenItStillRoundTripsToTheSamePosition()
    {
        // The other half of the pin: a token minted by an earlier build must still decode. Together these two
        // turn any wire-format change into a failing test rather than a silent replay in production.
        var decoded = KeysetContinuationToken.TryDecode(
            Base64Json("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":5000,"Phase":0}"""),
            out var position);

        decoded.ShouldBeTrue();
        position!.BoundaryValues.ShouldBe(["Adams"]);
        position.BoundaryResourceTypeId.ShouldBe((short)103);
        position.BoundarySurrogateId.ShouldBe(5000L);
        position.Phase.ShouldBe(SortPhase.Valued);
    }

    [Theory]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundarySurrogateId":5000,"Phase":0}""")]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"Phase":0}""")]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"boundarySurrogateId":5000,"Phase":0}""")]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":0,"BoundarySurrogateId":5000,"Phase":0}""")]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":0,"Phase":0}""")]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":-1,"BoundarySurrogateId":5000,"Phase":0}""")]
    [InlineData("""{"BoundaryValues":["Adams"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":-42,"Phase":0}""")]
    public void GivenATokenWhoseCoordinateIsAbsentOrNonPositive_WhenDecoded_ThenItIsRefused(string json)
    {
        // Zero is not a neutral default for either coordinate: EmitSeekPredicate renders `Sid1 > 0`, which
        // admits every row tied at the boundary sort value, so the client silently replays a page it has
        // already read. A camelCase field name from a foreign producer binds nothing and lands in the same
        // place, which is why the absent case and the misspelt case are pinned side by side.
        var decoded = KeysetContinuationToken.TryDecode(Base64Json(json), out var position);

        decoded.ShouldBeFalse();
        position.ShouldBeNull();
    }

    [Fact]
    public void GivenATokenCarryingANullBoundaryValue_WhenDecoded_ThenItIsRefused()
    {
        // A null element survives into IReadOnlyList<string> despite the annotation, becomes a NULL SQL
        // parameter, and makes every seek comparison UNKNOWN -- zero rows, Succeeded true, and a client that
        // concludes the result set is exhausted while silently losing the remainder.
        var decoded = KeysetContinuationToken.TryDecode(
            Base64Json("""{"BoundaryValues":[null],"BoundaryResourceTypeId":103,"BoundarySurrogateId":5000,"Phase":0}"""),
            out var position);

        decoded.ShouldBeFalse();
        position.ShouldBeNull();
    }

    [Fact]
    public void GivenATokenCarryingMoreValuesThanSortKeysAreAllowed_WhenDecoded_ThenItIsRefused()
    {
        // _sort caps at 3 keys, so no plan this compiler produces can consume a 4-value boundary.
        var decoded = KeysetContinuationToken.TryDecode(
            Base64Json("""{"BoundaryValues":["a","b","c","d"],"BoundaryResourceTypeId":103,"BoundarySurrogateId":5000,"Phase":0}"""),
            out var position);

        decoded.ShouldBeFalse();
        position.ShouldBeNull();
    }

    [Fact]
    public void GivenABoundaryValueWithAnUnpairedSurrogate_WhenEncoded_ThenItThrowsRatherThanMintingAWrongToken()
    {
        // nvarchar permits a lone surrogate, but JSON serialization substitutes U+FFFD for it rather than
        // throwing. The token would decode to a boundary the client never reached, skipping or repeating rows
        // at the page seam -- so it is refused where the fault is, not where the rows go missing.
        Should.Throw<ArgumentException>(() => KeysetContinuationToken.Encode(
            new KeysetPosition([new string([(char)0xD800, 'x'])], 103, 5000L, SortPhase.Valued)));
    }

    private static string Base64Json(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}