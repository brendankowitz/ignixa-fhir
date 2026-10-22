// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record CanonicalRef
{
    public CanonicalRef(string url, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Url = url;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public string Url { get; }

    public string? Version { get; }

    public static CanonicalRef Parse(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        int bar = canonical.LastIndexOf('|');
        return bar < 0
            ? new CanonicalRef(canonical)
            : new CanonicalRef(canonical[..bar], canonical[(bar + 1)..]);
    }

    public override string ToString() => Version is null ? Url : $"{Url}|{Version}";
}
