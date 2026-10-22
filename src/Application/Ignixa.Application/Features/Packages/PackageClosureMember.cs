namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Typed outcome for one package observed during a dependency-closure load.
/// A blocking member means the requested closure is not fully present.
/// </summary>
public sealed record PackageClosureMember(
    string PackageId,
    string Version,
    PackageClosureMemberStatus Status,
    string? Reason = null)
{
    public bool IsBlocking => Status is PackageClosureMemberStatus.VersionConflict
        or PackageClosureMemberStatus.LoadFailed
        or PackageClosureMemberStatus.ManifestUnavailable;
}
