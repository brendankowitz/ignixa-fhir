using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Encodes/decodes a <see cref="KeysetPosition"/> as an opaque continuation token. Not interchangeable with
/// Ignixa.Search.Models.ContinuationToken (an offset+count token for the legacy path) — keyset and offset are
/// different models. A token minted before a cutover goes stale and the client restarts from page 1, which is
/// acceptable.
/// </summary>
public static class KeysetContinuationToken
{
    /// <summary>The most boundary values a token can carry, matching the sort-key cap Lower.BuildSortSpec enforces.</summary>
    private const int MaxBoundaryValues = 3;

    /// <summary>
    /// Encodes a resume position, including the sort phase it was reached in. Rejects a boundary value that is
    /// not well-formed UTF-16: JSON serialization would substitute U+FFFD for a lone surrogate, minting a token
    /// that decodes to a coordinate the client never reached, which skips or repeats rows at the page seam.
    /// </summary>
    public static string Encode(KeysetPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        for (var i = 0; i < position.BoundaryValues.Count; i++)
        {
            var value = position.BoundaryValues[i];
            if (value is null)
            {
                throw new ArgumentException(
                    $"Boundary value {i} is null. A boundary carries one sort-key value per active key; a missing " +
                    "value is substituted with a sentinel before it reaches a token.",
                    nameof(position));
            }

            if (ContainsUnpairedSurrogate(value))
            {
                throw new ArgumentException(
                    $"Boundary value {i} contains an unpaired UTF-16 surrogate, which cannot round-trip through " +
                    "the token's JSON encoding. The decoded boundary would differ from the row it was read from.",
                    nameof(position));
            }
        }

        var state = new TokenState
        {
            BoundaryValues = [.. position.BoundaryValues],
            BoundaryResourceTypeId = position.BoundaryResourceTypeId,
            BoundarySurrogateId = position.BoundarySurrogateId,
            Phase = position.Phase,
        };
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decodes a previously encoded position, returning false for anything this compiler did not mint. A token
    /// is untrusted client input, so every field is refused rather than defaulted when it is absent or out of
    /// range: each of the three carries a coordinate whose zero value is not neutral. An absent phase resumes
    /// in the wrong segment; an absent surrogate id seeks past <c>Sid1 > 0</c>, which admits every tied row;
    /// an absent type id seeks past <c>T1 > 0</c>, which admits every resource type. All three replay rows the
    /// client has already seen, silently, in a page it believes is the next one.
    /// </summary>
    public static bool TryDecode(string token, [NotNullWhen(true)] out KeysetPosition? position)
    {
        position = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(token);
            var json = Encoding.UTF8.GetString(bytes);
            var state = JsonSerializer.Deserialize<TokenState>(json);

            var values = state?.BoundaryValues;
            var typeId = state?.BoundaryResourceTypeId;
            var surrogateId = state?.BoundarySurrogateId;
            var phase = state?.Phase;

            if (values is null
                || values.Length > MaxBoundaryValues
                || Array.IndexOf(values, null) >= 0
                || typeId is null or <= 0
                || surrogateId is null or <= 0
                || phase is null
                || !Enum.IsDefined(phase.Value))
            {
                return false;
            }

            position = new KeysetPosition(values!, typeId.Value, surrogateId.Value, phase.Value);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the string contains a high or low surrogate that is not part of a valid pair. Such a string
    /// is a legal .NET/SQL Server nvarchar value but cannot survive JSON encoding intact.
    /// </summary>
    private static bool ContainsUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
            {
                return true;
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// Every field is nullable so an absent one is distinguishable from a legitimately encoded zero or an
    /// explicit <see cref="SortPhase.Valued"/>. A producer whose serializer uses a different naming policy
    /// binds some fields and silently zeroes the rest; <see cref="TryDecode"/> refuses that token outright.
    /// </summary>
    private sealed class TokenState
    {
        public string[]? BoundaryValues { get; set; }

        public short? BoundaryResourceTypeId { get; set; }

        public long? BoundarySurrogateId { get; set; }

        public SortPhase? Phase { get; set; }
    }
}
