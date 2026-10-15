// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>Writes a file so that a reader sees either the previous content or the whole new content.</summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to a temporary file beside <paramref name="path"/> and renames it into place. The
    /// temporary name ends in <c>.tmp</c>, so it matches no pattern the store reads. If the write or the rename fails or
    /// is cancelled, the temporary file is deleted and the failure rethrown.
    /// </summary>
    public static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, contents, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // Deleting a file that was never created is a no-op; a delete that fails surfaces its own I/O error.
            File.Delete(temporaryPath);
            throw;
        }
    }
}
