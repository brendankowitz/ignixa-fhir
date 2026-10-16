// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Where an open file really is, asked of the operating system by its handle rather than worked out from a path string: a path
/// can lead somewhere else a moment after it was checked (a directory swapped for a link), the file a handle is open on cannot.
/// On Linux it is the link <c>/proc/self/fd/&lt;fd&gt;</c>, on Windows <c>GetFinalPathNameByHandleW</c>. Elsewhere (macOS, where
/// it would take <c>fcntl(F_GETPATH)</c>, a variadic call .NET cannot make portably) it is not <see cref="IsAvailable"/>.
/// </summary>
internal static class OpenFilePath
{
    // What Linux appends to the target of /proc/self/fd/<fd> once the file has no name left.
    private const string DeletedSuffix = " (deleted)";

    private const string LinuxDescriptors = "/proc/self/fd";

    /// <summary>Whether <see cref="Of"/> can tell where a file is on this system.</summary>
    public static bool IsAvailable { get; } = OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && Directory.Exists(LinuxDescriptors));

    /// <summary>
    /// The absolute path, links resolved, at which the file <paramref name="handle"/> is open on can be reached now; null when it no
    /// longer has one (it was deleted). On Linux a path that ends in <c> (deleted)</c> reads as deleted, which a temporary file of this
    /// tool (<c>.tmp</c>) never does.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Not <see cref="IsAvailable"/>.</exception>
    /// <exception cref="IOException">The operating system could not say.</exception>
    public static string? Of(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Where an open file is cannot be asked of this operating system.");
        }

        return OperatingSystem.IsWindows() ? OfWindows(handle) : OfLinux(handle);
    }

    /// <summary>Whether two resolved paths name the same place: case is ignored on Windows, where the file system ignores it.</summary>
    public static bool Same(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? OfLinux(SafeFileHandle handle)
    {
        // On Unix the handle is the file descriptor itself.
        string link = $"{LinuxDescriptors}/{handle.DangerousGetHandle()}";
        string target = new FileInfo(link).LinkTarget ?? throw new IOException($"'{link}' is not a link, so where the file is cannot be read.");
        return target.EndsWith(DeletedSuffix, StringComparison.Ordinal) ? null : target;
    }

    private static string OfWindows(SafeFileHandle handle)
    {
        const string Prefix = @"\\?\";
        const string UncPrefix = @"\\?\UNC\";
        char[] buffer = new char[1024];
        while (true)
        {
            uint length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, NativeMethods.FileNameNormalized);
            if (length == 0)
            {
                throw new IOException($"GetFinalPathNameByHandleW failed with error {Marshal.GetLastPInvokeError()}.");
            }

            if (length < buffer.Length)
            {
                string path = new(buffer, 0, (int)length);
                return path.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase) ? @"\\" + path[UncPrefix.Length..]
                    : path.StartsWith(Prefix, StringComparison.Ordinal) ? path[Prefix.Length..]
                    : path;
            }

            // Too small: the length returned is the size needed, terminator included.
            buffer = new char[length];
        }
    }

    private static class NativeMethods
    {
        // FILE_NAME_NORMALIZED | VOLUME_NAME_DOS: the drive-letter path, links and junctions resolved.
        public const uint FileNameNormalized = 0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);
    }
}
