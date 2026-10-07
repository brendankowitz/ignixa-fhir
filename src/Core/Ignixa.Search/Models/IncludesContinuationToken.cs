// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text;
using System.Text.Json;

namespace Ignixa.Search.Models;

/// <summary>
/// Helper for encoding/decoding continuation tokens for paginated $includes results.
/// Lives beside <see cref="ContinuationToken"/> so a data layer can read the include window it must serve.
/// </summary>
public static class IncludesContinuationToken
{
    /// <summary>
    /// The largest include offset a token may carry. A SQL data layer reads every include up to
    /// offset + page size, so this bounds the rows a single forged token can make it read.
    /// </summary>
    public const int MaxAllowedOffset = 100_000;

    private const int MaxAllowedPageSize = 1000;
    private const int MinAllowedPageSize = 1;

    /// <summary>
    /// Encodes pagination state into an includes continuation token.
    /// </summary>
    /// <param name="includesOffset">The offset for include entries (number of includes skipped).</param>
    /// <param name="pageSize">The page size (_includesCount parameter).</param>
    /// <returns>Base64-encoded token string.</returns>
    public static string Encode(int includesOffset, int pageSize)
    {
        var state = new IncludesPaginationState
        {
            IncludesOffset = includesOffset,
            PageSize = pageSize
        };

        string json = JsonSerializer.Serialize(state);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decodes an includes continuation token into pagination state.
    /// Includes validation to prevent DoS attacks via malicious token values.
    /// </summary>
    /// <param name="token">The Base64-encoded token string.</param>
    /// <param name="includesOffset">The decoded includes offset value.</param>
    /// <param name="pageSize">The decoded page size value.</param>
    /// <returns>True if decoding succeeded and values are valid, false otherwise.</returns>
    public static bool TryDecode(string token, out int includesOffset, out int pageSize)
    {
        includesOffset = 0;
        pageSize = 10;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        IncludesPaginationState? state;
        try
        {
            byte[] bytes = Convert.FromBase64String(token);
            string json = Encoding.UTF8.GetString(bytes);
            state = JsonSerializer.Deserialize<IncludesPaginationState>(json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }

        if (state == null)
        {
            return false;
        }

        if (state.IncludesOffset < 0 || state.IncludesOffset > MaxAllowedOffset)
        {
            return false;
        }

        if (state.PageSize < MinAllowedPageSize || state.PageSize > MaxAllowedPageSize)
        {
            return false;
        }

        includesOffset = state.IncludesOffset;
        pageSize = state.PageSize;
        return true;
    }

    private sealed class IncludesPaginationState
    {
        public int IncludesOffset { get; set; }
        public int PageSize { get; set; }
    }
}
