// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>
/// A search reached a live resource whose sidecar search index was written in a format this version does not read
/// (typically by an earlier build). The resource's versions, history and reads are unaffected; saving the resource again
/// rewrites its index in the current format. A server fault, not the caller's.
/// </summary>
/// <remarks>
/// The message names no file: it reaches the client through the API's generic error handling (a 500), while every
/// affected sidecar's path is logged server-side. Deliberately not an <see cref="InvalidOperationException"/>, which the
/// API reports as the client's mistake (400).
/// </remarks>
public sealed class SearchIndexReindexRequiredException : Exception
{
    public SearchIndexReindexRequiredException()
        : this("Stored search indexes were written in an older format; save the affected resources again to rebuild them. The server log names them.")
    {
    }

    public SearchIndexReindexRequiredException(string message)
        : base(message)
    {
    }

    public SearchIndexReindexRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
