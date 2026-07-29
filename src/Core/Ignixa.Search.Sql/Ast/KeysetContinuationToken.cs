using System.Text;
using System.Text.Json;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Encodes/decodes a keyset-pagination continuation token for this compiler's <see cref="PageSpec"/> shape.
/// Not interchangeable with Ignixa.Search.Models.ContinuationToken (an offset+count token for the legacy
/// path) — keyset and offset are different models. A token minted before a cutover goes stale and the
/// client restarts from page 1, which is acceptable.
/// </summary>
public static class KeysetContinuationToken
{
    /// <summary>
    /// Encodes a boundary. Always writes <paramref name="resourceTypeId"/>, so every token carries a type
    /// component — see <see cref="TryDecode"/> for the caveat that matters to consumers of the decoded value.
    /// </summary>
    public static string Encode(IReadOnlyList<string> boundaryValues, int resourceTypeId, long surrogateId)
    {
        var state = new TokenState
        {
            BoundaryValues = [.. boundaryValues],
            BoundaryResourceTypeId = resourceTypeId,
            BoundarySurrogateId = surrogateId,
        };
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decodes a previously encoded boundary. <paramref name="resourceTypeId"/> is always populated on success,
    /// but a token-to-<see cref="PageSpec"/> adapter must discard it (map to null) when the sort is custom:
    /// PageSpec rejects a typed boundary there and the type is redundant (ResourceSurrogateId is globally unique).
    /// </summary>
    public static bool TryDecode(string token, out IReadOnlyList<string> boundaryValues, out int resourceTypeId, out long surrogateId)
    {
        boundaryValues = [];
        resourceTypeId = 0;
        surrogateId = 0;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(token);
            var json = Encoding.UTF8.GetString(bytes);
            var state = JsonSerializer.Deserialize<TokenState>(json);
            if (state is null || state.BoundaryValues is null)
            {
                return false;
            }

            boundaryValues = state.BoundaryValues;
            resourceTypeId = state.BoundaryResourceTypeId;
            surrogateId = state.BoundarySurrogateId;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed class TokenState
    {
        public string[]? BoundaryValues { get; set; }
        public int BoundaryResourceTypeId { get; set; }
        public long BoundarySurrogateId { get; set; }
    }
}
