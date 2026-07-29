using System.Text;
using System.Text.Json;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Encodes/decodes a keyset-pagination continuation token for this compiler's <see cref="PageSpec"/>
/// shape. Not compatible with, and not intended to bridge to, Ignixa.Search.Models.ContinuationToken
/// (an offset+count token for the legacy EF-based read path) -- keyset and offset pagination are
/// different models, not different formats of the same thing. A token minted before a cutover to the
/// keyset-based path simply goes stale; the client restarts from page 1, which is acceptable.
/// </summary>
public static class KeysetContinuationToken
{
    /// <summary>
    /// Encodes a boundary. Note <paramref name="resourceTypeId"/> is always written, so a decoded token
    /// always carries a type component — but <see cref="PageSpec"/> only accepts one when the sort is
    /// non-custom (<c>SqlBuilder.Run</c> rejects a typed boundary alongside a custom search-parameter
    /// <c>_sort</c>). Any future token-to-<see cref="PageSpec"/> adapter must therefore map to
    /// <c>BoundaryResourceTypeId: null</c> whenever the sort is custom, discarding the encoded type rather
    /// than forwarding it; the type is redundant there because <c>ResourceSurrogateId</c> is globally unique.
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
