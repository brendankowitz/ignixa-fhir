// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;

namespace Ignixa.Search.Models;

/// <summary>
/// Helper for encoding/decoding continuation tokens for paginated search results.
/// Token format: Base64-encoded JSON with offset and count.
/// </summary>
public static class ContinuationToken
{
    /// <summary>
    /// Encodes pagination state into a continuation token.
    /// </summary>
    /// <param name="offset">The offset (number of results skipped).</param>
    /// <param name="count">The page size (_count parameter).</param>
    /// <returns>Base64-encoded token string.</returns>
    public static string Encode(int offset, int count)
    {
        var state = new PaginationState
        {
            Offset = offset,
            Count = count
        };

        string json = JsonSerializer.Serialize(state);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decodes a continuation token into pagination state.
    /// </summary>
    /// <param name="token">The Base64-encoded token string.</param>
    /// <param name="offset">The decoded offset value.</param>
    /// <param name="count">The decoded count value.</param>
    /// <returns>True if decoding succeeded, false otherwise.</returns>
    public static bool TryDecode(string token, out int offset, out int count)
    {
        offset = 0;
        count = 10;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(token);
            string json = Encoding.UTF8.GetString(bytes);
            var state = JsonSerializer.Deserialize<PaginationState>(json);

            if (state == null)
            {
                return false;
            }

            offset = state.Offset;
            count = state.Count;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Internal state model for continuation tokens.
    /// </summary>
    private class PaginationState
    {
        public int Offset { get; set; }
        public int Count { get; set; }
    }
}
