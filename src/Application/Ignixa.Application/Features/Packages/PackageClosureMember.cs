namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Typed outcome for one package observed during a dependency-closure load.
/// Entries are not unique per package because each observation is reported separately.
/// </summary>
public sealed record PackageClosureMember(
    string PackageId,
    string Version,
    PackageClosureMemberStatus Status,
    string? Reason = null)
{
    /// <summary>
    /// Gets a value indicating whether this observation means the requested closure is not fully present.
    /// </summary>
    public bool IsBlocking => Status is PackageClosureMemberStatus.VersionConflict
        or PackageClosureMemberStatus.LoadFailed
        or PackageClosureMemberStatus.ManifestUnavailable;
}
