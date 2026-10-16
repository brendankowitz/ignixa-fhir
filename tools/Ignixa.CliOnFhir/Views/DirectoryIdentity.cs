// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Text;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// A directory as the file system knows it, by device and inode rather than by path. A bind mount (or a second mount) of the data
/// folder is the same directory under another path that no link leads to, so no path comparison sees it; its device and inode are
/// the folder's. Read with Linux <c>statx</c>, whose buffer has the same layout on every architecture. Elsewhere, or where the C
/// library or the kernel has no <c>statx</c>, it is not <see cref="IsAvailable"/>.
/// </summary>
internal readonly record struct DirectoryIdentity(uint DeviceMajor, uint DeviceMinor, ulong Inode)
{
    /// <summary>Whether identities can be read on this system.</summary>
    public static bool IsAvailable { get; } = OperatingSystem.IsLinux() && Probe();

    /// <summary>The identity of the directory or file at <paramref name="path"/> (links followed); null when nothing is there.</summary>
    /// <exception cref="PlatformNotSupportedException">Not <see cref="IsAvailable"/>.</exception>
    /// <exception cref="IOException">The file system would not say (for example, permission denied).</exception>
    public static DirectoryIdentity? Of(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Directory identities cannot be read on this operating system.");
        }

        byte[] buffer = new byte[NativeMethods.StatxSize];
        if (NativeMethods.statx(NativeMethods.AtFdCwd, NativeMethods.CString(path), 0, NativeMethods.StatxIno, buffer) == 0)
        {
            return Read(buffer);
        }

        int errno = Marshal.GetLastPInvokeError();
        return errno is NativeMethods.ENOENT or NativeMethods.ENOTDIR
            ? null
            : throw new IOException($"statx failed with errno {errno}, so the directory cannot be compared with the data folder.");
    }

    /// <summary>
    /// The first of <paramref name="path"/> and its ancestors that is the directory <paramref name="folder"/> is, by identity; null
    /// when none is. Components that do not exist yet are skipped.
    /// </summary>
    /// <exception cref="IOException">An identity could not be read.</exception>
    public static string? SameAsOrUnder(string path, DirectoryIdentity folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Of(current) == folder)
            {
                return current;
            }
        }

        return null;
    }

    private static DirectoryIdentity Read(byte[] buffer) =>
        new(BitConverter.ToUInt32(buffer, NativeMethods.DevMajorOffset), BitConverter.ToUInt32(buffer, NativeMethods.DevMinorOffset), BitConverter.ToUInt64(buffer, NativeMethods.InoOffset));

    // A C library without statx (glibc before 2.28) or a kernel or sandbox that refuses it leaves only the path checks.
    private static bool Probe()
    {
        try
        {
            return NativeMethods.statx(NativeMethods.AtFdCwd, NativeMethods.CString("/"), 0, NativeMethods.StatxIno, new byte[NativeMethods.StatxSize]) == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        public const int AtFdCwd = -100;
        public const uint StatxIno = 0x100;
        public const int ENOENT = 2;
        public const int ENOTDIR = 20;

        // struct statx (linux/stat.h): 256 bytes; stx_ino at 32, stx_dev_major at 136, stx_dev_minor at 140.
        public const int StatxSize = 256;
        public const int InoOffset = 32;
        public const int DevMajorOffset = 136;
        public const int DevMinorOffset = 140;

        [DllImport("libc", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int statx(int dirfd, byte[] pathname, int flags, uint mask, [Out] byte[] statxbuf);

        // A path as the C library takes it: UTF-8, ended by a NUL.
        public static byte[] CString(string path) => Encoding.UTF8.GetBytes(path + '\0');
    }
}
