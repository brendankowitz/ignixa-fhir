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
    /// <summary>Encodes a resume position, including the sort phase it was reached in.</summary>
    public static string Encode(KeysetPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

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
    /// is untrusted client input, so an absent phase (a pre-cutover token) and an out-of-range one (a crafted
    /// token) are both refused rather than defaulted — defaulting would silently resume in the wrong segment,
    /// replaying rows the client has already seen.
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
            if (state?.BoundaryValues is null || state.Phase is not { } phase || !Enum.IsDefined(phase))
            {
                return false;
            }

            position = new KeysetPosition(
                state.BoundaryValues,
                state.BoundaryResourceTypeId,
                state.BoundarySurrogateId,
                phase);
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

        /// <summary>Nullable so an absent field is distinguishable from an explicit <see cref="SortPhase.Valued"/>.</summary>
        public SortPhase? Phase { get; set; }
    }
}
