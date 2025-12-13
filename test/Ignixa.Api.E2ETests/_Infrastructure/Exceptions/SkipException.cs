// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.E2ETests._Infrastructure.Exceptions;

/// <summary>
/// Exception thrown to skip a test when required capabilities are not supported by the server.
/// Xunit will mark the test as "Skipped" instead of "Failed".
/// </summary>
internal class SkipException : Exception
{
    public SkipException(string reason)
        : base(reason)
    {
    }

    public SkipException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}
