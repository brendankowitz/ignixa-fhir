// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

internal static class UnknownReasons
{
    /// <summary>The reasons of a determined answer: shared, read-only and empty.</summary>
    public static IReadOnlyList<UnknownReason> None { get; } = Array.AsReadOnly(Array.Empty<UnknownReason>());

    /// <summary>The reasons of an undetermined answer that has exactly one.</summary>
    public static IReadOnlyList<UnknownReason> Single(UnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return Array.AsReadOnly([reason]);
    }

    /// <summary>
    /// A defensive, read-only copy of the reasons for an undetermined answer. An unknown answer must say why,
    /// so the list must be non-empty and contain no nulls. The copy is wrapped so a caller cannot cast it back
    /// to the backing array or list and change the reasons behind the answer's equality.
    /// </summary>
    public static IReadOnlyList<UnknownReason> Copy(IReadOnlyList<UnknownReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Count == 0)
        {
            throw new ArgumentException("An undetermined answer requires at least one reason.", nameof(reasons));
        }

        if (reasons.Any(reason => reason is null))
        {
            throw new ArgumentException("Reasons must not contain null.", nameof(reasons));
        }

        return Array.AsReadOnly(reasons.ToArray());
    }

    /// <summary>A hash that covers every reason in order, consistent with sequence equality.</summary>
    public static int Hash(IReadOnlyList<UnknownReason> reasons)
    {
        var hash = new HashCode();
        foreach (var reason in reasons)
        {
            hash.Add(reason);
        }

        return hash.ToHashCode();
    }
}
