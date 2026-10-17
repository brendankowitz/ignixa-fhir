// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Text;

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// A rename that never replaces what is at the destination, in one step. .NET's <c>File.Move</c> without overwrite does it on Linux
/// as <c>link()</c> then <c>unlink()</c>, two steps with the file under both names between them; on Linux this asks the kernel for
/// <c>renameat2(RENAME_NOREPLACE)</c>, which is atomic. Where that call is missing (an old C library) or the file system does not take
/// it, and on other systems, it falls back to <c>File.Move</c>.
/// </summary>
internal static class NoReplaceRename
{
    /// <summary>Whether the last <see cref="Move"/> on this system could use <c>renameat2</c>; true until a call shows otherwise.</summary>
    public static bool IsAtomic { get; private set; } = OperatingSystem.IsLinux();

    /// <summary>Renames <paramref name="source"/> to <paramref name="destination"/>, which must not exist.</summary>
    /// <exception cref="IOException">Something is at <paramref name="destination"/> already, or the rename failed.</exception>
    public static void Move(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        if (IsAtomic && TryRenameAt2(source, destination))
        {
            return;
        }

        File.Move(source, destination, overwrite: false);
    }

    // True when renameat2 did the rename; false when it is not available here, for the fallback to do it.
    private static bool TryRenameAt2(string source, string destination)
    {
        int result;
        try
        {
            result = NativeMethods.renameat2(NativeMethods.AtFdCwd, CString(source), NativeMethods.AtFdCwd, CString(destination), NativeMethods.RenameNoReplace);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            IsAtomic = false;
            return false;
        }

        if (result == 0)
        {
            return true;
        }

        int errno = Marshal.GetLastPInvokeError();
        if (errno is NativeMethods.ENOSYS or NativeMethods.EINVAL)
        {
            IsAtomic = false;
            return false;
        }

        throw errno == NativeMethods.EEXIST
            ? new IOException($"'{destination}' exists, so nothing was renamed over it.")
            : new IOException($"renameat2 failed with errno {errno}.");
    }

    private static byte[] CString(string path) => Encoding.UTF8.GetBytes(path + '\0');

    private static class NativeMethods
    {
        public const int AtFdCwd = -100;
        public const uint RenameNoReplace = 1;
        public const int EEXIST = 17;
        public const int EINVAL = 22;
        public const int ENOSYS = 38;

        [DllImport("libc", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int renameat2(int olddirfd, byte[] oldpath, int newdirfd, byte[] newpath, uint flags);
    }
}
