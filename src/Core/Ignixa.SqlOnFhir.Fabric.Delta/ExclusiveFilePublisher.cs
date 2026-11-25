// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Publishes a file atomically and only if its name is still free: readers see either nothing or the complete content,
/// and an existing file is never replaced.
/// </summary>
/// <remarks>
/// <para>
/// The content is staged to a hidden sibling (<c>.&lt;name&gt;.&lt;guid&gt;.tmp</c>), written through to disk, then given its
/// final name. On Windows that is <c>MoveFileEx</c> without <c>MOVEFILE_REPLACE_EXISTING</c>, an atomic no-replace
/// rename. On Unix <see cref="File.Move(string, string, bool)"/> is not race-free (it checks the destination and then
/// calls <c>rename</c>, which replaces), so the final name is created with POSIX <c>link</c>, which fails with
/// <c>EEXIST</c> instead of replacing. A file system without hard links fails the publish rather than falling back to a
/// replacing rename. The staging file is removed on every path.
/// </para>
/// <para>Staging names follow the Delta convention for log temp files, which readers ignore.</para>
/// </remarks>
internal static partial class ExclusiveFilePublisher
{
    private const string StagingSuffix = ".tmp";

    // EEXIST is 17 on Linux and macOS.
    private const int UnixFileExists = 17;

    // HRESULTs of ERROR_FILE_EXISTS and ERROR_ALREADY_EXISTS.
    private const int WindowsFileExists = unchecked((int)0x80070050);
    private const int WindowsAlreadyExists = unchecked((int)0x800700B7);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> unless a file already has that name. The parent
    /// directory must exist.
    /// </summary>
    /// <returns><see langword="true"/> if this call created the file; <see langword="false"/> if the name was taken.</returns>
    /// <exception cref="OperationCanceledException">Canceled before the file was published; nothing was published.</exception>
    /// <exception cref="IOException">Staging or publishing failed for a reason other than the name being taken.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not writable.</exception>
    internal static async Task<bool> TryPublishAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var stagingPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{StagingSuffix}");

        try
        {
            // WriteThrough (O_SYNC on Unix) makes the content durable before it gets its final name.
            await using (var stream = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return OperatingSystem.IsWindows() ? MoveIfAbsent(stagingPath, path) : LinkIfAbsent(stagingPath, path);
        }
        finally
        {
            File.Delete(stagingPath);
        }
    }

    /// <summary>Whether <paramref name="fileName"/> is a hidden log temp file, such as a staging file of this class.</summary>
    internal static bool IsStagingFileName(string fileName) =>
        fileName.StartsWith('.') && fileName.EndsWith(StagingSuffix, StringComparison.Ordinal);

    private static bool MoveIfAbsent(string stagingPath, string path)
    {
        try
        {
            File.Move(stagingPath, path, overwrite: false);
            return true;
        }
        catch (IOException exception) when (exception.HResult is WindowsFileExists or WindowsAlreadyExists)
        {
            return false;
        }
    }

    private static bool LinkIfAbsent(string stagingPath, string path)
    {
        if (Link(stagingPath, path) == 0)
        {
            return true;
        }

        var errno = Marshal.GetLastPInvokeError();
        if (errno == UnixFileExists)
        {
            return false;
        }

        var error = new Win32Exception(errno);
        throw new IOException($"Publishing the file failed: link() returned errno {errno} ({error.Message}).", error);
    }

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Link(string existingPath, string newPath);
}
