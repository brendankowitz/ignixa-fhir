// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record UnknownReason
{
    public UnknownReason(UnknownKind kind, string subject, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Kind = kind;
        Subject = subject;
        Detail = detail;
    }

    public UnknownKind Kind { get; }

    public string Subject { get; }

    public string Detail { get; }
}
