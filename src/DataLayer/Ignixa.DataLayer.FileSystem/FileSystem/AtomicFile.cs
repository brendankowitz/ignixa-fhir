// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.FileSystem.FileSystem;

/// <summary>Writes a file so that a reader sees either the previous content or the whole new content.</summary>
internal static partial class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to a temporary file beside <paramref name="path"/> and renames it into place. The
    /// temporary name ends in <c>.tmp</c>, so it matches no pattern the store reads. If the write or the rename fails or
    /// is cancelled, the temporary file is deleted and the original failure rethrown unchanged, cancellation included; a
    /// temporary file that cannot be deleted is logged with its path and left behind.
    /// </summary>
    public static async Task WriteAllTextAsync(string path, string contents, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logger);

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Flushed to disk before the rename, so after a crash the file is never empty or torn (readers would treat
            // that as an unreadable sidecar). The rename itself is not made durable (on Linux that needs an fsync of the
            // directory), so a crash can still leave the previous content in place.
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(new UTF8Encoding(false).GetBytes(contents), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA1849 // FlushAsync has no flush-to-disk overload; the synchronous call is the durability barrier.
                stream.Flush(flushToDisk: true);
#pragma warning restore CA1849
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                // Deleting a file that was never created is a no-op.
                File.Delete(temporaryPath);
            }
            catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException)
            {
                // The write's own failure is what the caller must see; the leftover file is only reported.
                LogTemporaryFileLeft(logger, deleteFailure, temporaryPath);
            }

            throw;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Could not delete the temporary file {TemporaryPath} after a failed write; it is left behind and matches no pattern the store reads.")]
    private static partial void LogTemporaryFileLeft(ILogger logger, Exception exception, string temporaryPath);
}
