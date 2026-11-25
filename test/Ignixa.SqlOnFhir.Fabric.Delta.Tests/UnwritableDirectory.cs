// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.AccessControl;
using System.Security.Principal;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Makes one directory refuse new files until disposed, so a Delta commit into <c>_delta_log</c> fails after the data
/// files are written. On Windows a non-inherited deny ACE for the current user; elsewhere the owner's write bit is
/// cleared. Neither works for every account (root ignores mode bits), so <see cref="IsEffective"/> reports a probe.
/// </summary>
internal sealed class UnwritableDirectory : IDisposable
{
    private readonly string _path;
    private readonly FileSystemAccessRule? _deny;
    private readonly UnixFileMode? _originalMode;

    public UnwritableDirectory(string path)
    {
        _path = path;
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current user has no SID.");
            _deny = new FileSystemAccessRule(user, FileSystemRights.CreateFiles, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            security.AddAccessRule(_deny);
            directory.SetAccessControl(security);
        }
        else
        {
            _originalMode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, _originalMode.Value & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        }

        IsEffective = !CanCreateFile(path);
    }

    /// <summary>Whether a probe file could not be created, so the injection really blocks commits.</summary>
    public bool IsEffective { get; }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && _deny is not null)
        {
            var directory = new DirectoryInfo(_path);
            var security = directory.GetAccessControl();
            security.RemoveAccessRule(_deny);
            directory.SetAccessControl(security);
        }
        else if (!OperatingSystem.IsWindows() && _originalMode is { } mode)
        {
            File.SetUnixFileMode(_path, mode);
        }
    }

    private static bool CanCreateFile(string directory)
    {
        var probe = Path.Combine(directory, $"probe-{Guid.NewGuid():N}");
        try
        {
            File.Create(probe).Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        File.Delete(probe);
        return true;
    }
}
