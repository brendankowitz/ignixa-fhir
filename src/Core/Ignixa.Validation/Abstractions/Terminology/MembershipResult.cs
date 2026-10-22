// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record MembershipResult : ITerminologyAnswer
{
    private MembershipResult(Determination result, UnknownReason? unknown)
    {
        Result = result;
        Unknown = unknown;
    }

    public static MembershipResult Member { get; } = new(Determination.True, null);

    public static MembershipResult NotMember { get; } = new(Determination.False, null);

    public Determination Result { get; }

    public UnknownReason? Unknown { get; }

    public static MembershipResult Undetermined(UnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new(Determination.Unknown, reason);
    }
}
