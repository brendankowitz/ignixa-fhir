// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// A search query as a response repeats it (in facts, and in <c>next</c>): whole up to <see cref="MaxLength"/> characters;
/// a longer one is cut there, with an <c>omitted</c> item giving its length and digest, and is never repeated in <c>next</c>,
/// so a huge query cannot push a response past the size cap.
/// </summary>
internal static class QueryEcho
{
    /// <summary>The longest query repeated whole.</summary>
    public const int MaxLength = 2048;

    /// <summary>Whether <paramref name="query"/> is too long to repeat whole.</summary>
    public static bool IsCut(string? query) => query is { Length: > MaxLength };

    /// <summary><paramref name="query"/> as the response repeats it: whole, or its first <see cref="MaxLength"/> characters and <c>…</c>.</summary>
    public static string Of(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!IsCut(query))
        {
            return query;
        }

        // Never split a surrogate pair.
        int length = char.IsHighSurrogate(query[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return query[..length] + "…";
    }

    /// <summary>The <c>omitted</c> item for a cut echo of <paramref name="query"/> at <paramref name="where"/> (a facts path).</summary>
    public static OmittedItem Cut(string query, string where)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new OmittedItem($"{where} past its first {MaxLength} characters (the query is {query.Length} characters; digest {FhirIds.Digest(query)})", "size-budget");
    }

    /// <summary>The <c>omitted</c> item for a <c>next</c> call left out because it would repeat a query too long to repeat.</summary>
    public static OmittedItem NextLeftOut(string what) =>
        new($"next: {what} (it would repeat a query longer than {MaxLength} characters; send the same request again with the changed arguments)", "size-budget");
}
