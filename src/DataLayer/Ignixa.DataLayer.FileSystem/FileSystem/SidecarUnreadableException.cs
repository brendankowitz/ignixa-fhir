// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>
/// A stored resource's metadata sidecar could not be read: the store is damaged, or was written by a format this
/// version does not read. A server fault, not the caller's.
/// </summary>
/// <remarks>
/// The message names no file: it reaches the client through the API's generic error handling (a 500), while the
/// sidecar's path is logged server-side when the read fails. The cause is the inner exception. Deliberately not an
/// <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>, which the API reports as the client's
/// mistake (400).
/// </remarks>
public sealed class SidecarUnreadableException : Exception
{
    public SidecarUnreadableException()
        : this("A stored resource's metadata could not be read; the server log names the file.")
    {
    }

    public SidecarUnreadableException(string message)
        : base(message)
    {
    }

    public SidecarUnreadableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
