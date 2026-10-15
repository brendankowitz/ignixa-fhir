// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Npm;

/// <summary>A package archive or directory that cannot be read: corrupt, over a size limit, or without a usable package.json.</summary>
public sealed class PackageFormatException : Exception
{
    public PackageFormatException()
    {
    }

    public PackageFormatException(string message)
        : base(message)
    {
    }

    public PackageFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
