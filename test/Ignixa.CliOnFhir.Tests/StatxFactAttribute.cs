// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Output;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A test of directory identities: skipped, not passed, where statx cannot be called (not Linux, an old C library, a sandbox).</summary>
internal sealed class StatxFactAttribute : FactAttribute
{
    public StatxFactAttribute()
    {
        if (!DirectoryIdentity.IsAvailable)
        {
            Skip = "statx is not available here, so directory identities cannot be read.";
        }
    }
}
