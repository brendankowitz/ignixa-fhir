// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record CodeRef
{
    public CodeRef(string system, string code, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        System = system;
        Code = code;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public string System { get; }

    public string Code { get; }

    public string? Version { get; }

    public CanonicalRef CodeSystem => new(System, Version);

    public override string ToString() => $"{CodeSystem}#{Code}";
}
