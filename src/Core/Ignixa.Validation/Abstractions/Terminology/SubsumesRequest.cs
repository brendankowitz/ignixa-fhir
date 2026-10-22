// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record SubsumesRequest
{
    public SubsumesRequest(CodeRef codeA, CodeRef codeB)
    {
        ArgumentNullException.ThrowIfNull(codeA);
        ArgumentNullException.ThrowIfNull(codeB);
        if (codeA.CodeSystem != codeB.CodeSystem)
        {
            throw new ArgumentException($"$subsumes compares codes from one code system; got '{codeA.CodeSystem}' and '{codeB.CodeSystem}'.", nameof(codeB));
        }

        CodeA = codeA;
        CodeB = codeB;
    }

    public CodeRef CodeA { get; }

    public CodeRef CodeB { get; }

    public CanonicalRef CodeSystem => CodeA.CodeSystem;
}
