// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Clock;

/// <summary>Reads a bounded byte range of a file, however large the file is or grows while it is read.</summary>
internal static class FileRegion
{
    /// <summary>
    /// The bytes of <paramref name="path"/> from <paramref name="offset"/>, at most <paramref name="maxCount"/>; fewer when the file ends
    /// first. <paramref name="truncated"/> says whether more followed.
    /// </summary>
    /// <exception cref="IOException">The file cannot be read; also <see cref="UnauthorizedAccessException"/>.</exception>
    public static byte[] Read(string path, long offset, int maxCount, out bool truncated)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[maxCount + 1];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        truncated = read > maxCount;
        return buffer.AsSpan(0, Math.Min(read, maxCount)).ToArray();
    }
}
